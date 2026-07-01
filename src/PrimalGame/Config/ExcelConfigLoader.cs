using System;
using System.IO;
using System.Data;
using System.Collections.Generic;
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
                    // normal symbols have payouts for 3, 4, 5 of a kind (index 0 maps to 3, 1 to 4, 2 to 5)
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (double.TryParse(parts[i].Trim(), out double multiplier))
                        {
                            int matchCount = 3 + i;
                            long payoutInCents = (long)Math.Round(multiplier * 100);
                            config.Paytable.AddPayout(symbolId, matchCount, payoutInCents);
                        }
                    }
                }
            }
        }

        // Load Base Game Stage Weights from Row 18 (index 17) to Row 24 (index 23)
        for (int r = 17; r < Math.Min(24, dataTable.Rows.Count); r++)
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
