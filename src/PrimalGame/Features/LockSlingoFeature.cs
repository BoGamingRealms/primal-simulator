using System;
using PrimalGame.Config;
using SlotFramework.Interfaces;
using SlotFramework.Utilities;

namespace PrimalGame.Features;

public class LockSlingoFeature
{
    private readonly PrimalConfig _config;

    public LockSlingoFeature(PrimalConfig config)
    {
        _config = config;
    }

    public long Run(int powerLevel, int stageIndex, IRng rng, out int completedSlingos, out double cashValuesSum, out double ladderPrize, out bool minWinApplied)
    {
        int totalSpins = _config.LockSlingoSpins[powerLevel];
        Span<bool> gridLocked = stackalloc bool[25];
        Span<double> gridValues = stackalloc double[25];
        Span<int> emptyPositions = stackalloc int[25];

        for (int spin = 0; spin < totalSpins; spin++)
        {
            int emptyCount = 0;
            for (int i = 0; i < 25; i++)
            {
                if (!gridLocked[i])
                {
                    emptyPositions[emptyCount++] = i;
                }
            }

            if (emptyCount == 0) break; // Optimization: all spaces locked

            // Find landing weight table for this emptyCount
            WeightTable? landingTable = null;
            var fastLanding = _config.FastLockSlingoLandingChanceWeights;
            for (int i = 0; i < fastLanding.Count; i++)
            {
                if (emptyCount > fastLanding[i].Threshold)
                {
                    landingTable = fastLanding[i].Table;
                    break;
                }
            }
            if (landingTable == null && fastLanding.Count > 0)
            {
                landingTable = fastLanding[^1].Table;
            }

            int rolledIndex = landingTable != null ? landingTable.Sample(rng) : 3;
            int coresToLand = rolledIndex switch
            {
                0 => 3,
                1 => 2,
                2 => 1,
                _ => 0
            };

            if (coresToLand > emptyCount) coresToLand = emptyCount;

            if (coresToLand > 0)
            {
                for (int c = 0; c < coresToLand; c++)
                {
                    int remainingEmpty = emptyCount - c;
                    int idx = rng.Next(remainingEmpty);
                    int pos = emptyPositions[idx];
                    
                    // Fast swap remove
                    emptyPositions[idx] = emptyPositions[remainingEmpty - 1];

                    gridLocked[pos] = true;
                    int chosenValIndex = _config.FastLockSlingoFireCoreWeights.Sample(rng);
                    double val = _config.LockSlingoFireCoreValues[chosenValIndex];
                    gridValues[pos] = val;
                }
            }
        }

        double sum = 0.0;
        for (int i = 0; i < 25; i++)
        {
            sum += gridValues[i];
        }
        cashValuesSum = sum;

        completedSlingos = CountSlingos(gridLocked);
        ladderPrize = GetSlingoLadderPrize(completedSlingos);

        double totalBonusMultiplier = cashValuesSum + ladderPrize;

        minWinApplied = false;
        if (stageIndex >= 5)
        {
            double minWin = _config.LockSlingoBonusMinimums[powerLevel];
            if (totalBonusMultiplier < minWin)
            {
                totalBonusMultiplier = minWin;
                minWinApplied = true;
            }
        }

        return (long)Math.Round(totalBonusMultiplier * 100.0);
    }

    private static int CountSlingos(ReadOnlySpan<bool> gridLocked)
    {
        int completed = 0;

        // Horizontal lines
        if (gridLocked[0] && gridLocked[1] && gridLocked[2] && gridLocked[3] && gridLocked[4]) completed++;
        if (gridLocked[5] && gridLocked[6] && gridLocked[7] && gridLocked[8] && gridLocked[9]) completed++;
        if (gridLocked[10] && gridLocked[11] && gridLocked[12] && gridLocked[13] && gridLocked[14]) completed++;
        if (gridLocked[15] && gridLocked[16] && gridLocked[17] && gridLocked[18] && gridLocked[19]) completed++;
        if (gridLocked[20] && gridLocked[21] && gridLocked[22] && gridLocked[23] && gridLocked[24]) completed++;

        // Vertical lines
        if (gridLocked[0] && gridLocked[5] && gridLocked[10] && gridLocked[15] && gridLocked[20]) completed++;
        if (gridLocked[1] && gridLocked[6] && gridLocked[11] && gridLocked[16] && gridLocked[21]) completed++;
        if (gridLocked[2] && gridLocked[7] && gridLocked[12] && gridLocked[17] && gridLocked[22]) completed++;
        if (gridLocked[3] && gridLocked[8] && gridLocked[13] && gridLocked[18] && gridLocked[23]) completed++;
        if (gridLocked[4] && gridLocked[9] && gridLocked[14] && gridLocked[19] && gridLocked[24]) completed++;

        // Diagonal lines
        if (gridLocked[0] && gridLocked[6] && gridLocked[12] && gridLocked[18] && gridLocked[24]) completed++;
        if (gridLocked[4] && gridLocked[8] && gridLocked[12] && gridLocked[16] && gridLocked[20]) completed++;

        return completed;
    }

    private double GetSlingoLadderPrize(int completedSlingos)
    {
        if (completedSlingos <= 0) return 0.0;
        
        double prize = 0.0;
        for (int i = 0; i < _config.LockSlingoLadderLines.Length; i++)
        {
            if (completedSlingos >= _config.LockSlingoLadderLines[i])
            {
                prize = _config.LockSlingoLadderPrizes[i];
            }
        }
        return prize;
    }
}
