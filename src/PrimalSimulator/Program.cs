using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using ExcelDataReader;
using PrimalGame.Config;
using PrimalGame;
using SlotFramework.Utilities;
using SlotFramework.Models;

string defaultPath = "FirePrimalsElephant95.xlsx";
string resultsPath = "FirePrimalsElephant95_Results.xlsx";

bool trackFullStats = true;
string filePath = defaultPath;

foreach (var arg in args)
{
    if (arg.Equals("--basic", StringComparison.OrdinalIgnoreCase))
    {
        trackFullStats = false;
    }
    else if (arg.Equals("--full", StringComparison.OrdinalIgnoreCase))
    {
        trackFullStats = true;
    }
    else if (!arg.StartsWith("-"))
    {
        filePath = arg;
    }
}

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
    string modeName = trackFullStats ? "FULL STATS" : "BASIC STATS";
    Console.WriteLine($"\nGenerating real simulation results (1,000,000 spins, mode: {modeName})...");
    
    var engine = new PrimalSlotEngine(config);
    var rng = new FastRandom();
    
    long totalWin = 0;
    long totalLineWin = 0;
    long totalFeatureWin = 0;
    int winSpins = 0;
    int totalSpins = 1000000;
    
    int spinsWithCollectorOnReel0Or4 = 0;
    int spinsWithFireCore = 0;
    int collectionTriggerSpins = 0;
    int totalPowerUpTriggers = 0;
    
    int totalJackpotBonusTriggers = 0;
    long totalJackpotBonusWin = 0;
    
    // Pot progression statistics
    int[] spinsWithPotTrigger = new int[4];
    int[] totalPotTriggers = new int[4];
    long[] totalPotTriggerPowers = new long[4];
    
    // Detailed stats for Lock & Slingo (Bonus 1)
    int totalLockSlingoTriggers = 0;
    long totalLockSlingoWin = 0;
    long totalLockSlingoSlingosCompleted = 0;
    double totalLockSlingoCashSum = 0;
    double totalLockSlingoLadderSum = 0;
    int totalLockSlingoMinWinApplied = 0;
    long totalLockSlingoSpinsAwarded = 0;
    int[] lockSlingoTriggersByPower = new int[config.LockSlingoSpins.Length];
    
    // Detailed collect feature stats
    int collectTriggersWith1Collector = 0;
    int collectTriggersWith2Collectors = 0;
    double totalCollectCashMultiplierSum = 0.0;
    long totalCollectFireCoresCount = 0;
    int spinsWithCollectorButNoFireCore = 0;
    int spinsWithFireCoreButNoCollector = 0;

    // Detailed jackpot bonus stats
    int[] jackpotTriggersByFireCoreCount = new int[16];
    long totalFireCoresOnJackpotTrigger = 0;

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

        if (trackFullStats)
        {
            // Scan screen for stats
            int fireCoreCount = 0;
            for (int r = 0; r < 5; r++)
            {
                for (int row = 0; row < 3; row++)
                {
                    if (spinResult.ScreenSymbols[r][row] == config.FireCoreSymbolId)
                    {
                        fireCoreCount++;
                    }
                }
            }

            int collectorCount = 0;
            for (int row = 0; row < 3; row++)
            {
                if (spinResult.ScreenSymbols[0][row] == config.CollectorSymbolId) collectorCount++;
                if (spinResult.ScreenSymbols[4][row] == config.CollectorSymbolId) collectorCount++;
            }

            if (collectorCount > 0)
            {
                spinsWithCollectorOnReel0Or4++;
            }
            if (fireCoreCount > 0)
            {
                spinsWithFireCore++;
            }

            if (collectorCount > 0 && fireCoreCount == 0)
            {
                spinsWithCollectorButNoFireCore++;
            }
            if (fireCoreCount > 0 && collectorCount == 0)
            {
                spinsWithFireCoreButNoCollector++;
            }

            if (spinResult.CollectorTriggered)
            {
                collectionTriggerSpins++;
                if (spinResult.CollectorCount == 1) collectTriggersWith1Collector++;
                else if (spinResult.CollectorCount == 2) collectTriggersWith2Collectors++;

                totalCollectCashMultiplierSum += spinResult.TotalCollectedMultiplier;
                totalCollectFireCoresCount += fireCoreCount;
            }

            if (spinResult.JackpotBonusTriggered)
            {
                totalJackpotBonusTriggers++;
                totalJackpotBonusWin += spinResult.JackpotBonusWin;
                totalFireCoresOnJackpotTrigger += fireCoreCount;
                if (fireCoreCount >= 0 && fireCoreCount < jackpotTriggersByFireCoreCount.Length)
                {
                    jackpotTriggersByFireCoreCount[fireCoreCount]++;
                }
                if (jackpotHits.ContainsKey(spinResult.WonJackpotName))
                {
                    jackpotHits[spinResult.WonJackpotName]++;
                    jackpotWins[spinResult.WonJackpotName] += spinResult.JackpotBonusWin;
                }
            }
            
            // Track pot landing and triggers
            for (int p = 0; p < 4; p++)
            {
                int symbolId = 10 + p;
                bool hasPotTrigger = false;
                for (int r = 0; r < 5; r++)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        if (spinResult.ScreenSymbols[r][row] == symbolId)
                        {
                            hasPotTrigger = true;
                            break;
                        }
                    }
                    if (hasPotTrigger) break;
                }
                if (hasPotTrigger)
                {
                    spinsWithPotTrigger[p]++;
                }
            }
            
            foreach (var potBonus in spinResult.TriggeredPotBonuses)
            {
                int p = potBonus.PotIndex;
                totalPotTriggers[p]++;
                totalPotTriggerPowers[p] += potBonus.Power;
                
                if (p == 0)
                {
                    totalLockSlingoTriggers++;
                    totalLockSlingoWin += potBonus.Win;
                    totalLockSlingoSlingosCompleted += potBonus.CompletedSlingos;
                    totalLockSlingoCashSum += potBonus.CashValuesSum;
                    totalLockSlingoLadderSum += potBonus.LadderPrize;
                    totalLockSlingoSpinsAwarded += config.LockSlingoSpins[potBonus.Power];
                    if (potBonus.MinWinApplied)
                    {
                        totalLockSlingoMinWinApplied++;
                    }
                    if (potBonus.Power >= 0 && potBonus.Power < lockSlingoTriggersByPower.Length)
                    {
                        lockSlingoTriggersByPower[potBonus.Power]++;
                    }
                }
            }
        }
        else
        {
            if (spinResult.JackpotBonusTriggered)
            {
                totalJackpotBonusWin += spinResult.JackpotBonusWin;
            }
            
            // In basic mode, only sum Lock & Slingo wins for proper RTP mapping and power spins count
            foreach (var potBonus in spinResult.TriggeredPotBonuses)
            {
                if (potBonus.PotIndex == 0)
                {
                    totalLockSlingoTriggers++;
                    totalLockSlingoWin += potBonus.Win;
                    totalLockSlingoSpinsAwarded += config.LockSlingoSpins[potBonus.Power];
                }
            }
        }
    }
    
    double totalRtp = (double)totalWin / (totalSpins * 100.0);
    double lineWinRtp = (double)totalLineWin / (totalSpins * 100.0);
    double hitFreq = (double)winSpins / totalSpins;
    
    double lockSlingoRtp = (double)totalLockSlingoWin / (totalSpins * 100.0);
    double lockSlingoTriggerChance = (double)totalLockSlingoTriggers / totalSpins;
    string lockSlingoTriggerFreqStr = lockSlingoTriggerChance > 0 ? $"1 in {1.0 / lockSlingoTriggerChance:F1} spins ({lockSlingoTriggerChance:P4})" : "Never";
    double avgLockSlingoWinMultiplier = totalLockSlingoTriggers > 0 ? (double)totalLockSlingoWin / (totalLockSlingoTriggers * 100.0) : 0.0;
    double avgStartingSpins = totalLockSlingoTriggers > 0 ? (double)totalLockSlingoSpinsAwarded / totalLockSlingoTriggers : 0.0;

    double jackpotBonusRtp = (double)totalJackpotBonusWin / (totalSpins * 100.0);
    double collectFeatureRtp = (double)(totalFeatureWin - totalJackpotBonusWin - totalLockSlingoWin) / (totalSpins * 100.0);
    
    // Construct order-preserving stats dictionary following the requested sections
    var stats = new Dictionary<string, string>();

    // SECTION 1: Overall top-level game stats
    stats["Simulation Date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    stats["Game Name"] = "4 Fire Primals";
    stats["Simulation Mode"] = modeName;
    stats["Total Spins Run"] = totalSpins.ToString("N0");
    stats["Total Return to Player (RTP)"] = $"{totalRtp:P2}";
    stats["Line Payout RTP"] = $"{lineWinRtp:P2}";
    stats["Collector Feature RTP"] = $"{collectFeatureRtp:P2}";
    stats["Jackpot Bonus RTP"] = $"{jackpotBonusRtp:P2}";
    stats["Lock & Slingo (Bonus 1) RTP"] = $"{lockSlingoRtp:P2}";
    stats["Bonus 2 RTP"] = "0.00%";
    stats["Bonus 3 RTP"] = "0.00%";
    stats["Bonus 4 RTP"] = "0.00%";
    stats["Hit Frequency"] = $"{hitFreq:P2}";
    stats["Stage 6 Power Max Triggers"] = totalPowerUpTriggers.ToString("N0");
    stats["Number of Base Game Stages"] = config.BaseGameStageWeights.Count.ToString();

    if (trackFullStats)
    {
        double collectorLandingChance = (double)spinsWithCollectorOnReel0Or4 / totalSpins;
        double fireCoreLandingChance = (double)spinsWithFireCore / totalSpins;
        double collectionTriggerChance = (double)collectionTriggerSpins / totalSpins;
        
        string collectorLandingFreqStr = collectorLandingChance > 0 ? $"1 in {1.0 / collectorLandingChance:F1} spins ({collectorLandingChance:P2})" : "Never";
        string fireCoreLandingFreqStr = fireCoreLandingChance > 0 ? $"1 in {1.0 / fireCoreLandingChance:F1} spins ({fireCoreLandingChance:P2})" : "Never";
        string collectionTriggerFreqStr = collectionTriggerChance > 0 ? $"1 in {1.0 / collectionTriggerChance:F1} spins ({collectionTriggerChance:P2})" : "Never";

        double avgFeatureWinMultiplier = collectionTriggerSpins > 0 ? (double)(totalFeatureWin - totalJackpotBonusWin - totalLockSlingoWin) / (collectionTriggerSpins * 100.0) : 0.0;
        double avgCollectedCashMultiplier = collectionTriggerSpins > 0 ? totalCollectCashMultiplierSum / collectionTriggerSpins : 0.0;
        double avgCollectedFireCores = collectionTriggerSpins > 0 ? (double)totalCollectFireCoresCount / collectionTriggerSpins : 0.0;

        // SECTION 2: Collector Feature
        stats["Collector Feature Trigger Frequency"] = collectionTriggerFreqStr;
        stats["Collector Average Pay when Triggered"] = $"{avgFeatureWinMultiplier:F2}x bet";
        stats["Collector Total Triggers"] = collectionTriggerSpins.ToString("N0");
        stats["Collector Single Collector Triggers (1x)"] = $"{collectTriggersWith1Collector:N0} ({(collectionTriggerSpins > 0 ? (double)collectTriggersWith1Collector/collectionTriggerSpins : 0.0):P2})";
        stats["Collector Double Collector Triggers (2x)"] = $"{collectTriggersWith2Collectors:N0} ({(collectionTriggerSpins > 0 ? (double)collectTriggersWith2Collectors/collectionTriggerSpins : 0.0):P2})";
        stats["Collector Average Cash Multiplier Collected"] = $"{avgCollectedCashMultiplier:F2}x bet";
        stats["Collector Average Fire Cores Collected"] = $"{avgCollectedFireCores:F2}";
        stats["Collector Waste Spins (Collector, no Cash)"] = $"{spinsWithCollectorButNoFireCore:N0} ({(double)spinsWithCollectorButNoFireCore/totalSpins:P2})";
        stats["Collector Uncollected Spins (Cash, no Collector)"] = $"{spinsWithFireCoreButNoCollector:N0} ({(double)spinsWithFireCoreButNoCollector/totalSpins:P2})";
        stats["Collector Landing Freq (Reel 0 or 4)"] = collectorLandingFreqStr;
        stats["Collector Landing Fire Core Freq"] = fireCoreLandingFreqStr;

        double jackpotBonusTriggerChance = (double)totalJackpotBonusTriggers / totalSpins;
        string jackpotBonusTriggerFreqStr = jackpotBonusTriggerChance > 0 ? $"1 in {1.0 / jackpotBonusTriggerChance:F1} spins ({jackpotBonusTriggerChance:P4})" : "Never";
        double avgJackpotBonusWinMultiplier = totalJackpotBonusTriggers > 0 ? (double)totalJackpotBonusWin / (totalJackpotBonusTriggers * 100.0) : 0.0;
        double avgFireCoresOnJackpotTrigger = totalJackpotBonusTriggers > 0 ? (double)totalFireCoresOnJackpotTrigger / totalJackpotBonusTriggers : 0.0;

        // SECTION 3: Jackpot Bonus
        stats["Jackpot Bonus Trigger Frequency"] = jackpotBonusTriggerFreqStr;
        stats["Jackpot Bonus Average Win"] = $"{avgJackpotBonusWinMultiplier:F2}x bet";
        stats["Jackpot Bonus Average Fire Cores on Trigger"] = $"{avgFireCoresOnJackpotTrigger:F2}";
        foreach (var jpName in config.JackpotNames)
        {
            int hits = jackpotHits[jpName];
            double winChanceInBonus = totalJackpotBonusTriggers > 0 ? (double)hits / totalJackpotBonusTriggers : 0.0;
            double jpRtp = (double)jackpotWins[jpName] / (totalSpins * 100.0);
            string jpFreqInBonus = winChanceInBonus > 0 ? $"1 in {1.0 / winChanceInBonus:F1} triggers" : "Never";
            stats[$"Jackpot {jpName} Hits"] = hits.ToString("N0");
            stats[$"Jackpot {jpName} RTP"] = $"{jpRtp:P4}";
            stats[$"Jackpot {jpName} Win % in Bonus"] = $"{winChanceInBonus:P2} ({jpFreqInBonus})";
        }
        for (int c = 1; c < jackpotTriggersByFireCoreCount.Length; c++)
        {
            int hits = jackpotTriggersByFireCoreCount[c];
            if (hits > 0)
            {
                stats[$"Jackpot Triggered by Landed {c} Fire Cores"] = $"{hits:N0} ({(double)hits/totalJackpotBonusTriggers:P2})";
            }
        }

        double avgLockSlingoSlingos = totalLockSlingoTriggers > 0 ? (double)totalLockSlingoSlingosCompleted / totalLockSlingoTriggers : 0.0;
        double avgLockSlingoCashSum = totalLockSlingoTriggers > 0 ? totalLockSlingoCashSum / totalLockSlingoTriggers : 0.0;
        double avgLockSlingoLadderSum = totalLockSlingoTriggers > 0 ? totalLockSlingoLadderSum / totalLockSlingoTriggers : 0.0;
        double lockSlingoMinWinPercent = totalLockSlingoTriggers > 0 ? (double)totalLockSlingoMinWinApplied / totalLockSlingoTriggers : 0.0;

        double landingChance1 = (double)spinsWithPotTrigger[0] / totalSpins;
        string landingFreqStr1 = landingChance1 > 0 ? $"1 in {1.0 / landingChance1:F1} spins ({landingChance1:P2})" : "Never";
        double avgPower1 = totalPotTriggers[0] > 0 ? (double)totalPotTriggerPowers[0] / totalPotTriggers[0] : 0.0;

        // SECTION 4: Bonus 1 (Lock & Slingo)
        stats["Bonus 1 Landing Pot Trigger Freq"] = landingFreqStr1;
        stats["Bonus 1 Trigger Frequency"] = lockSlingoTriggerFreqStr;
        stats["Bonus 1 Average Power on Trigger"] = $"{avgPower1:F2}";
        stats["Bonus 1 Average Starting Spins"] = $"{avgStartingSpins:F2} spins";
        stats["Bonus 1 Average Win"] = $"{avgLockSlingoWinMultiplier:F2}x bet";
        stats["Bonus 1 Average Completed Slingos"] = $"{avgLockSlingoSlingos:F2}";
        stats["Bonus 1 Average Cash Values Sum"] = $"{avgLockSlingoCashSum:F2}x bet";
        stats["Bonus 1 Average Ladder Prize"] = $"{avgLockSlingoLadderSum:F2}x bet";
        stats["Bonus 1 Guaranteed Minimum Applied %"] = $"{lockSlingoMinWinPercent:P2}";

        for (int L = 0; L < lockSlingoTriggersByPower.Length; L++)
        {
            int hits = lockSlingoTriggersByPower[L];
            double pctOfTriggers = totalLockSlingoTriggers > 0 ? (double)hits / totalLockSlingoTriggers : 0.0;
            double hitRate = (double)hits / totalSpins;
            string freqStr = hits > 0 ? $"1 in {(1.0 / hitRate):N1} spins" : "Never";
            stats[$"Bonus 1 Power {L} ({config.LockSlingoSpins[L]} spins) Hits"] = $"{hits:N0} ({pctOfTriggers:P2} of triggers, {freqStr})";
        }

        // SECTION 5: Bonus 2
        double landingChance2 = (double)spinsWithPotTrigger[1] / totalSpins;
        string landingFreqStr2 = landingChance2 > 0 ? $"1 in {1.0 / landingChance2:F1} spins ({landingChance2:P2})" : "Never";
        double triggerChance2 = (double)totalPotTriggers[1] / totalSpins;
        string triggerFreqStr2 = triggerChance2 > 0 ? $"1 in {1.0 / triggerChance2:F1} spins ({triggerChance2:P4})" : "Never";
        double avgPower2 = totalPotTriggers[1] > 0 ? (double)totalPotTriggerPowers[1] / totalPotTriggers[1] : 0.0;

        stats["Bonus 2 Landing Pot Trigger Freq"] = landingFreqStr2;
        stats["Bonus 2 Trigger Frequency"] = triggerFreqStr2;
        stats["Bonus 2 Average Power on Trigger"] = $"{avgPower2:F2}";

        // SECTION 6: Bonus 3
        double landingChance3 = (double)spinsWithPotTrigger[2] / totalSpins;
        string landingFreqStr3 = landingChance3 > 0 ? $"1 in {1.0 / landingChance3:F1} spins ({landingChance3:P2})" : "Never";
        double triggerChance3 = (double)totalPotTriggers[2] / totalSpins;
        string triggerFreqStr3 = triggerChance3 > 0 ? $"1 in {1.0 / triggerChance3:F1} spins ({triggerChance3:P4})" : "Never";
        double avgPower3 = totalPotTriggers[2] > 0 ? (double)totalPotTriggerPowers[2] / totalPotTriggers[2] : 0.0;

        stats["Bonus 3 Landing Pot Trigger Freq"] = landingFreqStr3;
        stats["Bonus 3 Trigger Frequency"] = triggerFreqStr3;
        stats["Bonus 3 Average Power on Trigger"] = $"{avgPower3:F2}";

        // SECTION 7: Bonus 4
        double landingChance4 = (double)spinsWithPotTrigger[3] / totalSpins;
        string landingFreqStr4 = landingChance4 > 0 ? $"1 in {1.0 / landingChance4:F1} spins ({landingChance4:P2})" : "Never";
        double triggerChance4 = (double)totalPotTriggers[3] / totalSpins;
        string triggerFreqStr4 = triggerChance4 > 0 ? $"1 in {1.0 / triggerChance4:F1} spins ({triggerChance4:P4})" : "Never";
        double avgPower4 = totalPotTriggers[3] > 0 ? (double)totalPotTriggerPowers[3] / totalPotTriggers[3] : 0.0;

        stats["Bonus 4 Landing Pot Trigger Freq"] = landingFreqStr4;
        stats["Bonus 4 Trigger Frequency"] = triggerFreqStr4;
        stats["Bonus 4 Average Power on Trigger"] = $"{avgPower4:F2}";

        // Console Prints following the exact structured hierarchy:
        Console.WriteLine($"Simulation complete!");
        Console.WriteLine($"  - Total RTP: {totalRtp:P2} (Line RTP: {lineWinRtp:P2}, Collect Feature RTP: {collectFeatureRtp:P2}, Jackpot Bonus RTP: {jackpotBonusRtp:P2}, Lock & Slingo RTP: {lockSlingoRtp:P2})");
        Console.WriteLine($"  - Hit Frequency: {hitFreq:P2}");
        
        Console.WriteLine("\n=========================================================================================");
        Console.WriteLine("INDIVIDUAL FEATURE STATS BREAKDOWNS:");
        Console.WriteLine("=========================================================================================");

        Console.WriteLine("\n[Collector Feature]");
        Console.WriteLine($"  - Collect Feature RTP: {collectFeatureRtp:P2}");
        Console.WriteLine($"  - Collection Trigger Freq: {collectionTriggerFreqStr}");
        Console.WriteLine($"  - Average Pay when Collector Triggers: {avgFeatureWinMultiplier:F2}x bet");
        Console.WriteLine($"  - Total Collection Triggers: {collectionTriggerSpins}");
        Console.WriteLine($"    - Single Collector (1x collect): {collectTriggersWith1Collector} triggers ({(collectionTriggerSpins > 0 ? (double)collectTriggersWith1Collector/collectionTriggerSpins : 0.0):P2})");
        Console.WriteLine($"    - Double Collector (2x collect): {collectTriggersWith2Collectors} triggers ({(collectionTriggerSpins > 0 ? (double)collectTriggersWith2Collectors/collectionTriggerSpins : 0.0):P2})");
        Console.WriteLine($"  - Avg Cash Value Sum Collected: {avgCollectedCashMultiplier:F2}x bet");
        Console.WriteLine($"  - Avg Fire Cores Collected: {avgCollectedFireCores:F2}");
        Console.WriteLine($"  - Spins with Collector but no Fire Core (Waste): {spinsWithCollectorButNoFireCore} spins ({(double)spinsWithCollectorButNoFireCore/totalSpins:P2})");
        Console.WriteLine($"  - Spins with Fire Core but no Collector (Uncollected): {spinsWithFireCoreButNoCollector} spins ({(double)spinsWithFireCoreButNoCollector/totalSpins:P2})");
        Console.WriteLine($"  - Landing Collector Freq (Reel 0 or 4): {collectorLandingFreqStr}");
        Console.WriteLine($"  - Landing Fire Core Freq: {fireCoreLandingFreqStr}");

        Console.WriteLine("\n[Jackpot Bonus]");
        Console.WriteLine($"  - Jackpot Bonus RTP: {jackpotBonusRtp:P2}");
        Console.WriteLine($"  - Jackpot Bonus Trigger Freq: {jackpotBonusTriggerFreqStr}");
        Console.WriteLine($"  - Average Pay when Jackpot Bonus Triggers: {avgJackpotBonusWinMultiplier:F2}x bet");
        Console.WriteLine($"  - Avg Fire Cores on Screen when Triggered: {avgFireCoresOnJackpotTrigger:F2}");
        Console.WriteLine("  - Hit Distribution by Landing Fire Cores Count:");
        for (int c = 1; c < jackpotTriggersByFireCoreCount.Length; c++)
        {
            int hits = jackpotTriggersByFireCoreCount[c];
            if (hits > 0)
            {
                double pctOfTriggers = totalJackpotBonusTriggers > 0 ? (double)hits / totalJackpotBonusTriggers : 0.0;
                Console.WriteLine($"    Landed {c} Fire Cores: Hits = {hits,4} | {pctOfTriggers,6:P2} of triggers");
            }
        }
        Console.WriteLine("  - Jackpot Winners Distribution:");
        foreach (var jpName in config.JackpotNames)
        {
            int hits = jackpotHits[jpName];
            double winChanceInBonus = totalJackpotBonusTriggers > 0 ? (double)hits / totalJackpotBonusTriggers : 0.0;
            double jpRtp = (double)jackpotWins[jpName] / (totalSpins * 100.0);
            string jpFreqInBonus = winChanceInBonus > 0 ? $"1 in {1.0 / winChanceInBonus:F1} triggers" : "Never";
            Console.WriteLine($"    - {jpName,-6} Jackpot: Hits = {hits,6:N0} | RTP = {jpRtp,8:P4} | Win Chance in Bonus = {winChanceInBonus,8:P2} ({jpFreqInBonus})");
        }

        Console.WriteLine("\n[Bonus 1 - Lock & Slingo]");
        Console.WriteLine($"  - Lock & Slingo Total RTP: {lockSlingoRtp:P2}");
        Console.WriteLine($"  - Landing Pot Trigger Freq: {landingFreqStr1}");
        Console.WriteLine($"  - Trigger Frequency: {lockSlingoTriggerFreqStr}");
        Console.WriteLine($"  - Average Power on Trigger: {avgPower1:F2}");
        Console.WriteLine($"  - Average Starting Spins: {avgStartingSpins:F2} spins");
        Console.WriteLine($"  - Average Lock & Slingo Win: {avgLockSlingoWinMultiplier:F2}x bet");
        Console.WriteLine($"  - Average Completed Slingos: {avgLockSlingoSlingos:F2}");
        Console.WriteLine($"  - Average Cash Values Sum: {avgLockSlingoCashSum:F2}x bet");
        Console.WriteLine($"  - Average Ladder Prize: {avgLockSlingoLadderSum:F2}x bet");
        Console.WriteLine($"  - Guaranteed Minimum Win Applied %: {lockSlingoMinWinPercent:P2}");
        Console.WriteLine("  - Hit Distribution by Power Level:");
        for (int L = 0; L < lockSlingoTriggersByPower.Length; L++)
        {
            int hits = lockSlingoTriggersByPower[L];
            double pctOfTriggers = totalLockSlingoTriggers > 0 ? (double)hits / totalLockSlingoTriggers : 0.0;
            double hitRate = (double)hits / totalSpins;
            string freqStr = hits > 0 ? $"1 in {(1.0 / hitRate):N1} spins" : "Never";
            Console.WriteLine($"    Power Level {L} ({config.LockSlingoSpins[L]} spins): Hits = {hits,6:N0} | {pctOfTriggers,6:P2} of total triggers | {freqStr}");
        }

        Console.WriteLine("\n[Bonus 2]");
        Console.WriteLine($"  - Landing Pot Trigger Freq: {landingFreqStr2}");
        Console.WriteLine($"  - Trigger Frequency: {triggerFreqStr2}");
        Console.WriteLine($"  - Average Power on Trigger: {avgPower2:F2}");
        Console.WriteLine("  - Bonus 2 RTP: 0.00% (Placeholder)");

        Console.WriteLine("\n[Bonus 3]");
        Console.WriteLine($"  - Landing Pot Trigger Freq: {landingFreqStr3}");
        Console.WriteLine($"  - Trigger Frequency: {triggerFreqStr3}");
        Console.WriteLine($"  - Average Power on Trigger: {avgPower3:F2}");
        Console.WriteLine("  - Bonus 3 RTP: 0.00% (Placeholder)");

        Console.WriteLine("\n[Bonus 4]");
        Console.WriteLine($"  - Landing Pot Trigger Freq: {landingFreqStr4}");
        Console.WriteLine($"  - Trigger Frequency: {triggerFreqStr4}");
        Console.WriteLine($"  - Average Power on Trigger: {avgPower4:F2}");
        Console.WriteLine("  - Bonus 4 RTP: 0.00% (Placeholder)");

        Console.WriteLine($"\nStage 6 Power Max Triggers Count: {totalPowerUpTriggers}");
    }
    else
    {
        Console.WriteLine($"Simulation complete!");
        Console.WriteLine($"  - Total RTP: {totalRtp:P2} (Line RTP: {lineWinRtp:P2}, Collect Feature RTP: {collectFeatureRtp:P2}, Jackpot Bonus RTP: {jackpotBonusRtp:P2}, Lock & Slingo RTP: {lockSlingoRtp:P2})");
        Console.WriteLine($"  - Hit Frequency: {hitFreq:P2}");
        
        Console.WriteLine("\n=========================================================================================");
        Console.WriteLine("INDIVIDUAL FEATURE STATS BREAKDOWNS:");
        Console.WriteLine("=========================================================================================");
        
        Console.WriteLine("\n[Collector Feature]");
        Console.WriteLine($"  - Collect Feature RTP: {collectFeatureRtp:P2}");

        Console.WriteLine("\n[Jackpot Bonus]");
        Console.WriteLine($"  - Jackpot Bonus RTP: {jackpotBonusRtp:P2}");

        Console.WriteLine("\n[Bonus 1 - Lock & Slingo]");
        Console.WriteLine($"  - Lock & Slingo Trigger Freq: {lockSlingoTriggerFreqStr}");
        Console.WriteLine($"  - Lock & Slingo Total RTP: {lockSlingoRtp:P2}");
        Console.WriteLine($"  - Average Lock & Slingo Win: {avgLockSlingoWinMultiplier:F2}x bet");
        Console.WriteLine($"  - Average Starting Spins: {avgStartingSpins:F2} spins");

        Console.WriteLine("\n[Bonus 2]");
        Console.WriteLine("  - Bonus 2 RTP: 0.00% (Placeholder)");

        Console.WriteLine("\n[Bonus 3]");
        Console.WriteLine("  - Bonus 3 RTP: 0.00% (Placeholder)");

        Console.WriteLine("\n[Bonus 4]");
        Console.WriteLine("  - Bonus 4 RTP: 0.00% (Placeholder)");
    }
    
    Console.WriteLine($"\nWriting simulation results to: {resultsPath}");
    ExcelConfigLoader.SaveResults(resultsPath, stats);
    Console.WriteLine("Results successfully written to Excel workbook!");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
