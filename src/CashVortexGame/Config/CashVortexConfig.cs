using System;
using System.Collections.Generic;
using SlotFramework.Models;

namespace CashVortexGame.Config;

public class CashVortexConfig
{
    public string GameName { get; set; } = "Cash Vortex - Triple Power";
    public List<Symbol> Symbols { get; set; } = new();
    public Paytable Paytable { get; set; } = new();
    public ReelSet BaseReels { get; set; } = new();
    public Dictionary<string, ReelSet> Reelsets { get; set; } = new();
    public int[][] Paylines { get; set; } = Array.Empty<int[]>();
    public long[,] FastPaytableMatrix { get; set; } = new long[16, 6];

    public int WildSymbolId { get; set; }
    public int ScatterSymbolId { get; set; }

    public Dictionary<string, int[]> BaseGameStageWeights { get; set; } = new();
    public int[] StageSpinsToNext { get; set; } = Array.Empty<int>();
}
