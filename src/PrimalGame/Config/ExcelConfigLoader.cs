using System;
using System.IO;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using ExcelDataReader;
using ClosedXML.Excel;
using SlotFramework.Models;
using SlotFramework.Utilities;

namespace PrimalGame.Config;

public class ExcelConfigLoader
{
    public const string DefaultGoogleSheetUrl = "https://docs.google.com/spreadsheets/d/1vcIwTl9a7LCG2ZhFBJuVI7J3y8vttJwSJShcdSrCTFU/edit?usp=sharing";

    static ExcelConfigLoader()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public static PrimalConfig Load(string? filePathOrUrl = null)
    {
        // 1. If explicit online URL/ID is provided, load from Google Sheet directly
        if (!string.IsNullOrWhiteSpace(filePathOrUrl) && GoogleSheetDownloader.IsOnlineSource(filePathOrUrl))
        {
            using var onlineStream = GoogleSheetDownloader.DownloadStream(filePathOrUrl);
            return Load(onlineStream);
        }

        // 2. If explicit existing file path is provided, load it
        if (!string.IsNullOrEmpty(filePathOrUrl) && File.Exists(filePathOrUrl))
        {
            using var fileStream = File.Open(filePathOrUrl, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Load(fileStream);
        }

        // 3. Fallbacks: check local Downloads, then current dir
        string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "FirePrimalElephant95.xlsx");
        if (File.Exists(downloadsPath))
        {
            using var fileStream = File.Open(downloadsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Load(fileStream);
        }

        string localPath = "FirePrimalElephant95.xlsx";
        if (File.Exists(localPath))
        {
            using var fileStream = File.Open(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Load(fileStream);
        }

        // 4. Default: load directly from online Google Sheet
        using var defaultStream = GoogleSheetDownloader.DownloadStream(DefaultGoogleSheetUrl);
        return Load(defaultStream);
    }

    public static PrimalConfig Load(Stream stream)
    {
        var config = new PrimalConfig();

        using var reader = ExcelReaderFactory.CreateReader(stream);
        
        var dataSet = reader.AsDataSet();

        var dataTable = dataSet.Tables["Data"] ?? throw new Exception("Data sheet missing in configuration Excel");

        // Load symbols from Row 2 (index 1) to Row 16 (index 15)
        for (int r = 1; r < Math.Min(16, dataTable.Rows.Count); r++)
        {
            var row = dataTable.Rows[r];
            var cellValue = row[0]?.ToString();
            if (row[0] == DBNull.Value || string.IsNullOrWhiteSpace(cellValue)) continue;
            
            string name = cellValue.Trim();
            int symbolId = r - 1;
            
            bool isWild = name.Equals("Wild", StringComparison.OrdinalIgnoreCase);
            bool isScatter = false; // Will set scatter symbols dynamically or in future updates

            var sym = new Symbol(symbolId, name, isWild, isScatter);
            config.Symbols.Add(sym);

            if (isWild) config.WildSymbolId = symbolId;
            if (isScatter) config.ScatterSymbolId = symbolId;

            // Load payouts from Column B (index 1)
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var payValue = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(payValue))
                {
                    string payStr = payValue.Trim();
                    string[] parts = payStr.Split(',');
                    // Normal symbols have payouts for 3, 4, 5 of a kind (starting at 3).
                    // Symbols with 4 values (such as H1) start at 2 (payouts for 2, 3, 4, 5 of a kind).
                    int startMatch = (parts.Length == 4) ? 2 : 3;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (double.TryParse(parts[i].Trim(), out double multiplier))
                        {
                            int matchCount = startMatch + i;
                            long payoutInCents = (long)Math.Round(multiplier * 100);
                            config.Paytable.AddPayout(symbolId, matchCount, payoutInCents);
                            if (symbolId >= 0 && symbolId < 16 && matchCount >= 0 && matchCount < 6)
                            {
                                config.FastPaytableMatrix[symbolId, matchCount] = payoutInCents;
                            }
                        }
                    }
                }
            }
        }

        // Load Stage spins to next stage from Row 39 (index 38)
        if (dataTable.Rows.Count > 38)
        {
            var row39 = dataTable.Rows[38];
            if (row39.ItemArray.Length > 1 && row39[1] != DBNull.Value)
            {
                var spinsVal = row39[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(spinsVal))
                {
                    string[] parts = spinsVal.Split(',');
                    var spinsList = new List<int>();
                    foreach (var part in parts)
                    {
                        if (int.TryParse(part.Trim(), out int val))
                        {
                            spinsList.Add(val);
                        }
                    }
                    config.StageSpinsToNext = spinsList.ToArray();
                }
            }
        }

        // Load Base Game Stage Weights from Row 40 (index 39) to Row 46 (index 45)
        for (int r = 39; r < Math.Min(46, dataTable.Rows.Count); r++)
        {
            var row = dataTable.Rows[r];
            var stageNameVal = row[0]?.ToString();
            if (row[0] == DBNull.Value || string.IsNullOrWhiteSpace(stageNameVal)) continue;

            string stageName = stageNameVal.Trim();
            
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var weightsVal = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(weightsVal))
                {
                    string[] parts = weightsVal.Split(',');
                    var weights = new List<int>();
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (int.TryParse(parts[i].Trim(), out int w))
                        {
                            weights.Add(w);
                        }
                    }
                    config.BaseGameStageWeights[stageName] = weights.ToArray();
                }
            }
        }

        // Load Reelsets starting from Row 47 (index 46)
        int startRowIndex = 46;
        while (startRowIndex < 46 + 100 && startRowIndex < dataTable.Rows.Count)
        {
            var row = dataTable.Rows[startRowIndex];
            var reelsetNameVal = row[0]?.ToString();
            if (string.IsNullOrWhiteSpace(reelsetNameVal))
            {
                break;
            }

            string reelsetName = reelsetNameVal.Trim();
            int[][] reels = new int[5][];
            bool hasValidData = true;

            for (int r = 0; r < 5; r++)
            {
                int currRowIndex = startRowIndex + r;
                if (currRowIndex >= dataTable.Rows.Count)
                {
                    hasValidData = false;
                    break;
                }

                var rRow = dataTable.Rows[currRowIndex];
                var cellB = rRow[1]?.ToString();
                if (string.IsNullOrWhiteSpace(cellB))
                {
                    hasValidData = false;
                    break;
                }

                string[] parts = cellB.Split(',');
                var strip = new List<int>();
                foreach (var part in parts)
                {
                    if (int.TryParse(part.Trim(), out int symId))
                    {
                        strip.Add(symId);
                    }
                }
                reels[r] = strip.ToArray();
            }

            if (hasValidData)
            {
                config.Reelsets[reelsetName] = new ReelSet(reels);
            }

            startRowIndex += 5;
        }

        // Load Paylines from Row 18 (index 17) to Row 37 (index 36)
        var paylinesList = new List<int[]>();
        for (int r = 17; r < 37; r++)
        {
            if (r < dataTable.Rows.Count)
            {
                var row = dataTable.Rows[r];
                if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
                {
                    var lineConfigStr = row[1]?.ToString();
                    if (!string.IsNullOrWhiteSpace(lineConfigStr))
                    {
                        string[] parts = lineConfigStr.Split(',');
                        var lineCoordinates = new List<int>();
                        foreach (var part in parts)
                        {
                            if (int.TryParse(part.Trim(), out int val))
                            {
                                lineCoordinates.Add(val);
                            }
                        }
                        if (lineCoordinates.Count == 5)
                        {
                            paylinesList.Add(lineCoordinates.ToArray());
                        }
                    }
                }
            }
        }
        config.Paylines = paylinesList.ToArray();

        // Validate section row headers for configuration integrity
        ValidateRowHeader(dataTable, 146, "Fire Core");
        ValidateRowHeader(dataTable, 148, "Bonus");
        ValidateRowHeader(dataTable, 149, "Jackpot");
        ValidateRowHeader(dataTable, 152, "Pot");
        ValidateRowHeader(dataTable, 154, "Lock & Slingo");
        ValidateRowHeader(dataTable, 165, "Apex");
        ValidateRowHeader(dataTable, 204, "Colossal");
        ValidateRowHeader(dataTable, 243, "Primal Zone");

        // Load Fire Core values from Row 147 (index 146): Col B (Special for Reelsets 8,9,10), Col C (Default for other reelsets)
        if (dataTable.Rows.Count > 146)
        {
            var row147 = dataTable.Rows[146];
            // Col B: Special values for Reelsets 8, 9, 10
            if (row147.ItemArray.Length > 1 && row147[1] != DBNull.Value)
            {
                var valuesStr = row147[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(valuesStr))
                {
                    var valuesList = valuesStr.Split(',').Select(p => double.TryParse(p.Trim(), out double val) ? val : (double?)null).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                    config.FireCoreCashValuesSpecial = valuesList;
                }
            }
            // Col C: Default values for all other base game reelsets
            if (row147.ItemArray.Length > 2 && row147[2] != DBNull.Value)
            {
                var valuesStrC = row147[2]?.ToString();
                if (!string.IsNullOrWhiteSpace(valuesStrC))
                {
                    var valuesListC = valuesStrC.Split(',').Select(p => double.TryParse(p.Trim(), out double val) ? val : (double?)null).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                    config.FireCoreCashValuesDefault = valuesListC;
                }
            }
            if (config.FireCoreCashValuesDefault.Length == 0)
            {
                config.FireCoreCashValuesDefault = config.FireCoreCashValuesSpecial;
            }
        }

        // Load Fire Core weights from Row 148 (index 147): Col B (Special for Reelsets 8,9,10), Col C (Default for other reelsets)
        if (dataTable.Rows.Count > 147)
        {
            var row148 = dataTable.Rows[147];
            // Col B: Special weights for Reelsets 8, 9, 10
            if (row148.ItemArray.Length > 1 && row148[1] != DBNull.Value)
            {
                var weightsStr = row148[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(weightsStr))
                {
                    var weightsList = weightsStr.Split(',').Select(p => int.TryParse(p.Trim(), out int val) ? val : (int?)null).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                    config.FireCoreCashWeightsSpecial = weightsList;
                }
            }
            // Col C: Default weights for all other base game reelsets
            if (row148.ItemArray.Length > 2 && row148[2] != DBNull.Value)
            {
                var weightsStrC = row148[2]?.ToString();
                if (!string.IsNullOrWhiteSpace(weightsStrC))
                {
                    var weightsListC = weightsStrC.Split(',').Select(p => int.TryParse(p.Trim(), out int val) ? val : (int?)null).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                    config.FireCoreCashWeightsDefault = weightsListC;
                }
            }
            if (config.FireCoreCashWeightsDefault.Length == 0)
            {
                config.FireCoreCashWeightsDefault = config.FireCoreCashWeightsSpecial;
            }
        }

        // Load Jackpot Bonus triggering chance weight from Row 149 (index 148)
        if (dataTable.Rows.Count > 148)
        {
            var row149 = dataTable.Rows[148];
            if (row149.ItemArray.Length > 1 && row149[1] != DBNull.Value)
            {
                var weightStr = row149[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(weightStr) && int.TryParse(weightStr.Trim(), out int val))
                {
                    config.JackpotBonusTriggerChanceWeight = val;
                }
            }
        }

        // Load Jackpot Names from Row 150 (index 149)
        if (dataTable.Rows.Count > 149)
        {
            var row150 = dataTable.Rows[149];
            if (row150.ItemArray.Length > 1 && row150[1] != DBNull.Value)
            {
                var namesStr = row150[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(namesStr))
                {
                    config.JackpotNames = namesStr.Split(',').Select(s => s.Trim()).ToArray();
                }
            }
        }

        // Load Jackpot Values from Row 151 (index 150)
        if (dataTable.Rows.Count > 150)
        {
            var row151 = dataTable.Rows[150];
            if (row151.ItemArray.Length > 1 && row151[1] != DBNull.Value)
            {
                var valuesStr = row151[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(valuesStr))
                {
                    config.JackpotValues = valuesStr.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Jackpot Weights from Row 152 (index 151)
        if (dataTable.Rows.Count > 151)
        {
            var row152 = dataTable.Rows[151];
            if (row152.ItemArray.Length > 1 && row152[1] != DBNull.Value)
            {
                var weightsStr = row152[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(weightsStr))
                {
                    config.JackpotWeights = weightsStr.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Lock & Slingo Spins from Row 153 (index 152)
        if (dataTable.Rows.Count > 152)
        {
            var row = dataTable.Rows[152];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.LockSlingoSpins = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Lock & Slingo Trigger Weights from Row 154 (index 153)
        if (dataTable.Rows.Count > 153)
        {
            var row = dataTable.Rows[153];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.LockSlingoTriggerWeights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Lock & Slingo Bonus Minimums from Row 155 (index 154)
        if (dataTable.Rows.Count > 154)
        {
            var row = dataTable.Rows[154];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.LockSlingoBonusMinimums = colB.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Lock & Slingo Ladder Lines from Row 156 (index 155)
        if (dataTable.Rows.Count > 155)
        {
            var row = dataTable.Rows[155];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.LockSlingoLadderLines = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Lock & Slingo Ladder Prizes from Row 157 (index 156)
        if (dataTable.Rows.Count > 156)
        {
            var row = dataTable.Rows[156];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.LockSlingoLadderPrizes = colB.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Lock & Slingo Fire Core values from Row 158 (index 157)
        if (dataTable.Rows.Count > 157)
        {
            var row = dataTable.Rows[157];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.LockSlingoFireCoreValues = colB.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Lock & Slingo Fire Core weights from Row 159 (index 158)
        if (dataTable.Rows.Count > 158)
        {
            var row = dataTable.Rows[158];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.LockSlingoFireCoreWeights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Lock & Slingo landing chance weights from Rows 161 to 165 (indices 160-164)
        config.LockSlingoLandingChanceWeights = new List<PotLandingWeight>();
        for (int r = 160; r <= 164; r++)
        {
            if (dataTable.Rows.Count > r)
            {
                var row = dataTable.Rows[r];
                if (row.ItemArray.Length > 1 && row[0] != DBNull.Value && row[1] != DBNull.Value)
                {
                    var colA = row[0]?.ToString();
                    var colB = row[1]?.ToString();
                    if (!string.IsNullOrWhiteSpace(colA) && !string.IsNullOrWhiteSpace(colB))
                    {
                        int threshold = int.Parse(colA.Replace(">", "").Trim());
                        int[] weights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                        config.LockSlingoLandingChanceWeights.Add(new PotLandingWeight
                        {
                            Threshold = threshold,
                            Weights = weights
                        });
                    }
                }
            }
        }

        // Load Apex Spins (Bonus 2) Top Award Multipliers from Row 166 (index 165)
        if (dataTable.Rows.Count > 165)
        {
            var row = dataTable.Rows[165];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.ApexSpinsTopAwardMultipliers = colB.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Apex Spins Trigger Weights from Row 167 (index 166)
        if (dataTable.Rows.Count > 166)
        {
            var row = dataTable.Rows[166];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.ApexSpinsTriggerWeights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Apex Spins Bonus Minimums from Row 168 (index 167)
        if (dataTable.Rows.Count > 167)
        {
            var row = dataTable.Rows[167];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.ApexSpinsBonusMinimums = colB.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Apex Spins Reelset Weights from Row 169 (index 168)
        if (dataTable.Rows.Count > 168)
        {
            var row = dataTable.Rows[168];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.ApexSpinsReelsetWeights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Apex Spins Reelsets starting from Row 170 (index 169)
        int apexStartRowIndex = 169;
        for (int setIdx = 0; setIdx < 7; setIdx++)
        {
            int rStart = apexStartRowIndex + (setIdx * 5);
            if (rStart + 4 >= dataTable.Rows.Count) break;

            var headerRow = dataTable.Rows[rStart];
            string reelsetName = headerRow[0]?.ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(reelsetName))
            {
                reelsetName = $"Reelset{setIdx}";
            }

            int[][] reels = new int[5][];
            for (int r = 0; r < 5; r++)
            {
                var rRow = dataTable.Rows[rStart + r];
                var cellB = rRow[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(cellB))
                {
                    reels[r] = cellB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }

            config.ApexSpinsReelsets[reelsetName] = new ReelSet(reels);
        }

        // Load Colossal Spins (Bonus 3) Spins Counts from Row 205 (index 204)
        if (dataTable.Rows.Count > 204)
        {
            var row = dataTable.Rows[204];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.ColossalSpinsCounts = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Colossal Spins Trigger Weights from Row 206 (index 205)
        if (dataTable.Rows.Count > 205)
        {
            var row = dataTable.Rows[205];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.ColossalSpinsTriggerWeights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Colossal Spins Bonus Minimums from Row 207 (index 206)
        if (dataTable.Rows.Count > 206)
        {
            var row = dataTable.Rows[206];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.ColossalSpinsBonusMinimums = colB.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Colossal Spins Reelset Weights from Row 208 (index 207)
        if (dataTable.Rows.Count > 207)
        {
            var row = dataTable.Rows[207];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.ColossalSpinsReelsetWeights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Colossal Spins Reelsets starting from Row 209 (index 208)
        int colossalStartRowIndex = 208;
        for (int setIdx = 0; setIdx < 7; setIdx++)
        {
            int rStart = colossalStartRowIndex + (setIdx * 5);
            if (rStart + 4 >= dataTable.Rows.Count) break;

            var headerRow = dataTable.Rows[rStart];
            string reelsetName = headerRow[0]?.ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(reelsetName))
            {
                reelsetName = $"Reelset{setIdx}";
            }

            int[][] reels = new int[5][];
            for (int r = 0; r < 5; r++)
            {
                var rRow = dataTable.Rows[rStart + r];
                var cellB = rRow[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(cellB))
                {
                    reels[r] = cellB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }

            config.ColossalSpinsReelsets[reelsetName] = new ReelSet(reels);
        }

        // Load Primal Zone Bonus (Bonus 4) configuration starting from Row 244 (index 243)
        if (dataTable.Rows.Count > 243)
        {
            var row = dataTable.Rows[243];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.PrimalZoneSpins = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Primal Zone Trigger Weights from Row 245 (index 244)
        if (dataTable.Rows.Count > 244)
        {
            var row = dataTable.Rows[244];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.PrimalZoneTriggerWeights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Primal Zone Bonus Minimums from Row 246 (index 245)
        if (dataTable.Rows.Count > 245)
        {
            var row = dataTable.Rows[245];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.PrimalZoneBonusMinimums = colB.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Fire Core Cash Payouts from Row 247 (index 246)
        if (dataTable.Rows.Count > 246)
        {
            var row = dataTable.Rows[246];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.PrimalZoneFireCoreValues = colB.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Fire Core Cash Weights from Row 248 (index 247)
        if (dataTable.Rows.Count > 247)
        {
            var row = dataTable.Rows[247];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.PrimalZoneFireCoreWeights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Bananas Cash Payouts from Row 249 (index 248)
        if (dataTable.Rows.Count > 248)
        {
            var row = dataTable.Rows[248];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.PrimalZoneBananaValues = colB.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Bananas Cash Weights from Row 250 (index 249)
        if (dataTable.Rows.Count > 249)
        {
            var row = dataTable.Rows[249];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.PrimalZoneBananaWeights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Bonus Stages & Required Bananas from Rows 251 & 252 (indices 250 & 251)
        if (dataTable.Rows.Count > 251)
        {
            var row252 = dataTable.Rows[251];
            if (row252.ItemArray.Length > 1 && row252[1] != DBNull.Value)
            {
                var colB = row252[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.PrimalZoneStageBananasRequired = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Fire Core Landing Table from Rows 254 to 257 (indices 253 to 256)
        config.PrimalZoneFireCoreLandingChanceWeights = new List<PotLandingWeight>();
        for (int r = 253; r <= 256; r++)
        {
            if (dataTable.Rows.Count > r)
            {
                var row = dataTable.Rows[r];
                if (row.ItemArray.Length > 1 && row[0] != DBNull.Value && row[1] != DBNull.Value)
                {
                    var colA = row[0]?.ToString();
                    var colB = row[1]?.ToString();
                    if (!string.IsNullOrWhiteSpace(colA) && !string.IsNullOrWhiteSpace(colB))
                    {
                        int size = int.Parse(colA.Split('x')[0].Trim());
                        int[] weights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                        config.PrimalZoneFireCoreLandingChanceWeights.Add(new PotLandingWeight
                        {
                            Threshold = size,
                            Weights = weights
                        });
                    }
                }
            }
        }

        // Load Bananas Landing Table from Rows 259 to 262 (indices 258 to 261)
        config.PrimalZoneBananaLandingChanceWeights = new List<PotLandingWeight>();
        for (int r = 258; r <= 261; r++)
        {
            if (dataTable.Rows.Count > r)
            {
                var row = dataTable.Rows[r];
                if (row.ItemArray.Length > 1 && row[0] != DBNull.Value && row[1] != DBNull.Value)
                {
                    var colA = row[0]?.ToString();
                    var colB = row[1]?.ToString();
                    if (!string.IsNullOrWhiteSpace(colA) && !string.IsNullOrWhiteSpace(colB))
                    {
                        int size = int.Parse(colA.Split('x')[0].Trim());
                        int[] weights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                        config.PrimalZoneBananaLandingChanceWeights.Add(new PotLandingWeight
                        {
                            Threshold = size,
                            Weights = weights
                        });
                    }
                }
            }
        }

        // Load Stampede Spin configuration starting from Row 263 (index 262)
        if (dataTable.Rows.Count > 262)
        {
            var row = dataTable.Rows[262];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.StampedePotCounts = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Stampede Pot Count Weights from Row 264 (index 263)
        if (dataTable.Rows.Count > 263)
        {
            var row = dataTable.Rows[263];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.StampedePotCountWeights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        // Load Stampede Pot Type Weights from Row 265 (index 264)
        if (dataTable.Rows.Count > 264)
        {
            var row = dataTable.Rows[264];
            if (row.ItemArray.Length > 1 && row[1] != DBNull.Value)
            {
                var colB = row[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(colB))
                {
                    config.StampedePotTypeWeights = colB.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                }
            }
        }

        config.PrepareForSimulation();

        return config;
    }

    private static ReelSet ParseReels(DataTable table)
    {
        int reelCount = 5;
        var strips = new List<int>[reelCount];
        for (int r = 0; r < reelCount; r++)
        {
            strips[r] = new List<int>();
        }

        foreach (DataRow row in table.Rows)
        {
            for (int r = 0; r < reelCount; r++)
            {
                string colName = $"Reel{r}";
                if (table.Columns.Contains(colName) && row[colName] != DBNull.Value && !string.IsNullOrWhiteSpace(row[colName].ToString()))
                {
                    strips[r].Add(Convert.ToInt32(row[colName]));
                }
            }
        }

        int[][] reels = new int[reelCount][];
        for (int r = 0; r < reelCount; r++)
        {
            reels[r] = strips[r].ToArray();
        }

        return new ReelSet(reels);
    }

    public static void SaveResults(string outputFilePath, Dictionary<string, string> stats)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Simulation Summary");

        // Title styling
        worksheet.Cell("A1").Value = "Simulation Run Summary";
        worksheet.Cell("A1").Style.Font.Bold = true;
        worksheet.Cell("A1").Style.Font.FontSize = 16;
        worksheet.Cell("A1").Style.Font.FontColor = XLColor.DarkBlue;

        // Headers
        worksheet.Cell("A3").Value = "Metric";
        worksheet.Cell("B3").Value = "Value";
        worksheet.Range("A3:B3").Style.Font.Bold = true;
        worksheet.Range("A3:B3").Style.Fill.BackgroundColor = XLColor.LightGray;

        int rowNum = 4;
        foreach (var kvp in stats)
        {
            worksheet.Cell(rowNum, 1).Value = kvp.Key;
            worksheet.Cell(rowNum, 2).Value = kvp.Value;
            
            // Format RTP row specifically
            if (kvp.Key.Contains("RTP"))
            {
                worksheet.Cell(rowNum, 1).Style.Font.Bold = true;
                worksheet.Cell(rowNum, 2).Style.Font.Bold = true;
                worksheet.Cell(rowNum, 2).Style.Font.FontColor = XLColor.Green;
            }
            rowNum++;
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(outputFilePath);
    }

    private static void ValidateRowHeader(DataTable dataTable, int rowIndex, string expectedKeyword)
    {
        if (rowIndex < dataTable.Rows.Count)
        {
            var row = dataTable.Rows[rowIndex];
            string colA = row[0]?.ToString()?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(colA) && !colA.Contains(expectedKeyword, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[Config Warning] Row {rowIndex + 1} Col A was expected to contain '{expectedKeyword}', but found '{colA}'.");
            }
        }
    }
}
