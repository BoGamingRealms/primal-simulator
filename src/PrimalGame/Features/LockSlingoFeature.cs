using System;
using System.Collections.Generic;
using System.Linq;
using PrimalGame.Config;
using SlotFramework.Interfaces;

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
        bool[] gridLocked = new bool[25];
        double[] gridValues = new double[25];

        for (int spin = 0; spin < totalSpins; spin++)
        {
            int emptyCount = 0;
            for (int i = 0; i < 25; i++) if (!gridLocked[i]) emptyCount++;

            if (emptyCount == 0) break; // Optimization: all spaces locked

            // Find landing weights for this emptyCount
            int[]? landingWeights = null;
            foreach (var lw in _config.LockSlingoLandingChanceWeights)
            {
                if (emptyCount > lw.Threshold)
                {
                    landingWeights = lw.Weights;
                    break;
                }
            }
            if (landingWeights == null && _config.LockSlingoLandingChanceWeights.Count > 0)
            {
                landingWeights = _config.LockSlingoLandingChanceWeights.Last().Weights;
            }

            int rolledIndex = ChooseWeightedIndex(landingWeights ?? new int[] { 0, 0, 0, 100 }, rng);
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
                var emptyPositions = new List<int>();
                for (int i = 0; i < 25; i++)
                {
                    if (!gridLocked[i]) emptyPositions.Add(i);
                }

                for (int c = 0; c < coresToLand; c++)
                {
                    int idx = rng.Next(emptyPositions.Count);
                    int pos = emptyPositions[idx];
                    emptyPositions.RemoveAt(idx);

                    gridLocked[pos] = true;
                    int chosenValIndex = ChooseWeightedIndex(_config.LockSlingoFireCoreWeights, rng);
                    double val = _config.LockSlingoFireCoreValues[chosenValIndex];
                    gridValues[pos] = val;
                }
            }
        }

        cashValuesSum = gridValues.Sum();
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

    private static int CountSlingos(bool[] gridLocked)
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

    private static int ChooseWeightedIndex(int[] weights, IRng rng)
    {
        int totalWeight = 0;
        for (int i = 0; i < weights.Length; i++) totalWeight += weights[i];
        if (totalWeight <= 0) return 0;
        
        int r = rng.Next(totalWeight);
        int sum = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            sum += weights[i];
            if (r < sum) return i;
        }
        return 0;
    }
}
