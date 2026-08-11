using System;
using System.IO;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using ExcelDataReader;
using ClosedXML.Excel;
using SlotFramework.Models;

namespace CashVortexGame.Config;

public class CashVortexExcelLoader
{
    static CashVortexExcelLoader()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public static CashVortexConfig Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Cash Vortex Excel configuration file not found", filePath);
        }

        var config = new CashVortexConfig();

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var result = reader.AsDataSet();

        if (result.Tables.Count == 0)
        {
            throw new InvalidDataException("Excel file contains no worksheets");
        }

        var dataTable = result.Tables["Data"] ?? result.Tables[0];
        
        // Parse symbols and basic config if table exists
        return config;
    }
}
