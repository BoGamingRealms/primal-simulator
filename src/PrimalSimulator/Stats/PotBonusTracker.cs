using System;
using System.Collections.Generic;
using SlotFramework.Models;

namespace PrimalSimulator.Stats;

public class PotBonusTracker
{
    public int PotIndex { get; }
    public string BonusName { get; }
    public int Triggers { get; private set; }
    public long TotalWin { get; private set; }
    public long TotalSpinsPlayed { get; private set; }
    public int MinWinAppliedCount { get; private set; }

    public int[] TriggersByPower { get; }
    public long[] WinByPower { get; }
    public long[] SpinsPlayedByPower { get; }

    public PotBonusTracker(int potIndex, string bonusName, int maxPowerLevels = 13)
    {
        PotIndex = potIndex;
        BonusName = bonusName;
        TriggersByPower = new int[maxPowerLevels];
        WinByPower = new long[maxPowerLevels];
        SpinsPlayedByPower = new long[maxPowerLevels];
    }

    public void RecordTrigger(TriggeredPotBonus bonus)
    {
        Triggers++;
        TotalWin += bonus.Win;
        TotalSpinsPlayed += bonus.SpinsPlayed;

        if (bonus.MinWinApplied)
        {
            MinWinAppliedCount++;
        }

        if (bonus.Power >= 0 && bonus.Power < TriggersByPower.Length)
        {
            TriggersByPower[bonus.Power]++;
            WinByPower[bonus.Power] += bonus.Win;
            SpinsPlayedByPower[bonus.Power] += bonus.SpinsPlayed;
        }
    }
}
