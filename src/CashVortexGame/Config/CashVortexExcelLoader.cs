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

        // 4. Default: load directly from online Google Sheet
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

        var dataTable = result.Tables["Data"] ?? result.Tables[0];
        ParseDataTableDynamic(dataTable, config);

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

        for (int r = 0; r < dataTable.Rows.Count; r++)
        {
            var row = dataTable.Rows[r];
            string col0 = GetCellString(row, 0).Trim();
            string col1 = GetCellString(row, 1).Trim();

            if (string.IsNullOrEmpty(col0) && string.IsNullOrEmpty(col1)) continue;

            // Detect section headers
            if (col0.Equals("Table Selections", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Table Selections";
                continue;
            }
            else if (col0.Equals("Special Symbols Chance", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Special Symbols Chance";
                continue;
            }
            else if (col0.Equals("Special Symbols", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Special Symbols";
                continue;
            }
            else if (col0.Equals("Jackpot Coins", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Jackpot Coins";
                continue;
            }
            else if (col0.Equals("Cash Strikes", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Cash Strikes";
                continue;
            }
            else if (col0.Equals("Cash Coins Chance", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Cash Coins Chance";
                continue;
            }
            else if (col0.Equals("Cash Coins", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Cash Coins";
                continue;
            }

            // Skip table header or summary rows
            if (col0.Equals("TableID", StringComparison.OrdinalIgnoreCase) ||
                col0.Equals("SymbolID", StringComparison.OrdinalIgnoreCase) ||
                col0.Equals("JackpotID", StringComparison.OrdinalIgnoreCase) ||
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
                    if (TryParseDouble(row, 0, out double strikeMult) && TryParseInt(row, 1, out int strikeWeight))
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
                    if (TryParseDouble(row, 0, out double coinMult) && TryParseInt(row, 1, out int cWeight))
                    {
                        config.CashCoinValues.Add(new CashValueDef
                        {
                            Multiplier = coinMult,
                            Weight = cWeight
                        });
                    }
                    break;
            }
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
