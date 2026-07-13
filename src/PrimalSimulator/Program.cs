using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using ExcelDataReader;
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
    Console.WriteLine(new string('-', 120));
    for (int i = 0; i < config.StageSpinsToNext.Length; i++)
    {
        string unlockMsg = i switch
        {
            0 => "Bonus 1 & Collector feature unlocked",
            1 => "Bonus 2 unlocked",
            2 => "Bonus 3 unlocked",
            3 => "Bonus 4 unlocked",
            4 => "Stampede Spin unlocked",
            5 => "Guaranteed Bonus Minimums unlocked",
            6 => "Set a random bonus power to maximum (repeats itself)",
            _ => ""
        };
        Console.WriteLine($"Stage{i} -> Stage{(i == 6 ? 6 : i + 1)}: {config.StageSpinsToNext[i],-3} spins | {unlockMsg}");
    }
    Console.WriteLine(new string('-', 120));

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
    long totalLineWin = 0;
    long totalFeatureWin = 0;
    int winSpins = 0;
    int totalSpins = 1000000;
    
    int spinsWithCollectorOnReel0Or4 = 0;
    int spinsWithJackpotTrigger = 0;
    int collectionTriggerSpins = 0;
    int totalPowerUpTriggers = 0;
    
    int totalJackpotBonusTriggers = 0;
    long totalJackpotBonusWin = 0;
    
    var jackpotHits = new Dictionary<string, int>();
    var jackpotWins = new Dictionary<string, long>();
    foreach (var jpName in config.JackpotNames)
    {
        jackpotHits[jpName] = 0;
        jackpotWins[jpName] = 0;
    }
    
    for (int i = 0; i < totalSpins; i++)
    {
        var spinResult = engine.Spin(rng);
        totalWin += spinResult.TotalWin;
        totalFeatureWin += spinResult.FeatureWin;
        totalLineWin += (spinResult.TotalWin - spinResult.FeatureWin);
        
        if (spinResult.TotalWin > 0)
        {
            winSpins++;
        }

        if (spinResult.SetRandomBonusPowerToMax)
        {
            totalPowerUpTriggers++;
        }

        // Check if Collector landed on Reel 0 or Reel 4
        bool hasCollector = false;
        for (int row = 0; row < 3; row++)
        {
            if (spinResult.ScreenSymbols[0][row] == config.CollectorSymbolId ||
                spinResult.ScreenSymbols[4][row] == config.CollectorSymbolId)
            {
                hasCollector = true;
                break;
            }
        }
        if (hasCollector)
        {
            spinsWithCollectorOnReel0Or4++;
        }

        // Check if Jackpot Trigger landed anywhere
        bool hasJackpotTrigger = false;
        for (int r = 0; r < 5; r++)
        {
            for (int row = 0; row < 3; row++)
            {
                if (spinResult.ScreenSymbols[r][row] == config.JackpotTriggerSymbolId)
                {
                    hasJackpotTrigger = true;
                    break;
                }
            }
            if (hasJackpotTrigger) break;
        }
        if (hasJackpotTrigger)
        {
            spinsWithJackpotTrigger++;
        }

        if (spinResult.CollectorTriggered)
        {
            collectionTriggerSpins++;
        }

        if (spinResult.JackpotBonusTriggered)
        {
            totalJackpotBonusTriggers++;
            totalJackpotBonusWin += spinResult.JackpotBonusWin;
            if (jackpotHits.ContainsKey(spinResult.WonJackpotName))
            {
                jackpotHits[spinResult.WonJackpotName]++;
                jackpotWins[spinResult.WonJackpotName] += spinResult.JackpotBonusWin;
            }
        }
    }
    
    double totalRtp = (double)totalWin / (totalSpins * 100.0);
    double lineWinRtp = (double)totalLineWin / (totalSpins * 100.0);
    double totalFeatureRtp = (double)totalFeatureWin / (totalSpins * 100.0);
    double hitFreq = (double)winSpins / totalSpins;
    
    double collectorLandingChance = (double)spinsWithCollectorOnReel0Or4 / totalSpins;
    double jackpotLandingChance = (double)spinsWithJackpotTrigger / totalSpins;
    double collectionTriggerChance = (double)collectionTriggerSpins / totalSpins;
    
    string collectorLandingFreqStr = collectorLandingChance > 0 ? $"1 in {1.0 / collectorLandingChance:F1} spins ({collectorLandingChance:P2})" : "Never";
    string jackpotLandingFreqStr = jackpotLandingChance > 0 ? $"1 in {1.0 / jackpotLandingChance:F1} spins ({jackpotLandingChance:P2})" : "Never";
    string collectionTriggerFreqStr = collectionTriggerChance > 0 ? $"1 in {1.0 / collectionTriggerChance:F1} spins ({collectionTriggerChance:P2})" : "Never";

    double avgFeatureWinMultiplier = collectionTriggerSpins > 0 ? (double)(totalFeatureWin - totalJackpotBonusWin) / (collectionTriggerSpins * 100.0) : 0.0;
    double collectFeatureRtp = (double)(totalFeatureWin - totalJackpotBonusWin) / (totalSpins * 100.0);

    double jackpotBonusTriggerChance = (double)totalJackpotBonusTriggers / totalSpins;
    string jackpotBonusTriggerFreqStr = jackpotBonusTriggerChance > 0 ? $"1 in {1.0 / jackpotBonusTriggerChance:F1} spins ({jackpotBonusTriggerChance:P4})" : "Never";
    double avgJackpotBonusWinMultiplier = totalJackpotBonusTriggers > 0 ? (double)totalJackpotBonusWin / (totalJackpotBonusTriggers * 100.0) : 0.0;
    double jackpotBonusRtp = (double)totalJackpotBonusWin / (totalSpins * 100.0);

    var stats = new Dictionary<string, string>
    {
        { "Simulation Date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
        { "Game Name", "4 Fire Primals" },
        { "Total Spins Run", totalSpins.ToString("N0") },
        { "Line Payout RTP", $"{lineWinRtp:P2}" },
        { "Jackpot Collect Feature RTP", $"{collectFeatureRtp:P2}" },
        { "Jackpot Bonus RTP", $"{jackpotBonusRtp:P2}" },
        { "Total Return to Player (RTP)", $"{totalRtp:P2}" },
        { "Hit Frequency", $"{hitFreq:P2}" },
        { "Collection Trigger Frequency", collectionTriggerFreqStr },
        { "Average Pay when Collector Triggers", $"{avgFeatureWinMultiplier:F2}x bet" },
        { "Landing Collector Frequency (Reel 0 or 4)", collectorLandingFreqStr },
        { "Landing Jackpot Trigger Frequency", jackpotLandingFreqStr },
        { "Jackpot Bonus Trigger Frequency", jackpotBonusTriggerFreqStr },
        { "Average Pay when Jackpot Bonus Triggers", $"{avgJackpotBonusWinMultiplier:F2}x bet" }
    };

    foreach (var jpName in config.JackpotNames)
    {
        int hits = jackpotHits[jpName];
        double winChanceInBonus = totalJackpotBonusTriggers > 0 ? (double)hits / totalJackpotBonusTriggers : 0.0;
        double jpRtp = (double)jackpotWins[jpName] / (totalSpins * 100.0);
        string jpFreqInBonus = winChanceInBonus > 0 ? $"1 in {1.0 / winChanceInBonus:F1} triggers" : "Never";
        
        stats[$"{jpName} Jackpot Hits"] = hits.ToString("N0");
        stats[$"{jpName} Jackpot RTP"] = $"{jpRtp:P4}";
        stats[$"{jpName} Jackpot Win % in Bonus"] = $"{winChanceInBonus:P2} ({jpFreqInBonus})";
    }

    stats["Stage 6 Power Max Triggers"] = totalPowerUpTriggers.ToString("N0");
    stats["Number of Base Game Stages"] = config.BaseGameStageWeights.Count.ToString();
    stats["Total Stage Spins Sum"] = string.Join(", ", config.StageSpinsToNext);

    Console.WriteLine($"Simulation complete!");
    Console.WriteLine($"  - Total RTP: {totalRtp:P2} (Line RTP: {lineWinRtp:P2}, Collect Feature RTP: {collectFeatureRtp:P2}, Jackpot Bonus RTP: {jackpotBonusRtp:P2})");
    Console.WriteLine($"  - Hit Frequency: {hitFreq:P2}");
    Console.WriteLine($"  - Collection Trigger Freq: {collectionTriggerFreqStr}");
    Console.WriteLine($"  - Average Pay when Collector Triggers: {avgFeatureWinMultiplier:F2}x bet");
    Console.WriteLine($"  - Landing Collector Freq: {collectorLandingFreqStr}");
    Console.WriteLine($"  - Landing Jackpot Trigger Freq: {jackpotLandingFreqStr}");
    Console.WriteLine($"  - Jackpot Bonus Trigger Freq: {jackpotBonusTriggerFreqStr}");
    Console.WriteLine($"  - Average Pay when Jackpot Bonus Triggers: {avgJackpotBonusWinMultiplier:F2}x bet");
    
    Console.WriteLine("\nJackpot Breakdown:");
    foreach (var jpName in config.JackpotNames)
    {
        int hits = jackpotHits[jpName];
        double winChanceInBonus = totalJackpotBonusTriggers > 0 ? (double)hits / totalJackpotBonusTriggers : 0.0;
        double jpRtp = (double)jackpotWins[jpName] / (totalSpins * 100.0);
        string jpFreqInBonus = winChanceInBonus > 0 ? $"1 in {1.0 / winChanceInBonus:F1} triggers" : "Never";
        Console.WriteLine($"  - {jpName,-6} Jackpot: Hits = {hits,6:N0} | RTP = {jpRtp,8:P4} | Win Chance in Bonus = {winChanceInBonus,8:P2} ({jpFreqInBonus})");
    }
    
    Console.WriteLine($"\nStage 6 Power Max Triggers Count: {totalPowerUpTriggers}");
    Console.WriteLine($"Writing simulation results to: {resultsPath}");
    ExcelConfigLoader.SaveResults(resultsPath, stats);
    Console.WriteLine("Results successfully written to Excel workbook!");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
