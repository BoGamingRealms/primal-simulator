using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SlotFramework.Utilities;

/// <summary>
/// Utility to download and stream Google Sheets and Google Drive spreadsheets directly as Excel (.xlsx) workbooks.
/// </summary>
public static class GoogleSheetDownloader
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    });

    private static readonly Regex SheetIdRegex = new(
        @"/spreadsheets/d/([a-zA-Z0-9-_]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DriveFileIdRegex = new(
        @"/file/d/([a-zA-Z0-9-_]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RawIdRegex = new(
        @"^[a-zA-Z0-9-_]{25,}$",
        RegexOptions.Compiled);

    /// <summary>
    /// Checks if a given path or string represents an online URL or Google Sheet reference.
    /// </summary>
    public static bool IsOnlineSource(string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) return false;

        string trimmed = pathOrUrl.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("docs.google.com", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("drive.google.com", StringComparison.OrdinalIgnoreCase) ||
               (RawIdRegex.IsMatch(trimmed) && !File.Exists(trimmed));
    }

    /// <summary>
    /// Extracts the Google Spreadsheet ID from a URL, share link, or raw ID.
    /// </summary>
    public static string? ExtractSpreadsheetId(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) return null;

        string trimmed = pathOrUrl.Trim();

        var match = SheetIdRegex.Match(trimmed);
        if (match.Success) return match.Groups[1].Value;

        var driveMatch = DriveFileIdRegex.Match(trimmed);
        if (driveMatch.Success) return driveMatch.Groups[1].Value;

        if (trimmed.Contains("id="))
        {
            var uri = new Uri(trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? trimmed : "https://" + trimmed);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            string? id = query["id"];
            if (!string.IsNullOrEmpty(id)) return id;
        }

        if (RawIdRegex.IsMatch(trimmed))
        {
            return trimmed;
        }

        return null;
    }

    /// <summary>
    /// Builds a direct XLSX export URL for a given Google Sheet URL or ID.
    /// </summary>
    public static string GetExportUrl(string sheetUrlOrId)
    {
        string? sheetId = ExtractSpreadsheetId(sheetUrlOrId);
        if (!string.IsNullOrEmpty(sheetId))
        {
            return $"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=xlsx";
        }

        if (sheetUrlOrId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            sheetUrlOrId.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return sheetUrlOrId;
        }

        throw new ArgumentException($"Cannot resolve Google Spreadsheet ID from: {sheetUrlOrId}");
    }

    /// <summary>
    /// Downloads the Google Spreadsheet as an Excel (.xlsx) stream asynchronously.
    /// </summary>
    public static async Task<MemoryStream> DownloadStreamAsync(string sheetUrlOrId)
    {
        string exportUrl = GetExportUrl(sheetUrlOrId);
        
        using var response = await HttpClient.GetAsync(exportUrl, HttpCompletionOption.ResponseContentRead);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Failed to download Google Sheet. HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). " +
                $"Please ensure the Google Sheet is shared with 'Anyone with the link can view'. URL: {exportUrl}");
        }

        var memoryStream = new MemoryStream();
        await response.Content.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }

    /// <summary>
    /// Downloads the Google Spreadsheet as an Excel (.xlsx) stream synchronously.
    /// </summary>
    public static MemoryStream DownloadStream(string sheetUrlOrId)
    {
        return DownloadStreamAsync(sheetUrlOrId).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Opens a stream from either a local file or an online Google Sheet.
    /// </summary>
    public static Stream OpenConfigStream(string pathOrUrl)
    {
        if (IsOnlineSource(pathOrUrl))
        {
            return DownloadStream(pathOrUrl);
        }

        if (File.Exists(pathOrUrl))
        {
            return File.Open(pathOrUrl, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        throw new FileNotFoundException($"Configuration file not found locally and not a valid online URL: {pathOrUrl}", pathOrUrl);
    }
}
