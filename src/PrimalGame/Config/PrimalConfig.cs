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

    // Jackpot trigger cash value feature configuration
    public double[] JackpotTriggerCashValues { get; set; } = Array.Empty<double>();
    public int[] JackpotTriggerCashWeights { get; set; } = Array.Empty<int>();
    public int JackpotTriggerSymbolId { get; set; } = 14;
    public int CollectorSymbolId { get; set; } = 9;

    // Jackpot Bonus configuration
    public int JackpotBonusTriggerChanceWeight { get; set; } = 2000;
    public string[] JackpotNames { get; set; } = Array.Empty<string>();
    public double[] JackpotValues { get; set; } = Array.Empty<double>();
    public int[] JackpotWeights { get; set; } = Array.Empty<int>();

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
