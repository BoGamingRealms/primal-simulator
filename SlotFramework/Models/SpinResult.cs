using System.Collections.Generic;

namespace SlotFramework.Models;

public struct LineWin
{
    public int LineId { get; set; }
    public int SymbolId { get; set; }
    public int MatchCount { get; set; }
    public long Payout { get; set; }
}

public class SpinResult
{
    public int[] StopIndexes { get; set; } = System.Array.Empty<int>();
    public int[][] ScreenSymbols { get; set; } = System.Array.Empty<int[]>();
    public List<LineWin> LineWins { get; set; } = new();
    public long ScatterWin { get; set; }
    public int FreeSpinsTriggered { get; set; }
    public long TotalWin { get; set; }
    
    public bool TriggeredFeature { get; set; }
    public int Multiplier { get; set; } = 1;
}
