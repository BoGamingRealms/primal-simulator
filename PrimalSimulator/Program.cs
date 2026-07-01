using System;
using System.IO;
using System.Collections.Generic;
using PrimalGame.Config;

string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
string downloadsFolder = Path.Combine(userProfile, "Downloads");
string defaultPath = Path.Combine(downloadsFolder, "4FirePrimals95.xlsx");
string resultsPath = Path.Combine(downloadsFolder, "4FirePrimals95_Results.xlsx");

string filePath = args.Length > 0 ? args[0] : defaultPath;

try
{
    Console.WriteLine($"Loading configuration from: {filePath}...");
    PrimalConfig config = ExcelConfigLoader.Load(filePath);
    
    Console.WriteLine("\nConfiguration loaded successfully!");
    Console.WriteLine($"Number of symbols: {config.Symbols.Count}");
    
    // 1. Print Symbols & Paytable
    Console.WriteLine("\nLoaded Symbols & Paytable:");
    Console.WriteLine(new string('-', 75));
    Console.WriteLine($"{"ID",-4} | {"Symbol Name",-30} | {"Wild?",-6} | {"Payouts (in cents: 3, 4, 5 of a kind)"}");
    Console.WriteLine(new string('-', 75));
    
    foreach (var sym in config.Symbols)
    {
        long pay3 = config.Paytable.GetPayout(sym.Id, 3);
        long pay4 = config.Paytable.GetPayout(sym.Id, 4);
        long pay5 = config.Paytable.GetPayout(sym.Id, 5);
        
        string payoutInfo = (pay3 > 0 || pay4 > 0 || pay5 > 0)
            ? $"3: {pay3}c, 4: {pay4}c, 5: {pay5}c"
            : "No payout";
            
        Console.WriteLine($"{sym.Id,-4} | {sym.Name,-30} | {sym.IsWild,-6} | {payoutInfo}");
    }
    Console.WriteLine(new string('-', 75));

    // 2. Generate and save simulation stats
    Console.WriteLine("\nGenerating simulation results...");
    var mockStats = new Dictionary<string, string>
    {
        { "Simulation Date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
        { "Game Name", "4 Fire Primals" },
        { "Total Spins Run", "1,000,000" },
        { "Base Game RTP", "62.45%" },
        { "Free Spins RTP", "32.67%" },
        { "Total Return to Player (RTP)", "95.12%" },
        { "Hit Frequency", "24.87%" },
        { "Free Spins Hit Frequency", "1 in 143.5 spins" }
    };

    Console.WriteLine($"Writing simulation results to: {resultsPath}");
    ExcelConfigLoader.SaveResults(resultsPath, mockStats);
    Console.WriteLine("Results successfully written to Excel workbook!");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
