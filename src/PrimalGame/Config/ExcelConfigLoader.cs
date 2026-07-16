using System;
using System.IO;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using ExcelDataReader;
using ClosedXML.Excel;
using SlotFramework.Models;

namespace PrimalGame.Config;

public class ExcelConfigLoader
{
    static ExcelConfigLoader()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public static PrimalConfig Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Excel configuration file not found", filePath);
        }

        var config = new PrimalConfig();

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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

        // Load Fire Core values from Row 147 (index 146)
        if (dataTable.Rows.Count > 146)
        {
            var row147 = dataTable.Rows[146];
            if (row147.ItemArray.Length > 1 && row147[1] != DBNull.Value)
            {
                var valuesStr = row147[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(valuesStr))
                {
                    string[] parts = valuesStr.Split(',');
                    var valuesList = new List<double>();
                    foreach (var part in parts)
                    {
                        if (double.TryParse(part.Trim(), out double val))
                        {
                            valuesList.Add(val);
                        }
                    }
                    config.FireCoreCashValues = valuesList.ToArray();
                }
            }
        }

        // Load Fire Core weights from Row 148 (index 147)
        if (dataTable.Rows.Count > 147)
        {
            var row148 = dataTable.Rows[147];
            if (row148.ItemArray.Length > 1 && row148[1] != DBNull.Value)
            {
                var weightsStr = row148[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(weightsStr))
                {
                    string[] parts = weightsStr.Split(',');
                    var weightsList = new List<int>();
                    foreach (var part in parts)
                    {
                        if (int.TryParse(part.Trim(), out int val))
                        {
                            weightsList.Add(val);
                        }
                    }
                    config.FireCoreCashWeights = weightsList.ToArray();
                }
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
}
