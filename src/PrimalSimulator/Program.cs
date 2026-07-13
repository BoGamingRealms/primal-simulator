using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using PrimalGame.Config;
using PrimalGame;
using SlotFramework.Utilities;

string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
string downloadsFolder = Path.Combine(userProfile, "Downloads");
string defaultPath = Path.Combine(downloadsFolder, "FirePrimalsElephant95.xlsx");
string resultsPath = Path.Combine(downloadsFolder, "FirePrimalsElephant95_Results.xlsx");

string filePath = args.Length > 0 ? args[0] : defaultPath;

try
{
    Console.WriteLine($"Loading configuration from: {filePath}...");
    PrimalConfig config = ExcelConfigLoader.Load(filePath);
    
    // 1. Print Symbols & Paytable
    Console.WriteLine("\nLoaded Symbols & Paytable:");
    Console.WriteLine(new string('-', 85));
    Console.WriteLine($"{"ID",-4} | {"Symbol Name",-30} | {"Wild?",-6} | {"Payouts (in cents)"}");
    Console.WriteLine(new string('-', 85));
    
    foreach (var sym in config.Symbols)
    {
        long pay2 = config.Paytable.GetPayout(sym.Id, 2);
        long pay3 = config.Paytable.GetPayout(sym.Id, 3);
        long pay4 = config.Paytable.GetPayout(sym.Id, 4);
        long pay5 = config.Paytable.GetPayout(sym.Id, 5);
        
        List<string> payouts = new();
        if (pay2 > 0) payouts.Add($"2: {pay2}c");
        if (pay3 > 0) payouts.Add($"3: {pay3}c");
        if (pay4 > 0) payouts.Add($"4: {pay4}c");
        if (pay5 > 0) payouts.Add($"5: {pay5}c");
        
        string payoutInfo = payouts.Count > 0 ? string.Join(", ", payouts) : "No payout";
            
        Console.WriteLine($"{sym.Id,-4} | {sym.Name,-30} | {sym.IsWild,-6} | {payoutInfo}");
    }
    Console.WriteLine(new string('-', 85));

    // 2. Print Stage Spins To Next Stage
    Console.WriteLine("\nLoaded Stage Advancement Thresholds (Spins required to advance to next stage):");
    Console.WriteLine(new string('-', 100));
    for (int i = 0; i < config.StageSpinsToNext.Length; i++)
    {
        string repeatMsg = (i == 6) ? " (Stage6 repeats itself)" : "";
        Console.WriteLine($"Stage{i} -> Stage{(i == 6 ? 6 : i + 1)}: {config.StageSpinsToNext[i]} spins{repeatMsg}");
    }
    Console.WriteLine(new string('-', 100));

    // 3. Print Base Game Stages & Reelset Weights
    Console.WriteLine("\nLoaded Base Game Stages & Reelset Weights (20 Reelsets):");
    Console.WriteLine(new string('-', 100));
    Console.WriteLine($"{"Stage Name",-12} | {"Weights (Reelsets 0-19)"}");
    Console.WriteLine(new string('-', 100));
    foreach (var kvp in config.BaseGameStageWeights)
    {
        string weightsStr = string.Join(",", kvp.Value);
        Console.WriteLine($"{kvp.Key,-12} | {weightsStr}");
    }
    Console.WriteLine(new string('-', 100));

    // 4. Print Loaded Reelsets
    Console.WriteLine("\nLoaded Reelsets (Reelsets 0-19):");
    Console.WriteLine(new string('-', 100));
    foreach (var kvp in config.Reelsets)
    {
        Console.WriteLine($"Reelset Name: {kvp.Key}");
        for (int r = 0; r < 5; r++)
        {
            var strip = kvp.Value.Reels[r];
            string preview = string.Join(",", strip.Take(15));
            Console.WriteLine($"  Reel {r} (Len={strip.Length}): {preview}...");
        }
        Console.WriteLine();
    }
    Console.WriteLine(new string('-', 100));

    // 5. Generate and save simulation stats
    Console.WriteLine("\nGenerating real simulation results (1,000,000 spins)...");
    
    var engine = new PrimalSlotEngine(config);
    var rng = new FastRandom();
    
    long totalWin = 0;
    int winSpins = 0;
    int totalSpins = 1000000;
    
    for (int i = 0; i < totalSpins; i++)
    {
        var spinResult = engine.Spin(rng);
        totalWin += spinResult.TotalWin;
        if (spinResult.TotalWin > 0)
        {
            winSpins++;
        }
    }
    
    double rtp = (double)totalWin / (totalSpins * 100.0);
    double hitFreq = (double)winSpins / totalSpins;

    var stats = new Dictionary<string, string>
    {
        { "Simulation Date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
        { "Game Name", "4 Fire Primals" },
        { "Total Spins Run", totalSpins.ToString("N0") },
        { "Base Game RTP", $"{rtp:P2}" },
        { "Total Return to Player (RTP)", $"{rtp:P2}" },
        { "Hit Frequency", $"{hitFreq:P2}" },
        { "Number of Base Game Stages", config.BaseGameStageWeights.Count.ToString() },
        { "Total Stage Spins Sum", string.Join(", ", config.StageSpinsToNext) }
    };

    Console.WriteLine($"Simulation complete! RTP: {rtp:P2}, Hit Frequency: {hitFreq:P2}");
    Console.WriteLine($"Writing simulation results to: {resultsPath}");
    ExcelConfigLoader.SaveResults(resultsPath, stats);
    Console.WriteLine("Results successfully written to Excel workbook!");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
