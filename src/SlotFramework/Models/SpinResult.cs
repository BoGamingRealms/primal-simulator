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

    // Feature details
    public long FeatureWin { get; set; } = 0;
    public bool CollectorTriggered { get; set; } = false;
    public int CollectorCount { get; set; } = 0;
    public double TotalCollectedMultiplier { get; set; } = 0.0;
    public bool SetRandomBonusPowerToMax { get; set; } = false;

    // Jackpot Bonus details
    public bool JackpotBonusTriggered { get; set; } = false;
    public string WonJackpotName { get; set; } = "";
    public double WonJackpotValue { get; set; } = 0.0;
    public long JackpotBonusWin { get; set; } = 0;

    // Pot Bonus details
    public List<TriggeredPotBonus> TriggeredPotBonuses { get; set; } = new();
    public int[] PotPowersBefore { get; set; } = System.Array.Empty<int>();
    public int[] PotPowersAfter { get; set; } = System.Array.Empty<int>();
}

public class TriggeredPotBonus
{
    public int PotIndex { get; set; } // 0 = Bonus 1, 1 = Bonus 2, 2 = Bonus 3, 3 = Bonus 4
    public int Power { get; set; }    // Power level of the bonus when triggered (current + N - 1)
}
