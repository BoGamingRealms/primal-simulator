using System;
using System.Collections.Generic;
using SlotFramework.Models;

namespace PrimalGame.Config;

public class PrimalConfig
{
    public List<Symbol> Symbols { get; set; } = new();
    public Paytable Paytable { get; set; } = new();
    public ReelSet BaseReels { get; set; } = new();
    public ReelSet FreeReels { get; set; } = new();
    public Dictionary<string, ReelSet> Reelsets { get; set; } = new();
    public int[][] Paylines { get; set; } = Array.Empty<int[]>();
    public long[,] FastPaytableMatrix { get; set; } = new long[16, 6];
    
    // Core parameters
    public int WildSymbolId { get; set; }
    public int ScatterSymbolId { get; set; }
    
    // Feature parameters
    public int MinScattersForFreeSpins { get; set; } = 3;
    public Dictionary<int, int> ScatterFreeSpinsCount { get; set; } = new()
    {
        { 3, 10 },
        { 4, 15 },
        { 5, 20 }
    };
    public int FreeSpinsMultiplier { get; set; } = 2;

    public Dictionary<string, int[]> BaseGameStageWeights { get; set; } = new();

    public int[] StageSpinsToNext { get; set; } = Array.Empty<int>();

    // Fire Core cash value feature configuration
    public double[] FireCoreCashValuesSpecial { get; set; } = Array.Empty<double>(); // Col B (for Reelsets 8, 9, 10 when Collector is present)
    public int[] FireCoreCashWeightsSpecial { get; set; } = Array.Empty<int>();     // Col B (for Reelsets 8, 9, 10 when Collector is present)
    public double[] FireCoreCashValuesDefault { get; set; } = Array.Empty<double>(); // Col C (for all other base game reelsets)
    public int[] FireCoreCashWeightsDefault { get; set; } = Array.Empty<int>();     // Col C (for all other base game reelsets)

    public double[] FireCoreCashValues
    {
        get => FireCoreCashValuesDefault.Length > 0 ? FireCoreCashValuesDefault : FireCoreCashValuesSpecial;
        set => FireCoreCashValuesSpecial = value;
    }
    public int[] FireCoreCashWeights
    {
        get => FireCoreCashWeightsDefault.Length > 0 ? FireCoreCashWeightsDefault : FireCoreCashWeightsSpecial;
        set => FireCoreCashWeightsSpecial = value;
    }

    public int FireCoreSymbolId { get; set; } = 14;
    public int CollectorSymbolId { get; set; } = 9;

    // Jackpot Bonus configuration
    public int JackpotBonusTriggerChanceWeight { get; set; } = 2000;
    public string[] JackpotNames { get; set; } = Array.Empty<string>();
    public double[] JackpotValues { get; set; } = Array.Empty<double>();
    public int[] JackpotWeights { get; set; } = Array.Empty<int>();

    // Pot Bonus configuration
    public int[] PotTriggerChanceWeights { get; set; } = new int[] { 1000, 1000, 1000, 1000 };
    public int MaxBonusPower { get; set; } = 100;

    // Lock & Slingo (Bonus 1) configuration
    public int[] LockSlingoSpins { get; set; } = Array.Empty<int>();
    public int[] LockSlingoTriggerWeights { get; set; } = Array.Empty<int>();
    public double[] LockSlingoBonusMinimums { get; set; } = Array.Empty<double>();
    public int[] LockSlingoLadderLines { get; set; } = Array.Empty<int>();
    public double[] LockSlingoLadderPrizes { get; set; } = Array.Empty<double>();
    public double[] LockSlingoFireCoreValues { get; set; } = Array.Empty<double>();
    public int[] LockSlingoFireCoreWeights { get; set; } = Array.Empty<int>();
    public List<PotLandingWeight> LockSlingoLandingChanceWeights { get; set; } = new();

    // Apex Spins (Bonus 2) configuration
    public double[] ApexSpinsTopAwardMultipliers { get; set; } = Array.Empty<double>();
    public int[] ApexSpinsTriggerWeights { get; set; } = Array.Empty<int>();
    public double[] ApexSpinsBonusMinimums { get; set; } = Array.Empty<double>();
    public int[] ApexSpinsReelsetWeights { get; set; } = Array.Empty<int>();
    public Dictionary<string, ReelSet> ApexSpinsReelsets { get; set; } = new();

    // Colossal Spins (Bonus 3) configuration
    public int[] ColossalSpinsCounts { get; set; } = Array.Empty<int>();
    public int[] ColossalSpinsTriggerWeights { get; set; } = Array.Empty<int>();
    public double[] ColossalSpinsBonusMinimums { get; set; } = Array.Empty<double>();
    public int[] ColossalSpinsReelsetWeights { get; set; } = Array.Empty<int>();
    public Dictionary<string, ReelSet> ColossalSpinsReelsets { get; set; } = new();

    // Primal Zone Bonus (Bonus 4) configuration
    public int[] PrimalZoneSpins { get; set; } = Array.Empty<int>();
    public int[] PrimalZoneTriggerWeights { get; set; } = Array.Empty<int>();
    public double[] PrimalZoneBonusMinimums { get; set; } = Array.Empty<double>();
    public double[] PrimalZoneFireCoreValues { get; set; } = Array.Empty<double>();
    public int[] PrimalZoneFireCoreWeights { get; set; } = Array.Empty<int>();
    public double[] PrimalZoneBananaValues { get; set; } = Array.Empty<double>();
    public int[] PrimalZoneBananaWeights { get; set; } = Array.Empty<int>();
    public int[] PrimalZoneStageSizes { get; set; } = new int[] { 2, 3, 4, 5 };
    public int[] PrimalZoneStageBananasRequired { get; set; } = new int[] { 5, 4, 3, 0 };
    public List<PotLandingWeight> PrimalZoneFireCoreLandingChanceWeights { get; set; } = new();
    public List<PotLandingWeight> PrimalZoneBananaLandingChanceWeights { get; set; } = new();

    // Stampede Spin configuration
    public int[] StampedePotCounts { get; set; } = Array.Empty<int>();
    public int[] StampedePotCountWeights { get; set; } = Array.Empty<int>();
    public int[] StampedePotTypeWeights { get; set; } = Array.Empty<int>();

    // Pre-allocated arrays for ultra-fast lookup during simulation
    public long[][] FastPaytable { get; private set; } = Array.Empty<long[]>();
    public bool[] FastIsWild { get; private set; } = Array.Empty<bool>();
    public bool[] FastIsScatter { get; private set; } = Array.Empty<bool>();

    public void PrepareForSimulation()
    {
        int maxSymbolId = 0;
        foreach (var sym in Symbols)
        {
            if (sym.Id > maxSymbolId) maxSymbolId = sym.Id;
        }
        
        FastIsWild = new bool[maxSymbolId + 1];
        FastIsScatter = new bool[maxSymbolId + 1];
        FastPaytable = new long[maxSymbolId + 1][];
        
        for (int i = 0; i <= maxSymbolId; i++)
        {
            FastPaytable[i] = new long[6]; // Up to 5 match count (0-5)
        }
        
        foreach (var sym in Symbols)
        {
            FastIsWild[sym.Id] = sym.IsWild;
            FastIsScatter[sym.Id] = sym.IsScatter;
            
            for (int match = 0; match <= 5; match++)
            {
                FastPaytable[sym.Id][match] = Paytable.GetPayout(sym.Id, match);
            }
        }
    }
}

public class PotLandingWeight
{
    public int Threshold { get; set; } // Empty space count threshold
    public int[] Weights { get; set; } = Array.Empty<int>(); // Weights for landing 3, 2, 1 Fire Cores or Blank
}
