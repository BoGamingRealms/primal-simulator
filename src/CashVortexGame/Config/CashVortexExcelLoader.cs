using System;
using System.IO;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using ExcelDataReader;
using CashVortexGame.Config;
using SlotFramework.Utilities;

namespace CashVortexGame.Config;

public class CashVortexExcelLoader
{
    public const string DefaultGoogleSheetUrl = "https://docs.google.com/spreadsheets/d/1pYeAirnQRzlnHgQZGVG2eOVe1yHdtsflfJ9NQjVESbE/edit?usp=sharing";

    static CashVortexExcelLoader()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public static CashVortexConfig Load(string? filePathOrUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(filePathOrUrl) && GoogleSheetDownloader.IsOnlineSource(filePathOrUrl))
        {
            using var onlineStream = GoogleSheetDownloader.DownloadStream(filePathOrUrl);
            return Load(onlineStream);
        }

        if (!string.IsNullOrEmpty(filePathOrUrl) && File.Exists(filePathOrUrl))
        {
            using var fileStream = File.Open(filePathOrUrl, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Load(fileStream);
        }

        string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CashVortexTriplePower95.xlsx");
        if (File.Exists(downloadsPath))
        {
            using var fileStream = File.Open(downloadsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Load(fileStream);
        }

        string localPath = "CashVortexTriplePower95.xlsx";
        if (File.Exists(localPath))
        {
            using var fileStream = File.Open(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Load(fileStream);
        }

        using var defaultStream = GoogleSheetDownloader.DownloadStream(DefaultGoogleSheetUrl);
        return Load(defaultStream);
    }

    public static CashVortexConfig Load(Stream stream)
    {
        var config = new CashVortexConfig();

        using var reader = ExcelReaderFactory.CreateReader(stream);
        var result = reader.AsDataSet();

        if (result.Tables.Count == 0)
        {
            throw new InvalidDataException("Excel file contains no worksheets");
        }

        DataTable? dataTable = null;
        foreach (DataTable table in result.Tables)
        {
            if (table.TableName.Trim().Equals("Data", StringComparison.OrdinalIgnoreCase))
            {
                dataTable = table;
                break;
            }
        }

        if (dataTable == null)
        {
            foreach (DataTable table in result.Tables)
            {
                if (table.TableName.Trim().Equals("BaseGame", StringComparison.OrdinalIgnoreCase))
                {
                    dataTable = table;
                    break;
                }
            }
        }

        dataTable ??= result.Tables[result.Tables.Count - 1];
        ParseDataTableDynamic(dataTable, config);

        EnsureDefaultConfigTables(config);
        config.BuildWeightTables();
        return config;
    }

    private static void ParseDataTableDynamic(DataTable dataTable, CashVortexConfig config)
    {
        string currentSection = string.Empty;
        int tableSelCount = 0;
        int specChanceCount = 0;
        int specSymCount = 0;
        int jackpotCount = 0;
        int coinChanceCount = 0;

        int miniWheelCount = 0;
        int megaWheelCount = 0;
        int ultraWheelCount = 0;

        bool inBonusSection = false;
        int bonusOutcomeCount = 0;

        for (int r = 0; r < dataTable.Rows.Count; r++)
        {
            var row = dataTable.Rows[r];
            string col0 = GetCellString(row, 0).Trim();
            string col1 = GetCellString(row, 1).Trim();

            if (string.IsNullOrEmpty(col0) && string.IsNullOrEmpty(col1)) continue;

            string checkStr = string.IsNullOrEmpty(col0) ? col1 : col0;

            // Detect section headers
            if (checkStr.StartsWith("Table Selections", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Table Selections";
                continue;
            }
            else if (checkStr.StartsWith("Special Symbols Chance", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Special Symbols Chance";
                continue;
            }
            else if (checkStr.StartsWith("Special Symbol", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Special Symbols";
                continue;
            }
            else if (checkStr.StartsWith("Slingo Ladder", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Slingo Ladder";
                inBonusSection = true;
                continue;
            }
            else if (checkStr.StartsWith("Symbol Landing Chance", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Symbol Landing Chance";
                inBonusSection = true;
                continue;
            }
            else if (checkStr.StartsWith("Symbols Landing", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Symbols Landing Selections";
                inBonusSection = true;
                continue;
            }
            else if (checkStr.StartsWith("Jackpot Coins", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = inBonusSection ? "Bonus Jackpot Coins" : "Jackpot Coins";
                continue;
            }
            else if (checkStr.StartsWith("Cash Strikes", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = inBonusSection ? "Bonus Cash Strikes" : "Cash Strikes";
                continue;
            }
            else if (checkStr.StartsWith("Cash Coins Chance", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Cash Coins Chance";
                continue;
            }
            else if (checkStr.StartsWith("Cash Coins", StringComparison.OrdinalIgnoreCase) ||
                     checkStr.StartsWith("For each landing Cash Coin", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = inBonusSection ? "Bonus Cash Coins" : "Cash Coins";
                continue;
            }
            else if (checkStr.StartsWith("Mini Wheel", StringComparison.OrdinalIgnoreCase) || checkStr.StartsWith("Wheel 1", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = inBonusSection ? "Bonus Mini Wheel" : "Mini Wheel";
                continue;
            }
            else if (checkStr.StartsWith("Mega Wheel", StringComparison.OrdinalIgnoreCase) || checkStr.StartsWith("Wheel 2", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = inBonusSection ? "Bonus Mega Wheel" : "Mega Wheel";
                continue;
            }
            else if (checkStr.StartsWith("Ultra Wheel", StringComparison.OrdinalIgnoreCase) || checkStr.StartsWith("Wheel 3", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = inBonusSection ? "Bonus Ultra Wheel" : "Ultra Wheel";
                continue;
            }

            // Skip table header or summary rows
            if (col0.Equals("TableID", StringComparison.OrdinalIgnoreCase) ||
                col0.Equals("SymbolID", StringComparison.OrdinalIgnoreCase) ||
                col0.Equals("JackpotID", StringComparison.OrdinalIgnoreCase) ||
                col0.Equals("PrizeID", StringComparison.OrdinalIgnoreCase) ||
                col0.Equals("Spaces Left", StringComparison.OrdinalIgnoreCase) ||
                col0.StartsWith("Pays", StringComparison.OrdinalIgnoreCase) ||
                col0.Equals("Total", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (currentSection)
            {
                case "Table Selections":
                    if (TryParseInt(row, 1, out int tWeight))
                    {
                        config.TableSelections.Add(new TableSelection
                        {
                            TableId = tableSelCount++,
                            Description = col0,
                            Weight = tWeight
                        });
                    }
                    break;

                case "Special Symbols Chance":
                    if (TryParseInt(row, 1, out int specWeight) && TryParseInt(row, 2, out int noSpecWeight))
                    {
                        config.SpecialSymbolChances.Add(new SpecialSymbolChance
                        {
                            TableId = specChanceCount++,
                            Description = col0,
                            SpecialSymbolWeight = specWeight,
                            NoSpecialSymbolWeight = noSpecWeight
                        });
                    }
                    break;

                case "Special Symbols":
                    if (TryParseInt(row, 1, out int symWeight))
                    {
                        config.SpecialSymbolDefs.Add(new SpecialSymbolDef
                        {
                            SymbolId = specSymCount++,
                            SymbolName = col0,
                            Weight = symWeight
                        });
                    }
                    break;

                case "Jackpot Coins":
                    if (TryParseDouble(row, 1, out double jpMult) && TryParseInt(row, 2, out int jpWeight))
                    {
                        config.JackpotCoins.Add(new JackpotCoinDef
                        {
                            JackpotId = jackpotCount++,
                            JackpotName = col0,
                            Multiplier = jpMult,
                            Weight = jpWeight
                        });
                    }
                    break;

                case "Cash Strikes":
                    if ((TryParseDouble(row, 0, out double strikeMult) && TryParseInt(row, 1, out int strikeWeight)) ||
                        (TryParseDouble(row, 1, out strikeMult) && TryParseInt(row, 2, out strikeWeight)))
                    {
                        config.CashStrikeValues.Add(new CashValueDef
                        {
                            Multiplier = strikeMult,
                            Weight = strikeWeight
                        });
                    }
                    break;

                case "Cash Coins Chance":
                    if (TryParseInt(row, 1, out int coinWeight) && TryParseInt(row, 2, out int blankWeight))
                    {
                        config.CashCoinChances.Add(new CashCoinChance
                        {
                            TableId = coinChanceCount++,
                            Description = col0,
                            CoinWeight = coinWeight,
                            BlankWeight = blankWeight
                        });
                    }
                    break;

                case "Cash Coins":
                    if ((TryParseDouble(row, 0, out double coinMult) && TryParseInt(row, 1, out int cWeight)) ||
                        (TryParseDouble(row, 1, out coinMult) && TryParseInt(row, 2, out cWeight)))
                    {
                        config.CashCoinValues.Add(new CashValueDef
                        {
                            Multiplier = coinMult,
                            Weight = cWeight
                        });
                    }
                    break;

                case "Mini Wheel":
                    if (TryParseWheelPrize(row, miniWheelCount++, out var miniPrize))
                    {
                        config.MiniWheelPrizes.Add(miniPrize);
                    }
                    break;

                case "Mega Wheel":
                    if (TryParseWheelPrize(row, megaWheelCount++, out var megaPrize))
                    {
                        config.MegaWheelPrizes.Add(megaPrize);
                    }
                    break;

                case "Ultra Wheel":
                    if (TryParseWheelPrize(row, ultraWheelCount++, out var ultraPrize))
                    {
                        config.UltraWheelPrizes.Add(ultraPrize);
                    }
                    break;

                // --- Lock & Slingo Bonus Parsers ---
                case "Slingo Ladder":
                    if (TryParseInt(row, 0, out int slingoCount))
                    {
                        string prizeStr = GetCellString(row, 1).Trim();
                        var ladderPrize = ParseSlingoLadderPrize(slingoCount, prizeStr);
                        config.SlingoLadderPrizes.Add(ladderPrize);
                    }
                    break;

                case "Symbol Landing Chance":
                    if (col0.StartsWith("Base Factor", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseInt(row, 1, out int bf) && bf > 0)
                        {
                            config.BonusBaseFactor = bf;
                        }
                    }
                    else if (col0.Contains("Lives", StringComparison.OrdinalIgnoreCase) || col0.Contains("Life", StringComparison.OrdinalIgnoreCase))
                    {
                        int lives = col0.Contains("3") ? 3 : (col0.Contains("2") ? 2 : 1);
                        for (int b = 0; b < 5; b++)
                        {
                            if (TryParseInt(row, b + 1, out int w))
                            {
                                config.BonusLandingWeightsByLifeAndBucket[lives, b] = w;
                            }
                        }
                    }
                    break;

                case "Symbols Landing Selections":
                    if (!string.IsNullOrEmpty(col0) && !col0.StartsWith("Spaces", StringComparison.OrdinalIgnoreCase))
                    {
                        var outcomeDef = new BonusOutcomeDef
                        {
                            OutcomeId = bonusOutcomeCount++,
                            Description = col0,
                            Items = ParseBonusOutcomeItems(col0)
                        };
                        for (int b = 0; b < 5; b++)
                        {
                            if (TryParseInt(row, b + 1, out int w))
                            {
                                outcomeDef.WeightsBySpaceBucket[b] = w;
                            }
                        }
                        config.BonusOutcomeDefs.Add(outcomeDef);
                    }
                    break;

                case "Bonus Jackpot Coins":
                    if (TryParseDouble(row, 1, out double bJpMult) && TryParseInt(row, 2, out int bJpWeight))
                    {
                        config.BonusJackpotCoins.Add(new JackpotCoinDef
                        {
                            JackpotName = col0,
                            Multiplier = bJpMult,
                            Weight = bJpWeight
                        });
                    }
                    break;

                case "Bonus Cash Strikes":
                    if (col0.Contains("Strike", StringComparison.OrdinalIgnoreCase) && TryParseInt(row, 1, out int strTypeW))
                    {
                        config.BonusCashStrikeTypes.Add(new SpecialSymbolDef
                        {
                            SymbolName = col0,
                            Weight = strTypeW
                        });
                    }
                    else if (TryParseDouble(row, 0, out double bStrikeVal) && TryParseInt(row, 1, out int bStrikeValW))
                    {
                        config.BonusCashStrikeValues.Add(new CashValueDef
                        {
                            Multiplier = bStrikeVal,
                            Weight = bStrikeValW
                        });
                    }
                    break;

                case "Bonus Cash Coins":
                    if (TryParseDouble(row, 0, out double bCoinVal) && TryParseInt(row, 1, out int bCoinValW))
                    {
                        config.BonusCashCoinValues.Add(new CashValueDef
                        {
                            Multiplier = bCoinVal,
                            Weight = bCoinValW
                        });
                    }
                    break;

                case "Bonus Mini Wheel":
                    if (TryParseWheelPrize(row, config.BonusMiniWheelPrizes.Count, out var bMiniPrize))
                    {
                        config.BonusMiniWheelPrizes.Add(bMiniPrize);
                    }
                    break;

                case "Bonus Mega Wheel":
                    if (TryParseWheelPrize(row, config.BonusMegaWheelPrizes.Count, out var bMegaPrize))
                    {
                        config.BonusMegaWheelPrizes.Add(bMegaPrize);
                    }
                    break;

                case "Bonus Ultra Wheel":
                    if (TryParseWheelPrize(row, config.BonusUltraWheelPrizes.Count, out var bUltraPrize))
                    {
                        config.BonusUltraWheelPrizes.Add(bUltraPrize);
                    }
                    break;
            }
        }
    }

    private static SlingoLadderPrizeDef ParseSlingoLadderPrize(int slingoCount, string prizeStr)
    {
        var ladderPrize = new SlingoLadderPrizeDef
        {
            SlingoCount = slingoCount,
            PrizeString = prizeStr
        };

        if (string.IsNullOrEmpty(prizeStr) || prizeStr == "0")
        {
            ladderPrize.Type = WheelPrizeType.Multiplier;
            ladderPrize.ParameterValue = 0;
            return ladderPrize;
        }

        if (prizeStr.StartsWith("x", StringComparison.OrdinalIgnoreCase))
        {
            ladderPrize.Type = WheelPrizeType.Multiplier;
            if (double.TryParse(prizeStr.Substring(1), out double m)) ladderPrize.ParameterValue = m;
        }
        else if (prizeStr.Contains("Jackpot", StringComparison.OrdinalIgnoreCase))
        {
            ladderPrize.Type = WheelPrizeType.Jackpot;
            if (prizeStr.Contains("Mini", StringComparison.OrdinalIgnoreCase)) ladderPrize.JackpotType = "Mini";
            else if (prizeStr.Contains("Mega", StringComparison.OrdinalIgnoreCase)) ladderPrize.JackpotType = "Mega";
            else if (prizeStr.Contains("Ultra", StringComparison.OrdinalIgnoreCase)) ladderPrize.JackpotType = "Ultra";
        }
        else if (double.TryParse(prizeStr, out double strikeVal))
        {
            ladderPrize.Type = WheelPrizeType.UltraStrike;
            ladderPrize.ParameterValue = strikeVal;
        }

        return ladderPrize;
    }

    private static List<BonusOutcomeItem> ParseBonusOutcomeItems(string desc)
    {
        var list = new List<BonusOutcomeItem>();
        var parts = desc.Split('+', StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            string p = part.Trim();
            int count = 1;
            if (char.IsDigit(p[0]))
            {
                int spaceIdx = p.IndexOf(' ');
                if (spaceIdx > 0 && int.TryParse(p.Substring(0, spaceIdx), out int c))
                {
                    count = c;
                    p = p.Substring(spaceIdx + 1).Trim();
                }
            }

            if (p.Contains("Jackpot", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new BonusOutcomeItem { Type = SymbolType.JackpotCoin, Count = count });
            }
            else if (p.Contains("Vortex", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new BonusOutcomeItem { Type = SymbolType.MiniVortex, Count = count });
            }
            else if (p.Contains("Strike", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new BonusOutcomeItem { Type = SymbolType.MiniStrike, Count = count });
            }
            else if (p.Contains("X", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new BonusOutcomeItem { Type = SymbolType.XWheel, Count = count });
            }
            else
            {
                list.Add(new BonusOutcomeItem { Type = SymbolType.CashCoin, Count = count });
            }
        }

        return list;
    }

    private static bool TryParseWheelPrize(DataRow row, int defaultId, out WheelPrizeDef prize)
    {
        prize = new WheelPrizeDef();
        string col0 = GetCellString(row, 0).Trim();
        string col1 = GetCellString(row, 1).Trim();
        string col2 = GetCellString(row, 2).Trim();

        string prizeStr = string.IsNullOrEmpty(col1) ? col0 : col1;
        string weightStr = string.IsNullOrEmpty(col2) ? col1 : col2;

        if (TryParseInt(row, 2, out int w) || TryParseInt(row, 1, out w))
        {
            weightStr = w.ToString();
        }

        if (string.IsNullOrEmpty(prizeStr)) return false;

        int weight = int.TryParse(weightStr, out int parsedWeight) ? parsedWeight : 100;
        prize = ParsePrizeDef(defaultId, prizeStr, weight);
        return true;
    }

    public static WheelPrizeDef ParsePrizeDef(int id, string prizeStr, int weight)
    {
        var prize = new WheelPrizeDef
        {
            PrizeId = id,
            PrizeString = prizeStr,
            Weight = weight
        };

        string s = prizeStr.Trim();
        if (s.StartsWith("x", StringComparison.OrdinalIgnoreCase))
        {
            prize.Type = WheelPrizeType.Multiplier;
            if (double.TryParse(s.Substring(1), out double m)) prize.ParameterValue = m;
        }
        else if (s.Equals("Upgrade", StringComparison.OrdinalIgnoreCase))
        {
            prize.Type = WheelPrizeType.Upgrade;
        }
        else if (s.Contains("Lock", StringComparison.OrdinalIgnoreCase) || s.Contains("Slingo", StringComparison.OrdinalIgnoreCase))
        {
            prize.Type = WheelPrizeType.LockAndSlingo;
        }
        else if (s.Contains("Jackpot", StringComparison.OrdinalIgnoreCase))
        {
            prize.Type = WheelPrizeType.Jackpot;
            if (s.Contains("Mini", StringComparison.OrdinalIgnoreCase)) prize.JackpotType = "Mini";
            else if (s.Contains("Mega", StringComparison.OrdinalIgnoreCase)) prize.JackpotType = "Mega";
            else if (s.Contains("Ultra", StringComparison.OrdinalIgnoreCase)) prize.JackpotType = "Ultra";
            else prize.JackpotType = "Mini";
        }
        else if (double.TryParse(s, out double strikeVal))
        {
            prize.Type = WheelPrizeType.UltraStrike;
            prize.ParameterValue = strikeVal;
        }
        else
        {
            prize.Type = WheelPrizeType.Multiplier;
            prize.ParameterValue = 2.0;
        }

        return prize;
    }

    private static void EnsureDefaultConfigTables(CashVortexConfig config)
    {
        if (config.TableSelections.Count == 0)
        {
            config.TableSelections.Add(new TableSelection { TableId = 0, Description = "Low Symbol Chance", Weight = 1000 });
            config.TableSelections.Add(new TableSelection { TableId = 1, Description = "Medium Symbol Chance", Weight = 300 });
            config.TableSelections.Add(new TableSelection { TableId = 2, Description = "High Symbol Chance", Weight = 100 });
        }

        if (config.SpecialSymbolChances.Count == 0)
        {
            config.SpecialSymbolChances.Add(new SpecialSymbolChance { TableId = 0, Description = "Low Symbol Chance", SpecialSymbolWeight = 200, NoSpecialSymbolWeight = 1000 });
            config.SpecialSymbolChances.Add(new SpecialSymbolChance { TableId = 1, Description = "Medium Symbol Chance", SpecialSymbolWeight = 200, NoSpecialSymbolWeight = 1000 });
            config.SpecialSymbolChances.Add(new SpecialSymbolChance { TableId = 2, Description = "High Symbol Chance", SpecialSymbolWeight = 200, NoSpecialSymbolWeight = 1000 });
        }

        if (config.SpecialSymbolDefs.Count == 0)
        {
            config.SpecialSymbolDefs.Add(new SpecialSymbolDef { SymbolId = 0, SymbolName = "Jackpot Coin", Weight = 1000 });
            config.SpecialSymbolDefs.Add(new SpecialSymbolDef { SymbolId = 1, SymbolName = "Mini Vortex", Weight = 1000 });
            config.SpecialSymbolDefs.Add(new SpecialSymbolDef { SymbolId = 2, SymbolName = "Mega Vortex", Weight = 300 });
            config.SpecialSymbolDefs.Add(new SpecialSymbolDef { SymbolId = 3, SymbolName = "Ultra Vortex", Weight = 100 });
            config.SpecialSymbolDefs.Add(new SpecialSymbolDef { SymbolId = 4, SymbolName = "Mini Strike", Weight = 1000 });
            config.SpecialSymbolDefs.Add(new SpecialSymbolDef { SymbolId = 5, SymbolName = "Mega Strike", Weight = 300 });
            config.SpecialSymbolDefs.Add(new SpecialSymbolDef { SymbolId = 6, SymbolName = "Ultra Strike", Weight = 100 });
            config.SpecialSymbolDefs.Add(new SpecialSymbolDef { SymbolId = 7, SymbolName = "X Wheel", Weight = 1000 });
        }

        if (config.JackpotCoins.Count == 0)
        {
            config.JackpotCoins.Add(new JackpotCoinDef { JackpotId = 0, JackpotName = "Mini", Multiplier = 5.0, Weight = 1000 });
            config.JackpotCoins.Add(new JackpotCoinDef { JackpotId = 1, JackpotName = "Mega", Multiplier = 50.0, Weight = 50 });
            config.JackpotCoins.Add(new JackpotCoinDef { JackpotId = 2, JackpotName = "Ultra", Multiplier = 500.0, Weight = 1 });
        }

        if (config.CashStrikeValues.Count == 0)
        {
            double[] strikeVals = { 0.2, 0.4, 0.6, 0.8, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0 };
            int[] strikeW = { 1000, 1000, 1000, 1000, 700, 600, 200, 100, 60, 50, 30, 20, 10 };
            for (int i = 0; i < strikeVals.Length; i++)
            {
                config.CashStrikeValues.Add(new CashValueDef { Multiplier = strikeVals[i], Weight = strikeW[i] });
            }
        }

        if (config.CashCoinChances.Count == 0)
        {
            config.CashCoinChances.Add(new CashCoinChance { TableId = 0, Description = "Low Symbol Chance", CoinWeight = 100, BlankWeight = 1000 });
            config.CashCoinChances.Add(new CashCoinChance { TableId = 1, Description = "Medium Symbol Chance", CoinWeight = 300, BlankWeight = 1000 });
            config.CashCoinChances.Add(new CashCoinChance { TableId = 2, Description = "High Symbol Chance", CoinWeight = 500, BlankWeight = 1000 });
        }

        if (config.CashCoinValues.Count == 0)
        {
            double[] coinVals = { 0.2, 0.4, 0.6, 0.8, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0 };
            int[] coinW = { 1000, 1000, 1000, 1000, 700, 600, 200, 100, 60, 50, 30, 20, 10 };
            for (int i = 0; i < coinVals.Length; i++)
            {
                config.CashCoinValues.Add(new CashValueDef { Multiplier = coinVals[i], Weight = coinW[i] });
            }
        }

        if (config.MiniWheelPrizes.Count == 0)
        {
            config.MiniWheelPrizes.Add(ParsePrizeDef(0, "x2", 1000));
            config.MiniWheelPrizes.Add(ParsePrizeDef(1, "2", 1000));
            config.MiniWheelPrizes.Add(ParsePrizeDef(2, "Mini Jackpot", 500));
            config.MiniWheelPrizes.Add(ParsePrizeDef(3, "Upgrade", 300));
        }

        if (config.MegaWheelPrizes.Count == 0)
        {
            config.MegaWheelPrizes.Add(ParsePrizeDef(0, "x3", 1000));
            config.MegaWheelPrizes.Add(ParsePrizeDef(1, "3", 1000));
            config.MegaWheelPrizes.Add(ParsePrizeDef(2, "Mega Jackpot", 300));
            config.MegaWheelPrizes.Add(ParsePrizeDef(3, "Upgrade", 200));
        }

        if (config.UltraWheelPrizes.Count == 0)
        {
            config.UltraWheelPrizes.Add(ParsePrizeDef(0, "x5", 1000));
            config.UltraWheelPrizes.Add(ParsePrizeDef(1, "5", 1000));
            config.UltraWheelPrizes.Add(ParsePrizeDef(2, "Ultra Jackpot", 100));
            config.UltraWheelPrizes.Add(ParsePrizeDef(3, "Lock & Slingo", 500));
        }

        // Lock & Slingo Bonus Defaults
        if (config.SlingoLadderPrizes.Count == 0)
        {
            config.SlingoLadderPrizes.Add(ParseSlingoLadderPrize(1, "0"));
            config.SlingoLadderPrizes.Add(ParseSlingoLadderPrize(2, "0"));
            config.SlingoLadderPrizes.Add(ParseSlingoLadderPrize(3, "0"));
            config.SlingoLadderPrizes.Add(ParseSlingoLadderPrize(4, "Mini Jackpot"));
            config.SlingoLadderPrizes.Add(ParseSlingoLadderPrize(5, "1"));
            config.SlingoLadderPrizes.Add(ParseSlingoLadderPrize(6, "2"));
            config.SlingoLadderPrizes.Add(ParseSlingoLadderPrize(7, "3"));
            config.SlingoLadderPrizes.Add(ParseSlingoLadderPrize(8, "Mega Jackpot"));
            config.SlingoLadderPrizes.Add(ParseSlingoLadderPrize(9, "5"));
            config.SlingoLadderPrizes.Add(ParseSlingoLadderPrize(10, "x2"));
            config.SlingoLadderPrizes.Add(ParseSlingoLadderPrize(12, "Ultra Jackpot"));
        }

        if (config.BonusCashStrikeTypes.Count == 0)
        {
            config.BonusCashStrikeTypes.Add(new SpecialSymbolDef { SymbolName = "Mini Strike", Weight = 1000 });
            config.BonusCashStrikeTypes.Add(new SpecialSymbolDef { SymbolName = "Mega Strike", Weight = 300 });
            config.BonusCashStrikeTypes.Add(new SpecialSymbolDef { SymbolName = "Ultra Strike", Weight = 50 });
        }

        if (config.BonusOutcomeDefs.Count == 0)
        {
            config.BonusOutcomeDefs.Add(new BonusOutcomeDef
            {
                OutcomeId = 0,
                Description = "1 Cash Coin",
                WeightsBySpaceBucket = new[] { 1000, 1000, 1000, 1000, 1000 },
                Items = new List<BonusOutcomeItem> { new() { Type = SymbolType.CashCoin, Count = 1 } }
            });
            config.BonusOutcomeDefs.Add(new BonusOutcomeDef
            {
                OutcomeId = 1,
                Description = "2 Cash Coins",
                WeightsBySpaceBucket = new[] { 400, 300, 200, 100, 0 },
                Items = new List<BonusOutcomeItem> { new() { Type = SymbolType.CashCoin, Count = 2 } }
            });
        }
    }

    private static string GetCellString(DataRow row, int colIndex)
    {
        if (colIndex < 0 || colIndex >= row.ItemArray.Length || row[colIndex] == DBNull.Value) return string.Empty;
        return row[colIndex]?.ToString() ?? string.Empty;
    }

    private static bool TryParseDouble(DataRow row, int colIndex, out double val)
    {
        val = 0;
        string s = GetCellString(row, colIndex).Trim();
        return double.TryParse(s, out val);
    }

    private static bool TryParseInt(DataRow row, int colIndex, out int val)
    {
        val = 0;
        string s = GetCellString(row, colIndex).Trim();
        if (double.TryParse(s, out double dVal))
        {
            val = (int)Math.Round(dVal);
            return true;
        }
        return false;
    }
}
