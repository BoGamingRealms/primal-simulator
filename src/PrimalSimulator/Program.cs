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

string defaultPath = "FirePrimalElephant95.xlsx";
string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
string downloadsFolder = Path.Combine(userProfile, "Downloads");
string resultsPath = Path.Combine(downloadsFolder, "FirePrimalElephant95_Results.xlsx");

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
    
    var sw = System.Diagnostics.Stopwatch.StartNew();
    
    int totalSpins = 1000000;
    int workerCount = Math.Max(1, Environment.ProcessorCount);
    int baseSpinsPerWorker = totalSpins / workerCount;
    var workers = new SimWorkerStats[workerCount];

    Parallel.For(0, workerCount, w =>
    {
        int spinsForThisWorker = (w == workerCount - 1)
            ? baseSpinsPerWorker + (totalSpins - (baseSpinsPerWorker * workerCount))
            : baseSpinsPerWorker;

        var localEngine = new PrimalSlotEngine(config);
        var localRng = new FastRandom((ulong)(123456789012345UL + (ulong)w * 9876543210987UL + (ulong)DateTime.UtcNow.Ticks));
        var localStats = new SimWorkerStats(config);

        for (int i = 0; i < spinsForThisWorker; i++)
        {
            var spinResult = localEngine.Spin(localRng);
            localStats.Record(spinResult, config, trackFullStats);
        }

        workers[w] = localStats;
    });

    sw.Stop();
    Console.WriteLine($"\nSimulation finished in {sw.ElapsedMilliseconds} ms ({totalSpins / (sw.Elapsed.TotalSeconds):N0} spins/sec across {workerCount} CPU threads)!");

    // Merge all worker stats into aggregate counters
    long totalWin = 0;
    long totalLineWin = 0;
    long totalFeatureWin = 0;
    int winSpins = 0;
    int totalPowerUpTriggers = 0;

    int spinsWithCollectorOnReel0Or4 = 0;
    int spinsWithFireCore = 0;
    int collectionTriggerSpins = 0;
    int collectTriggersWith1Collector = 0;
    int collectTriggersWith2Collectors = 0;
    double totalCollectCashMultiplierSum = 0.0;
    long totalCollectFireCoresCount = 0;
    int spinsWithCollectorButNoFireCore = 0;
    int spinsWithFireCoreButNoCollector = 0;

    int totalJackpotBonusTriggers = 0;
    long totalJackpotBonusWin = 0;
    long totalFireCoresOnJackpotTrigger = 0;
    int[] jackpotTriggersByFireCoreCount = new int[16];
    var jackpotHits = new Dictionary<string, int>();
    var jackpotWins = new Dictionary<string, long>();
    foreach (var jpName in config.JackpotNames)
    {
        jackpotHits[jpName] = 0;
        jackpotWins[jpName] = 0;
    }

    int[] spinsWithPotTrigger = new int[4];
    int[] totalPotTriggers = new int[4];
    long[] totalPotTriggerPowers = new long[4];

    int totalLockSlingoTriggers = 0;
    long totalLockSlingoWin = 0;
    long totalLockSlingoSlingosCompleted = 0;
    double totalLockSlingoCashSum = 0;
    double totalLockSlingoLadderSum = 0;
    int totalLockSlingoMinWinApplied = 0;
    long totalLockSlingoSpinsAwarded = 0;
    int[] lockSlingoTriggersByPower = new int[config.LockSlingoSpins.Length];

    int totalApexSpinsTriggers = 0;
    long totalApexSpinsWin = 0;
    long totalApexSpinsPlayed = 0;
    int totalApexSpinsMinWinApplied = 0;
    int[] apexSpinsTriggersByPower = new int[config.ApexSpinsTopAwardMultipliers.Length];
    long[] apexSpinsWinByPower = new long[config.ApexSpinsTopAwardMultipliers.Length];
    long[] apexSpinsPlayedByPower = new long[config.ApexSpinsTopAwardMultipliers.Length];

    int totalColossalSpinsTriggers = 0;
    long totalColossalSpinsWin = 0;
    long totalColossalSpinsPlayed = 0;
    int totalColossalSpinsMinWinApplied = 0;
    int[] colossalSpinsTriggersByPower = new int[config.ColossalSpinsCounts.Length];
    long[] colossalSpinsWinByPower = new long[config.ColossalSpinsCounts.Length];
    long[] colossalSpinsPlayedByPower = new long[config.ColossalSpinsCounts.Length];
    var colossalSymbolHits = new Dictionary<int, long>();
    var colossalSymbolWins = new Dictionary<int, long>();

    int totalPrimalZoneTriggers = 0;
    long totalPrimalZoneWin = 0;
    long totalPrimalZonePlayed = 0;
    long totalPrimalZoneBananas = 0;
    int totalPrimalZoneMinWinApplied = 0;
    int[] primalZoneTriggersByPower = new int[config.PrimalZoneSpins.Length];
    long[] primalZoneWinByPower = new long[config.PrimalZoneSpins.Length];
    long[] primalZonePlayedByPower = new long[config.PrimalZoneSpins.Length];
    int[] primalZoneStageHits = new int[4];

    int totalStampedeSpins = 0;
    long totalStampedeWin = 0;
    long totalStampedePotsAdded = 0;

    foreach (var w in workers)
    {
        totalWin += w.TotalWin;
        totalLineWin += w.TotalLineWin;
        totalFeatureWin += w.TotalFeatureWin;
        winSpins += w.WinSpins;
        totalPowerUpTriggers += w.TotalPowerUpTriggers;

        spinsWithCollectorOnReel0Or4 += w.SpinsWithCollectorOnReel0Or4;
        spinsWithFireCore += w.SpinsWithFireCore;
        collectionTriggerSpins += w.CollectionTriggerSpins;
        collectTriggersWith1Collector += w.CollectTriggersWith1Collector;
        collectTriggersWith2Collectors += w.CollectTriggersWith2Collectors;
        totalCollectCashMultiplierSum += w.TotalCollectCashMultiplierSum;
        totalCollectFireCoresCount += w.TotalCollectFireCoresCount;
        spinsWithCollectorButNoFireCore += w.SpinsWithCollectorButNoFireCore;
        spinsWithFireCoreButNoCollector += w.SpinsWithFireCoreButNoCollector;

        totalJackpotBonusTriggers += w.TotalJackpotBonusTriggers;
        totalJackpotBonusWin += w.TotalJackpotBonusWin;
        totalFireCoresOnJackpotTrigger += w.TotalFireCoresOnJackpotTrigger;

        for (int i = 0; i < 16; i++)
        {
            jackpotTriggersByFireCoreCount[i] += w.JackpotTriggersByFireCoreCount[i];
        }

        foreach (var kvp in w.JackpotHits)
        {
            jackpotHits[kvp.Key] = jackpotHits.GetValueOrDefault(kvp.Key) + kvp.Value;
        }
        foreach (var kvp in w.JackpotWins)
        {
            jackpotWins[kvp.Key] = jackpotWins.GetValueOrDefault(kvp.Key) + kvp.Value;
        }

        for (int i = 0; i < 4; i++)
        {
            spinsWithPotTrigger[i] += w.SpinsWithPotTrigger[i];
            totalPotTriggers[i] += w.TotalPotTriggers[i];
            totalPotTriggerPowers[i] += w.TotalPotTriggerPowers[i];
        }

        totalLockSlingoTriggers += w.TotalLockSlingoTriggers;
        totalLockSlingoWin += w.TotalLockSlingoWin;
        totalLockSlingoSlingosCompleted += w.TotalLockSlingoSlingosCompleted;
        totalLockSlingoCashSum += w.TotalLockSlingoCashSum;
        totalLockSlingoLadderSum += w.TotalLockSlingoLadderSum;
        totalLockSlingoMinWinApplied += w.TotalLockSlingoMinWinApplied;
        totalLockSlingoSpinsAwarded += w.TotalLockSlingoSpinsAwarded;
        for (int i = 0; i < lockSlingoTriggersByPower.Length; i++)
        {
            lockSlingoTriggersByPower[i] += w.LockSlingoTriggersByPower[i];
        }

        totalApexSpinsTriggers += w.TotalApexSpinsTriggers;
        totalApexSpinsWin += w.TotalApexSpinsWin;
        totalApexSpinsPlayed += w.TotalApexSpinsPlayed;
        totalApexSpinsMinWinApplied += w.TotalApexSpinsMinWinApplied;
        for (int i = 0; i < apexSpinsTriggersByPower.Length; i++)
        {
            apexSpinsTriggersByPower[i] += w.ApexSpinsTriggersByPower[i];
            apexSpinsWinByPower[i] += w.ApexSpinsWinByPower[i];
            apexSpinsPlayedByPower[i] += w.ApexSpinsPlayedByPower[i];
        }

        totalColossalSpinsTriggers += w.TotalColossalSpinsTriggers;
        totalColossalSpinsWin += w.TotalColossalSpinsWin;
        totalColossalSpinsPlayed += w.TotalColossalSpinsPlayed;
        totalColossalSpinsMinWinApplied += w.TotalColossalSpinsMinWinApplied;
        for (int i = 0; i < colossalSpinsTriggersByPower.Length; i++)
        {
            colossalSpinsTriggersByPower[i] += w.ColossalSpinsTriggersByPower[i];
            colossalSpinsWinByPower[i] += w.ColossalSpinsWinByPower[i];
            colossalSpinsPlayedByPower[i] += w.ColossalSpinsPlayedByPower[i];
        }
        foreach (var kvp in w.ColossalSymbolHits)
        {
            colossalSymbolHits[kvp.Key] = colossalSymbolHits.GetValueOrDefault(kvp.Key) + kvp.Value;
        }
        foreach (var kvp in w.ColossalSymbolWins)
        {
            colossalSymbolWins[kvp.Key] = colossalSymbolWins.GetValueOrDefault(kvp.Key) + kvp.Value;
        }

        totalPrimalZoneTriggers += w.TotalPrimalZoneTriggers;
        totalPrimalZoneWin += w.TotalPrimalZoneWin;
        totalPrimalZonePlayed += w.TotalPrimalZonePlayed;
        totalPrimalZoneBananas += w.TotalPrimalZoneBananas;
        totalPrimalZoneMinWinApplied += w.TotalPrimalZoneMinWinApplied;
        for (int i = 0; i < primalZoneTriggersByPower.Length; i++)
        {
            primalZoneTriggersByPower[i] += w.PrimalZoneTriggersByPower[i];
            primalZoneWinByPower[i] += w.PrimalZoneWinByPower[i];
            primalZonePlayedByPower[i] += w.PrimalZonePlayedByPower[i];
        }
        for (int i = 0; i < 4; i++)
        {
            primalZoneStageHits[i] += w.PrimalZoneStageHits[i];
        }

        totalStampedeSpins += w.TotalStampedeSpins;
        totalStampedeWin += w.TotalStampedeWin;
        totalStampedePotsAdded += w.TotalStampedePotsAdded;
    }
    
    double totalRtp = (double)totalWin / (totalSpins * 100.0);
    double lineWinRtp = (double)totalLineWin / (totalSpins * 100.0);
    double hitFreq = (double)winSpins / totalSpins;
    
    double lockSlingoRtp = (double)totalLockSlingoWin / (totalSpins * 100.0);
    double lockSlingoTriggerChance = (double)totalLockSlingoTriggers / totalSpins;
    string lockSlingoTriggerFreqStr = lockSlingoTriggerChance > 0 ? $"1 in {1.0 / lockSlingoTriggerChance:F1} spins ({lockSlingoTriggerChance:P4})" : "Never";
    double avgLockSlingoWinMultiplier = totalLockSlingoTriggers > 0 ? (double)totalLockSlingoWin / (totalLockSlingoTriggers * 100.0) : 0.0;
    double avgStartingSpins = totalLockSlingoTriggers > 0 ? (double)totalLockSlingoSpinsAwarded / totalLockSlingoTriggers : 0.0;

    double apexSpinsRtp = (double)totalApexSpinsWin / (totalSpins * 100.0);
    double apexSpinsTriggerChance = (double)totalApexSpinsTriggers / totalSpins;
    string apexSpinsTriggerFreqStr = apexSpinsTriggerChance > 0 ? $"1 in {1.0 / apexSpinsTriggerChance:F1} spins ({apexSpinsTriggerChance:P4})" : "Never";
    double avgApexSpinsWinMultiplier = totalApexSpinsTriggers > 0 ? (double)totalApexSpinsWin / (totalApexSpinsTriggers * 100.0) : 0.0;
    double avgApexSpinsSpinsPlayed = totalApexSpinsTriggers > 0 ? (double)totalApexSpinsPlayed / totalApexSpinsTriggers : 0.0;

    double colossalSpinsRtp = (double)totalColossalSpinsWin / (totalSpins * 100.0);
    double colossalSpinsTriggerChance = (double)totalColossalSpinsTriggers / totalSpins;
    string colossalSpinsTriggerFreqStr = colossalSpinsTriggerChance > 0 ? $"1 in {1.0 / colossalSpinsTriggerChance:F1} spins ({colossalSpinsTriggerChance:P4})" : "Never";
    double avgColossalSpinsWinMultiplier = totalColossalSpinsTriggers > 0 ? (double)totalColossalSpinsWin / (totalColossalSpinsTriggers * 100.0) : 0.0;
    double avgColossalSpinsSpinsPlayed = totalColossalSpinsTriggers > 0 ? (double)totalColossalSpinsPlayed / totalColossalSpinsTriggers : 0.0;

    double primalZoneRtp = (double)totalPrimalZoneWin / (totalSpins * 100.0);
    double primalZoneTriggerChance = (double)totalPrimalZoneTriggers / totalSpins;
    string primalZoneTriggerFreqStr = primalZoneTriggerChance > 0 ? $"1 in {1.0 / primalZoneTriggerChance:F1} spins ({primalZoneTriggerChance:P4})" : "Never";
    double avgPrimalZoneWinMultiplier = totalPrimalZoneTriggers > 0 ? (double)totalPrimalZoneWin / (totalPrimalZoneTriggers * 100.0) : 0.0;
    double avgPrimalZoneSpinsPlayed = totalPrimalZoneTriggers > 0 ? (double)totalPrimalZonePlayed / totalPrimalZoneTriggers : 0.0;
    double avgPrimalZoneBananas = totalPrimalZoneTriggers > 0 ? (double)totalPrimalZoneBananas / totalPrimalZoneTriggers : 0.0;
    double primalZoneMinWinPercent = totalPrimalZoneTriggers > 0 ? (double)totalPrimalZoneMinWinApplied / totalPrimalZoneTriggers : 0.0;

    double jackpotBonusRtp = (double)totalJackpotBonusWin / (totalSpins * 100.0);
    double collectFeatureRtp = (double)(totalFeatureWin - totalJackpotBonusWin - totalLockSlingoWin - totalApexSpinsWin - totalColossalSpinsWin - totalPrimalZoneWin) / (totalSpins * 100.0);
    
    // Construct order-preserving stats dictionary following the requested sections
    var stats = new Dictionary<string, string>();

    // SECTION 1: Overall top-level game stats
    stats["Simulation Date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    stats["Game Name"] = "Fire Primal Elephant 95";
    stats["Simulation Mode"] = modeName;
    stats["Total Spins Run"] = totalSpins.ToString("N0");
    stats["Total Return to Player (RTP)"] = $"{totalRtp:P2}";
    stats["Line Payout RTP"] = $"{lineWinRtp:P2}";
    stats["Collector Feature RTP"] = $"{collectFeatureRtp:P2}";
    stats["Jackpot Bonus RTP"] = $"{jackpotBonusRtp:P2}";
    stats["Lock & Slingo (Bonus 1) RTP"] = $"{lockSlingoRtp:P2}";
    stats["Apex Spins (Bonus 2) RTP"] = $"{apexSpinsRtp:P2}";
    stats["Colossal Spins (Bonus 3) RTP"] = $"{colossalSpinsRtp:P2}";
    stats["Primal Zone (Bonus 4) RTP"] = $"{primalZoneRtp:P2}";
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

        double avgFeatureWinMultiplier = collectionTriggerSpins > 0 ? (double)totalFeatureWin / (collectionTriggerSpins * 100.0) : 0.0;
        double avgCollectedCashMultiplier = collectionTriggerSpins > 0 ? (double)totalCollectCashMultiplierSum / collectionTriggerSpins : 0.0;
        double avgCollectedFireCores = collectionTriggerSpins > 0 ? (double)totalCollectFireCoresCount / collectionTriggerSpins : 0.0;

        // SECTION 2: Collector Feature
        stats["Collector Feature RTP"] = $"{collectFeatureRtp:P2}";
        stats["Collector Feature Collection Trigger Freq"] = collectionTriggerFreqStr;
        stats["Collector Feature Average Pay when Triggered"] = $"{avgFeatureWinMultiplier:F2}x bet";
        stats["Collector Feature Total Collection Triggers"] = collectionTriggerSpins.ToString("N0");
        stats["Collector Feature Single Collector (1x) Triggers"] = $"{collectTriggersWith1Collector:N0} ({(collectionTriggerSpins > 0 ? (double)collectTriggersWith1Collector/collectionTriggerSpins : 0.0):P2})";
        stats["Collector Feature Double Collector (2x) Triggers"] = $"{collectTriggersWith2Collectors:N0} ({(collectionTriggerSpins > 0 ? (double)collectTriggersWith2Collectors/collectionTriggerSpins : 0.0):P2})";
        stats["Collector Feature Avg Cash Value Sum Collected"] = $"{avgCollectedCashMultiplier:F2}x bet";
        stats["Collector Feature Avg Fire Cores Collected"] = $"{avgCollectedFireCores:F2}";
        stats["Collector Feature Waste Spins (Collector, no Fire Core)"] = $"{spinsWithCollectorButNoFireCore:N0} ({(double)spinsWithCollectorButNoFireCore/totalSpins:P2})";
        stats["Collector Feature Uncollected Spins (Fire Core, no Collector)"] = $"{spinsWithFireCoreButNoCollector:N0} ({(double)spinsWithFireCoreButNoCollector/totalSpins:P2})";
        stats["Collector Feature Landing Collector Freq (Reel 0 or 4)"] = collectorLandingFreqStr;
        stats["Collector Feature Landing Fire Core Freq"] = fireCoreLandingFreqStr;

        double jackpotBonusTriggerChance = (double)totalJackpotBonusTriggers / totalSpins;
        string jackpotBonusTriggerFreqStr = jackpotBonusTriggerChance > 0 ? $"1 in {1.0 / jackpotBonusTriggerChance:F1} spins ({jackpotBonusTriggerChance:P4})" : "Never";
        double avgJackpotBonusWinMultiplier = totalJackpotBonusTriggers > 0 ? (double)totalJackpotBonusWin / (totalJackpotBonusTriggers * 100.0) : 0.0;
        double avgFireCoresOnJackpotTrigger = totalJackpotBonusTriggers > 0 ? (double)totalFireCoresOnJackpotTrigger / totalJackpotBonusTriggers : 0.0;

        // SECTION 3: Jackpot Bonus
        stats["Jackpot Bonus RTP"] = $"{jackpotBonusRtp:P2}";
        stats["Jackpot Bonus Trigger Freq"] = jackpotBonusTriggerFreqStr;
        stats["Jackpot Bonus Average Pay when Triggered"] = $"{avgJackpotBonusWinMultiplier:F2}x bet";
        stats["Jackpot Bonus Avg Fire Cores on Screen when Triggered"] = $"{avgFireCoresOnJackpotTrigger:F2}";
        foreach (var jpName in config.JackpotNames)
        {
            int hits = jackpotHits[jpName];
            double winChanceInBonus = totalJackpotBonusTriggers > 0 ? (double)hits / totalJackpotBonusTriggers : 0.0;
            double jpRtp = (double)jackpotWins[jpName] / (totalSpins * 100.0);
            string jpFreqInBonus = winChanceInBonus > 0 ? $"1 in {1.0 / winChanceInBonus:F1} triggers" : "Never";
            stats[$"Jackpot Winner - {jpName}"] = $"Hits: {hits:N0} | RTP: {jpRtp:P4} | Chance in Bonus: {winChanceInBonus:P2} ({jpFreqInBonus})";
        }
        for (int c = 1; c < jackpotTriggersByFireCoreCount.Length; c++)
        {
            int hits = jackpotTriggersByFireCoreCount[c];
            if (hits > 0)
            {
                double pctOfTriggers = totalJackpotBonusTriggers > 0 ? (double)hits / totalJackpotBonusTriggers : 0.0;
                stats[$"Jackpot Bonus Landed {c} Fire Cores Hits"] = $"{hits:N0} ({pctOfTriggers:P2} of triggers)";
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
        stats["Bonus 1 Average Lock & Slingo Win"] = $"{avgLockSlingoWinMultiplier:F2}x bet";
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
            stats[$"Bonus 1 Power {L} ({config.LockSlingoSpins[L]} spins) Hits"] = $"{hits:N0} ({pctOfTriggers:P2} of total triggers | {freqStr})";
        }

        // SECTION 5: Bonus 2 (Apex Spins)
        double landingChance2 = (double)spinsWithPotTrigger[1] / totalSpins;
        string landingFreqStr2 = landingChance2 > 0 ? $"1 in {1.0 / landingChance2:F1} spins ({landingChance2:P2})" : "Never";
        double avgPower2 = totalPotTriggers[1] > 0 ? (double)totalPotTriggerPowers[1] / totalPotTriggers[1] : 0.0;
        double apexSpinsMinWinPercent = totalApexSpinsTriggers > 0 ? (double)totalApexSpinsMinWinApplied / totalApexSpinsTriggers : 0.0;

        stats["Bonus 2 Landing Pot Trigger Freq"] = landingFreqStr2;
        stats["Bonus 2 Trigger Frequency"] = apexSpinsTriggerFreqStr;
        stats["Bonus 2 Average Power on Trigger"] = $"{avgPower2:F2}";
        stats["Bonus 2 Average Win"] = $"{avgApexSpinsWinMultiplier:F2}x bet";
        stats["Bonus 2 Average Spins Played"] = $"{avgApexSpinsSpinsPlayed:F2} spins";
        stats["Bonus 2 Guaranteed Minimum Applied %"] = $"{apexSpinsMinWinPercent:P2}";

        for (int L = 0; L < apexSpinsTriggersByPower.Length; L++)
        {
            int hits = apexSpinsTriggersByPower[L];
            double pctOfTriggers = totalApexSpinsTriggers > 0 ? (double)hits / totalApexSpinsTriggers : 0.0;
            double hitRate = (double)hits / totalSpins;
            string freqStr = hits > 0 ? $"1 in {(1.0 / hitRate):N1} spins" : "Never";
            double avgWin = hits > 0 ? (double)apexSpinsWinByPower[L] / (hits * 100.0) : 0.0;
            double avgSpins = hits > 0 ? (double)apexSpinsPlayedByPower[L] / hits : 0.0;

            stats[$"Bonus 2 Power {L} (Top Award {config.ApexSpinsTopAwardMultipliers[L]}x) Hits"] = $"{hits:N0} ({pctOfTriggers:P2} of triggers)";
            stats[$"Bonus 2 Power {L} Trigger Chance (% of spins)"] = hitRate > 0 ? $"{hitRate:P4} ({freqStr})" : "Never";
            stats[$"Bonus 2 Power {L} Avg Win"] = $"{avgWin:F2}x bet";
            stats[$"Bonus 2 Power {L} Avg Spins Played"] = $"{avgSpins:F2} spins";
        }

        // SECTION 6: Bonus 3 (Colossal Spins)
        double landingChance3 = (double)spinsWithPotTrigger[2] / totalSpins;
        string landingFreqStr3 = landingChance3 > 0 ? $"1 in {1.0 / landingChance3:F1} spins ({landingChance3:P2})" : "Never";
        double avgPower3 = totalPotTriggers[2] > 0 ? (double)totalPotTriggerPowers[2] / totalPotTriggers[2] : 0.0;
        double colossalSpinsMinWinPercent = totalColossalSpinsTriggers > 0 ? (double)totalColossalSpinsMinWinApplied / totalColossalSpinsTriggers : 0.0;

        stats["Bonus 3 Landing Pot Trigger Freq"] = landingFreqStr3;
        stats["Bonus 3 Trigger Frequency"] = colossalSpinsTriggerFreqStr;
        stats["Bonus 3 Average Power on Trigger"] = $"{avgPower3:F2}";
        stats["Bonus 3 Average Win"] = $"{avgColossalSpinsWinMultiplier:F2}x bet";
        stats["Bonus 3 Average Spins Awarded"] = $"{avgColossalSpinsSpinsPlayed:F2} spins";
        stats["Bonus 3 Guaranteed Minimum Applied %"] = $"{colossalSpinsMinWinPercent:P2}";

        for (int L = 0; L < colossalSpinsTriggersByPower.Length; L++)
        {
            int hits = colossalSpinsTriggersByPower[L];
            double pctOfTriggers = totalColossalSpinsTriggers > 0 ? (double)hits / totalColossalSpinsTriggers : 0.0;
            double hitRate = (double)hits / totalSpins;
            string freqStr = hits > 0 ? $"1 in {(1.0 / hitRate):N1} spins" : "Never";
            double avgWin = hits > 0 ? (double)colossalSpinsWinByPower[L] / (hits * 100.0) : 0.0;
            double avgSpins = hits > 0 ? (double)colossalSpinsPlayedByPower[L] / hits : 0.0;

            stats[$"Bonus 3 Power {L} ({config.ColossalSpinsCounts[L]} spins) Hits"] = $"{hits:N0} ({pctOfTriggers:P2} of triggers)";
            stats[$"Bonus 3 Power {L} Trigger Chance (% of spins)"] = hitRate > 0 ? $"{hitRate:P4} ({freqStr})" : "Never";
            stats[$"Bonus 3 Power {L} Avg Win"] = $"{avgWin:F2}x bet";
            stats[$"Bonus 3 Power {L} Avg Spins"] = $"{avgSpins:F2} spins";
        }

        stats["Bonus 3 Colossal Symbols Breakdown"] = "---";
        var sortedColossalSyms = colossalSymbolHits.Keys.OrderBy(id => id).ToList();
        foreach (var symId in sortedColossalSyms)
        {
            long hits = colossalSymbolHits[symId];
            long wins = colossalSymbolWins[symId];
            double symRtp = (double)wins / (totalSpins * 100.0);
            double avgWin = hits > 0 ? (double)wins / (hits * 100.0) : 0.0;
            string symName = config.Symbols.FirstOrDefault(s => s.Id == symId)?.Name ?? $"Symbol {symId}";

            stats[$"Colossal Symbol {symId} ({symName}) Landed Spins"] = $"{hits:N0} ({(totalColossalSpinsPlayed > 0 ? (double)hits / totalColossalSpinsPlayed : 0.0):P2} of colossal spins)";
            stats[$"Colossal Symbol {symId} ({symName}) RTP"] = $"{symRtp:P4}";
            stats[$"Colossal Symbol {symId} ({symName}) Avg Win"] = $"{avgWin:F2}x bet";
        }

        // SECTION 7: Bonus 4 (Primal Zone Bonus)
        double landingChance4 = (double)spinsWithPotTrigger[3] / totalSpins;
        string landingFreqStr4 = landingChance4 > 0 ? $"1 in {1.0 / landingChance4:F1} spins ({landingChance4:P2})" : "Never";
        double avgPower4 = totalPotTriggers[3] > 0 ? (double)totalPotTriggerPowers[3] / totalPotTriggers[3] : 0.0;

        stats["Bonus 4 Landing Pot Trigger Freq"] = landingFreqStr4;
        stats["Bonus 4 Trigger Frequency"] = primalZoneTriggerFreqStr;
        stats["Bonus 4 Average Power on Trigger"] = $"{avgPower4:F2}";
        stats["Bonus 4 Average Win"] = $"{avgPrimalZoneWinMultiplier:F2}x bet";
        stats["Bonus 4 Average Spins Awarded"] = $"{avgPrimalZoneSpinsPlayed:F2} spins";
        stats["Bonus 4 Average Bananas Collected"] = $"{avgPrimalZoneBananas:F2}";
        stats["Bonus 4 Guaranteed Minimum Applied %"] = $"{primalZoneMinWinPercent:P2}";

        for (int L = 0; L < primalZoneTriggersByPower.Length; L++)
        {
            int hits = primalZoneTriggersByPower[L];
            double pctOfTriggers = totalPrimalZoneTriggers > 0 ? (double)hits / totalPrimalZoneTriggers : 0.0;
            double hitRate = (double)hits / totalSpins;
            string freqStr = hits > 0 ? $"1 in {(1.0 / hitRate):N1} spins" : "Never";
            double avgWin = hits > 0 ? (double)primalZoneWinByPower[L] / (hits * 100.0) : 0.0;
            double avgSpins = hits > 0 ? (double)primalZonePlayedByPower[L] / hits : 0.0;

            stats[$"Bonus 4 Power {L} ({config.PrimalZoneSpins[L]} spins) Hits"] = $"{hits:N0} ({pctOfTriggers:P2} of triggers)";
            stats[$"Bonus 4 Power {L} Trigger Chance (% of spins)"] = hitRate > 0 ? $"{hitRate:P4} ({freqStr})" : "Never";
            stats[$"Bonus 4 Power {L} Avg Win"] = $"{avgWin:F2}x bet";
            stats[$"Bonus 4 Power {L} Avg Spins"] = $"{avgSpins:F2} spins";
        }

        // Console Prints following the exact structured hierarchy:
        Console.WriteLine($"Simulation complete!");
        Console.WriteLine($"  - Total RTP: {totalRtp:P2}");
        Console.WriteLine($"    - Line Payout RTP: {lineWinRtp:P2}");
        Console.WriteLine($"    - Collect Feature RTP: {collectFeatureRtp:P2}");
        Console.WriteLine($"    - Jackpot Bonus RTP: {jackpotBonusRtp:P2}");
        Console.WriteLine($"    - Lock & Slingo (Bonus 1) RTP: {lockSlingoRtp:P2}");
        Console.WriteLine($"    - Apex Spins (Bonus 2) RTP: {apexSpinsRtp:P2}");
        Console.WriteLine($"    - Colossal Spins (Bonus 3) RTP: {colossalSpinsRtp:P2}");
        Console.WriteLine($"    - Primal Zone (Bonus 4) RTP: {primalZoneRtp:P2}");
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

        Console.WriteLine("\n[Bonus 2 - Apex Spins]");
        Console.WriteLine($"  - Apex Spins Total RTP: {apexSpinsRtp:P2}");
        Console.WriteLine($"  - Landing Pot Trigger Freq: {landingFreqStr2}");
        Console.WriteLine($"  - Trigger Frequency: {apexSpinsTriggerFreqStr}");
        Console.WriteLine($"  - Average Power on Trigger: {avgPower2:F2}");
        Console.WriteLine($"  - Average Win: {avgApexSpinsWinMultiplier:F2}x bet");
        Console.WriteLine($"  - Average Spins Played: {avgApexSpinsSpinsPlayed:F2} spins");
        Console.WriteLine($"  - Guaranteed Minimum Win Applied %: {apexSpinsMinWinPercent:P2}");
        Console.WriteLine("  - Hit Distribution & Stats by Power Level:");
        for (int L = 0; L < apexSpinsTriggersByPower.Length; L++)
        {
            int hits = apexSpinsTriggersByPower[L];
            double pctOfTriggers = totalApexSpinsTriggers > 0 ? (double)hits / totalApexSpinsTriggers : 0.0;
            double hitRate = (double)hits / totalSpins;
            string freqStr = hits > 0 ? $"1 in {(1.0 / hitRate):N1} spins" : "Never";
            double avgWin = hits > 0 ? (double)apexSpinsWinByPower[L] / (hits * 100.0) : 0.0;
            double avgSpins = hits > 0 ? (double)apexSpinsPlayedByPower[L] / hits : 0.0;

            Console.WriteLine($"    Power Level {L,2} (Top Award {config.ApexSpinsTopAwardMultipliers[L],2}x): Hits = {hits,6:N0} | {pctOfTriggers,6:P2} of triggers ({freqStr}) | Avg Win = {avgWin,6:F2}x bet | Avg Spins = {avgSpins,5:F2}");
        }

        Console.WriteLine("\n[Bonus 3 - Colossal Spins]");
        Console.WriteLine($"  - Colossal Spins Total RTP: {colossalSpinsRtp:P2}");
        Console.WriteLine($"  - Landing Pot Trigger Freq: {landingFreqStr3}");
        Console.WriteLine($"  - Trigger Frequency: {colossalSpinsTriggerFreqStr}");
        Console.WriteLine($"  - Average Power on Trigger: {avgPower3:F2}");
        Console.WriteLine($"  - Average Win: {avgColossalSpinsWinMultiplier:F2}x bet");
        Console.WriteLine($"  - Average Spins Awarded: {avgColossalSpinsSpinsPlayed:F2} spins");
        Console.WriteLine($"  - Guaranteed Minimum Win Applied %: {colossalSpinsMinWinPercent:P2}");
        Console.WriteLine("  - Hit Distribution & Stats by Power Level:");
        for (int L = 0; L < colossalSpinsTriggersByPower.Length; L++)
        {
            int hits = colossalSpinsTriggersByPower[L];
            double pctOfTriggers = totalColossalSpinsTriggers > 0 ? (double)hits / totalColossalSpinsTriggers : 0.0;
            double hitRate = (double)hits / totalSpins;
            string freqStr = hits > 0 ? $"1 in {(1.0 / hitRate):N1} spins" : "Never";
            double avgWin = hits > 0 ? (double)colossalSpinsWinByPower[L] / (hits * 100.0) : 0.0;
            double avgSpins = hits > 0 ? (double)colossalSpinsPlayedByPower[L] / hits : 0.0;

            Console.WriteLine($"    Power Level {L,2} ({config.ColossalSpinsCounts[L],2} spins): Hits = {hits,6:N0} | {pctOfTriggers,6:P2} of triggers ({freqStr}) | Avg Win = {avgWin,6:F2}x bet | Avg Spins = {avgSpins,5:F2}");
        }

        Console.WriteLine("\n[Bonus 4 - Primal Zone Bonus]");
        Console.WriteLine($"  - Primal Zone Bonus Total RTP: {primalZoneRtp:P2}");
        Console.WriteLine($"  - Landing Pot Trigger Freq: {landingFreqStr4}");
        Console.WriteLine($"  - Trigger Frequency: {primalZoneTriggerFreqStr}");
        Console.WriteLine($"  - Average Power on Trigger: {avgPower4:F2}");
        Console.WriteLine($"  - Average Win: {avgPrimalZoneWinMultiplier:F2}x bet");
        Console.WriteLine($"  - Average Spins Awarded: {avgPrimalZoneSpinsPlayed:F2} spins");
        Console.WriteLine($"  - Average Bananas Collected: {avgPrimalZoneBananas:F2}");
        Console.WriteLine($"  - Guaranteed Minimum Win Applied %: {primalZoneMinWinPercent:P2}");
        Console.WriteLine("  - Hit Distribution & Stats by Power Level:");
        for (int L = 0; L < primalZoneTriggersByPower.Length; L++)
        {
            int hits = primalZoneTriggersByPower[L];
            double pctOfTriggers = totalPrimalZoneTriggers > 0 ? (double)hits / totalPrimalZoneTriggers : 0.0;
            double hitRate = (double)hits / totalSpins;
            string freqStr = hits > 0 ? $"1 in {(1.0 / hitRate):N1} spins" : "Never";
            double avgWin = hits > 0 ? (double)primalZoneWinByPower[L] / (hits * 100.0) : 0.0;
            double avgSpins = hits > 0 ? (double)primalZonePlayedByPower[L] / hits : 0.0;

            Console.WriteLine($"    Power Level {L,2} ({config.PrimalZoneSpins[L],2} spins): Hits = {hits,6:N0} | {pctOfTriggers,6:P2} of triggers ({freqStr}) | Avg Win = {avgWin,6:F2}x bet | Avg Spins = {avgSpins,5:F2}");
        }

        // SECTION 8: Stampede Spin Feature
        double stampedeSpinChance = (double)totalStampedeSpins / totalSpins;
        string stampedeFreqStr = stampedeSpinChance > 0 ? $"1 in {1.0 / stampedeSpinChance:F1} spins ({stampedeSpinChance:P2})" : "Never";
        double avgStampedeWinMultiplier = totalStampedeSpins > 0 ? (double)totalStampedeWin / (totalStampedeSpins * 100.0) : 0.0;
        double avgStampedePotsAdded = totalStampedeSpins > 0 ? (double)totalStampedePotsAdded / totalStampedeSpins : 0.0;

        stats["Stampede Spin Trigger Freq"] = stampedeFreqStr;
        stats["Stampede Spin Avg Win"] = $"{avgStampedeWinMultiplier:F2}x bet";
        stats["Stampede Spin Avg Pots Added"] = $"{avgStampedePotsAdded:F2}";

        Console.WriteLine("\n[Stampede Spin Feature]");
        Console.WriteLine($"  - Stampede Spin Trigger Freq: {stampedeFreqStr}");
        Console.WriteLine($"  - Total Stampede Spins: {totalStampedeSpins:N0}");
        Console.WriteLine($"  - Average Pay when Stampede Spin Triggers: {avgStampedeWinMultiplier:F2}x bet");
        Console.WriteLine($"  - Avg Pot Triggers Added per Stampede Spin: {avgStampedePotsAdded:F2}");

        Console.WriteLine($"\nStage 6 Power Max Triggers Count: {totalPowerUpTriggers}");
    }
    else
    {
        double landingChance3 = (double)spinsWithPotTrigger[2] / totalSpins;
        string landingFreqStr3 = landingChance3 > 0 ? $"1 in {1.0 / landingChance3:F1} spins ({landingChance3:P2})" : "Never";
        double avgPower3 = totalPotTriggers[2] > 0 ? (double)totalPotTriggerPowers[2] / totalPotTriggers[2] : 0.0;

        stats["Bonus 3 Landing Pot Trigger Freq"] = landingFreqStr3;
        stats["Bonus 3 Trigger Frequency"] = colossalSpinsTriggerFreqStr;
        stats["Bonus 3 Average Power on Trigger"] = $"{avgPower3:F2}";
        stats["Bonus 3 Average Win"] = $"{avgColossalSpinsWinMultiplier:F2}x bet";
        stats["Bonus 3 Average Spins Awarded"] = $"{avgColossalSpinsSpinsPlayed:F2} spins";

        Console.WriteLine($"Simulation complete!");
        Console.WriteLine($"  - Total RTP: {totalRtp:P2}");
        Console.WriteLine($"    - Line Payout RTP: {lineWinRtp:P2}");
        Console.WriteLine($"    - Collect Feature RTP: {collectFeatureRtp:P2}");
        Console.WriteLine($"    - Jackpot Bonus RTP: {jackpotBonusRtp:P2}");
        Console.WriteLine($"    - Lock & Slingo (Bonus 1) RTP: {lockSlingoRtp:P2}");
        Console.WriteLine($"    - Apex Spins (Bonus 2) RTP: {apexSpinsRtp:P2}");
        Console.WriteLine($"    - Colossal Spins (Bonus 3) RTP: {colossalSpinsRtp:P2}");
        Console.WriteLine($"    - Bonus 4 RTP: 0.00% (Placeholder)");
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

        Console.WriteLine("\n[Bonus 2 - Apex Spins]");
        Console.WriteLine($"  - Apex Spins Trigger Freq: {apexSpinsTriggerFreqStr}");
        Console.WriteLine($"  - Apex Spins Total RTP: {apexSpinsRtp:P2}");
        Console.WriteLine($"  - Average Win: {avgApexSpinsWinMultiplier:F2}x bet");
        Console.WriteLine($"  - Average Spins Played: {avgApexSpinsSpinsPlayed:F2} spins");

        Console.WriteLine("\n[Bonus 3 - Colossal Spins]");
        Console.WriteLine($"  - Colossal Spins Trigger Freq: {colossalSpinsTriggerFreqStr}");
        Console.WriteLine($"  - Colossal Spins Total RTP: {colossalSpinsRtp:P2}");
        Console.WriteLine($"  - Average Win: {avgColossalSpinsWinMultiplier:F2}x bet");
        Console.WriteLine($"  - Average Spins Awarded: {avgColossalSpinsSpinsPlayed:F2} spins");

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

class SimWorkerStats
{
    public long TotalWin;
    public long TotalLineWin;
    public long TotalFeatureWin;
    public int WinSpins;
    public int TotalPowerUpTriggers;

    public int SpinsWithCollectorOnReel0Or4;
    public int SpinsWithFireCore;
    public int CollectionTriggerSpins;
    public int CollectTriggersWith1Collector;
    public int CollectTriggersWith2Collectors;
    public double TotalCollectCashMultiplierSum;
    public long TotalCollectFireCoresCount;
    public int SpinsWithCollectorButNoFireCore;
    public int SpinsWithFireCoreButNoCollector;

    public int TotalJackpotBonusTriggers;
    public long TotalJackpotBonusWin;
    public long TotalFireCoresOnJackpotTrigger;
    public int[] JackpotTriggersByFireCoreCount = new int[16];
    public Dictionary<string, int> JackpotHits = new();
    public Dictionary<string, long> JackpotWins = new();

    public int[] SpinsWithPotTrigger = new int[4];
    public int[] TotalPotTriggers = new int[4];
    public long[] TotalPotTriggerPowers = new long[4];

    public int TotalLockSlingoTriggers;
    public long TotalLockSlingoWin;
    public long TotalLockSlingoSlingosCompleted;
    public double TotalLockSlingoCashSum;
    public double TotalLockSlingoLadderSum;
    public int TotalLockSlingoMinWinApplied;
    public long TotalLockSlingoSpinsAwarded;
    public int[] LockSlingoTriggersByPower;

    public int TotalApexSpinsTriggers;
    public long TotalApexSpinsWin;
    public long TotalApexSpinsPlayed;
    public int TotalApexSpinsMinWinApplied;
    public int[] ApexSpinsTriggersByPower;
    public long[] ApexSpinsWinByPower;
    public long[] ApexSpinsPlayedByPower;

    public int TotalColossalSpinsTriggers;
    public long TotalColossalSpinsWin;
    public long TotalColossalSpinsPlayed;
    public int TotalColossalSpinsMinWinApplied;
    public int[] ColossalSpinsTriggersByPower;
    public long[] ColossalSpinsWinByPower;
    public long[] ColossalSpinsPlayedByPower;
    public Dictionary<int, long> ColossalSymbolHits = new();
    public Dictionary<int, long> ColossalSymbolWins = new();

    public int TotalPrimalZoneTriggers;
    public long TotalPrimalZoneWin;
    public long TotalPrimalZonePlayed;
    public long TotalPrimalZoneBananas;
    public int TotalPrimalZoneMinWinApplied;
    public int[] PrimalZoneTriggersByPower;
    public long[] PrimalZoneWinByPower;
    public long[] PrimalZonePlayedByPower;
    public int[] PrimalZoneStageHits = new int[4];

    public int TotalStampedeSpins;
    public long TotalStampedeWin;
    public long TotalStampedePotsAdded;

    public SimWorkerStats(PrimalConfig config)
    {
        foreach (var jp in config.JackpotNames)
        {
            JackpotHits[jp] = 0;
            JackpotWins[jp] = 0;
        }

        LockSlingoTriggersByPower = new int[config.LockSlingoSpins.Length];
        ApexSpinsTriggersByPower = new int[config.ApexSpinsTopAwardMultipliers.Length];
        ApexSpinsWinByPower = new long[config.ApexSpinsTopAwardMultipliers.Length];
        ApexSpinsPlayedByPower = new long[config.ApexSpinsTopAwardMultipliers.Length];

        ColossalSpinsTriggersByPower = new int[config.ColossalSpinsCounts.Length];
        ColossalSpinsWinByPower = new long[config.ColossalSpinsCounts.Length];
        ColossalSpinsPlayedByPower = new long[config.ColossalSpinsCounts.Length];

        PrimalZoneTriggersByPower = new int[config.PrimalZoneSpins.Length];
        PrimalZoneWinByPower = new long[config.PrimalZoneSpins.Length];
        PrimalZonePlayedByPower = new long[config.PrimalZoneSpins.Length];
    }

    public void Record(SpinResult spinResult, PrimalConfig config, bool trackFullStats)
    {
        TotalWin += spinResult.TotalWin;
        TotalFeatureWin += spinResult.FeatureWin;
        TotalLineWin += (spinResult.TotalWin - spinResult.FeatureWin);

        if (spinResult.IsStampedeSpin)
        {
            TotalStampedeSpins++;
            TotalStampedeWin += spinResult.TotalWin;
            TotalStampedePotsAdded += spinResult.StampedeAddedPotCount;
        }

        if (spinResult.TotalWin > 0)
        {
            WinSpins++;
        }

        if (spinResult.SetRandomBonusPowerToMax)
        {
            TotalPowerUpTriggers++;
        }

        if (trackFullStats)
        {
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

            if (collectorCount > 0) SpinsWithCollectorOnReel0Or4++;
            if (fireCoreCount > 0) SpinsWithFireCore++;
            if (collectorCount > 0 && fireCoreCount == 0) SpinsWithCollectorButNoFireCore++;
            if (fireCoreCount > 0 && collectorCount == 0) SpinsWithFireCoreButNoCollector++;

            if (spinResult.CollectorTriggered)
            {
                CollectionTriggerSpins++;
                if (spinResult.CollectorCount == 1) CollectTriggersWith1Collector++;
                else if (spinResult.CollectorCount == 2) CollectTriggersWith2Collectors++;

                TotalCollectCashMultiplierSum += spinResult.TotalCollectedMultiplier;
                TotalCollectFireCoresCount += fireCoreCount;
            }

            if (spinResult.JackpotBonusTriggered)
            {
                TotalJackpotBonusTriggers++;
                TotalJackpotBonusWin += spinResult.JackpotBonusWin;
                TotalFireCoresOnJackpotTrigger += fireCoreCount;
                if (fireCoreCount >= 0 && fireCoreCount < JackpotTriggersByFireCoreCount.Length)
                {
                    JackpotTriggersByFireCoreCount[fireCoreCount]++;
                }
                if (JackpotHits.ContainsKey(spinResult.WonJackpotName))
                {
                    JackpotHits[spinResult.WonJackpotName]++;
                    JackpotWins[spinResult.WonJackpotName] += spinResult.JackpotBonusWin;
                }
            }

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
                    SpinsWithPotTrigger[p]++;
                }
            }

            foreach (var potBonus in spinResult.TriggeredPotBonuses)
            {
                int p = potBonus.PotIndex;
                TotalPotTriggers[p]++;
                TotalPotTriggerPowers[p] += potBonus.Power;

                if (p == 0)
                {
                    TotalLockSlingoTriggers++;
                    TotalLockSlingoWin += potBonus.Win;
                    TotalLockSlingoSlingosCompleted += potBonus.CompletedSlingos;
                    TotalLockSlingoCashSum += potBonus.CashValuesSum;
                    TotalLockSlingoLadderSum += potBonus.LadderPrize;
                    TotalLockSlingoSpinsAwarded += config.LockSlingoSpins[potBonus.Power];
                    if (potBonus.MinWinApplied) TotalLockSlingoMinWinApplied++;
                    if (potBonus.Power >= 0 && potBonus.Power < LockSlingoTriggersByPower.Length)
                    {
                        LockSlingoTriggersByPower[potBonus.Power]++;
                    }
                }
                else if (p == 1)
                {
                    TotalApexSpinsTriggers++;
                    TotalApexSpinsWin += potBonus.Win;
                    TotalApexSpinsPlayed += potBonus.SpinsPlayed;
                    if (potBonus.MinWinApplied) TotalApexSpinsMinWinApplied++;
                    if (potBonus.Power >= 0 && potBonus.Power < ApexSpinsTriggersByPower.Length)
                    {
                        ApexSpinsTriggersByPower[potBonus.Power]++;
                        ApexSpinsWinByPower[potBonus.Power] += potBonus.Win;
                        ApexSpinsPlayedByPower[potBonus.Power] += potBonus.SpinsPlayed;
                    }
                }
                else if (p == 2)
                {
                    TotalColossalSpinsTriggers++;
                    TotalColossalSpinsWin += potBonus.Win;
                    TotalColossalSpinsPlayed += potBonus.SpinsPlayed;
                    if (potBonus.MinWinApplied) TotalColossalSpinsMinWinApplied++;
                    if (potBonus.Power >= 0 && potBonus.Power < ColossalSpinsTriggersByPower.Length)
                    {
                        ColossalSpinsTriggersByPower[potBonus.Power]++;
                        ColossalSpinsWinByPower[potBonus.Power] += potBonus.Win;
                        ColossalSpinsPlayedByPower[potBonus.Power] += potBonus.SpinsPlayed;
                    }
                    foreach (var kvp in potBonus.ColossalSymbolHits)
                    {
                        if (!ColossalSymbolHits.ContainsKey(kvp.Key))
                        {
                            ColossalSymbolHits[kvp.Key] = 0;
                            ColossalSymbolWins[kvp.Key] = 0;
                        }
                        ColossalSymbolHits[kvp.Key] += kvp.Value;
                        if (potBonus.ColossalSymbolWins.TryGetValue(kvp.Key, out long win))
                        {
                            ColossalSymbolWins[kvp.Key] += win;
                        }
                    }
                }
                else if (p == 3)
                {
                    TotalPrimalZoneTriggers++;
                    TotalPrimalZoneWin += potBonus.Win;
                    TotalPrimalZonePlayed += potBonus.SpinsPlayed;
                    TotalPrimalZoneBananas += potBonus.BananasCollected;
                    if (potBonus.MinWinApplied) TotalPrimalZoneMinWinApplied++;
                    if (potBonus.Power >= 0 && potBonus.Power < PrimalZoneTriggersByPower.Length)
                    {
                        PrimalZoneTriggersByPower[potBonus.Power]++;
                        PrimalZoneWinByPower[potBonus.Power] += potBonus.Win;
                        PrimalZonePlayedByPower[potBonus.Power] += potBonus.SpinsPlayed;
                    }
                    if (potBonus.FinalPrimalZoneStage >= 0 && potBonus.FinalPrimalZoneStage < 4)
                    {
                        PrimalZoneStageHits[potBonus.FinalPrimalZoneStage]++;
                    }
                }
            }
        }
        else
        {
            if (spinResult.JackpotBonusTriggered)
            {
                TotalJackpotBonusWin += spinResult.JackpotBonusWin;
            }

            foreach (var potBonus in spinResult.TriggeredPotBonuses)
            {
                if (potBonus.PotIndex == 0)
                {
                    TotalLockSlingoTriggers++;
                    TotalLockSlingoWin += potBonus.Win;
                    TotalLockSlingoSpinsAwarded += config.LockSlingoSpins[potBonus.Power];
                }
                else if (potBonus.PotIndex == 1)
                {
                    TotalApexSpinsTriggers++;
                    TotalApexSpinsWin += potBonus.Win;
                    TotalApexSpinsPlayed += potBonus.SpinsPlayed;
                }
                else if (potBonus.PotIndex == 2)
                {
                    TotalColossalSpinsTriggers++;
                    TotalColossalSpinsWin += potBonus.Win;
                    TotalColossalSpinsPlayed += potBonus.SpinsPlayed;
                }
                else if (potBonus.PotIndex == 3)
                {
                    TotalPrimalZoneTriggers++;
                    TotalPrimalZoneWin += potBonus.Win;
                    TotalPrimalZonePlayed += potBonus.SpinsPlayed;
                }
            }
        }
    }
}

