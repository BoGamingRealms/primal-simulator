using System;
using System.Collections.Generic;
using System.Linq;
using PrimalGame.Config;
using SlotFramework.Interfaces;

namespace PrimalGame.Features;

public class PrimalZoneFeature
{
    private readonly PrimalConfig _config;

    public PrimalZoneFeature(PrimalConfig config)
    {
        _config = config;
    }

    public long Run(int powerLevel, int stageIndex, IRng rng, out int totalBananasCollected, out int finalStage, out int finalSize, out bool minWinApplied)
    {
        int totalSpins = _config.PrimalZoneSpins.Length > powerLevel ? _config.PrimalZoneSpins[powerLevel] : 5;
        long totalBonusWinInCents = 0;
        totalBananasCollected = 0;

        int currentStage = 0; // 0 = 2x2, 1 = 3x3, 2 = 4x4, 3 = 5x5
        int currentSize = _config.PrimalZoneStageSizes.Length > currentStage ? _config.PrimalZoneStageSizes[currentStage] : 2;
        int pzRow = 0; // Top-left row (0..4)
        int pzCol = 0; // Top-left col (0..4)
        int bananasInCurrentStage = 0;

        for (int spin = 0; spin < totalSpins; spin++)
        {
            int[] fireCoreWeights = GetLandingWeights(currentSize, _config.PrimalZoneFireCoreLandingChanceWeights);
            int numFireCores = ChooseLandingCount(fireCoreWeights, rng);

            int[] bananaWeights = GetLandingWeights(currentSize, _config.PrimalZoneBananaLandingChanceWeights);
            int numBananas = ChooseLandingCount(bananaWeights, rng);

            int totalItems = Math.Min(25, numFireCores + numBananas);
            List<int> positions = SelectUniquePositions(25, totalItems, rng);

            int actualCores = Math.Min(numFireCores, positions.Count);
            int actualBananas = Math.Min(numBananas, positions.Count - actualCores);

            List<int> corePositions = positions.Take(actualCores).ToList();
            List<int> bananaPositions = positions.Skip(actualCores).Take(actualBananas).ToList();

            Dictionary<int, long> coreValues = new();
            foreach (int pos in corePositions)
            {
                int valIdx = ChooseWeightedIndex(_config.PrimalZoneFireCoreWeights, rng);
                double valMultiplier = _config.PrimalZoneFireCoreValues[valIdx];
                coreValues[pos] = (long)Math.Round(valMultiplier * 100.0);
            }

            Dictionary<int, long> bananaValues = new();
            foreach (int pos in bananaPositions)
            {
                int valIdx = ChooseWeightedIndex(_config.PrimalZoneBananaWeights, rng);
                double valMultiplier = _config.PrimalZoneBananaValues[valIdx];
                bananaValues[pos] = (long)Math.Round(valMultiplier * 100.0);
            }

            static bool IsCovered(int pos, int r, int c, int size)
            {
                int itemRow = pos / 5;
                int itemCol = pos % 5;
                return itemRow >= r && itemRow < r + size && itemCol >= c && itemCol < c + size;
            }

            foreach (int pos in corePositions)
            {
                if (IsCovered(pos, pzRow, pzCol, currentSize))
                {
                    totalBonusWinInCents += coreValues[pos];
                }
            }

            List<int> remainingBananas = new List<int>();
            foreach (int pos in bananaPositions)
            {
                if (IsCovered(pos, pzRow, pzCol, currentSize))
                {
                    totalBonusWinInCents += bananaValues[pos];
                    totalBananasCollected++;
                    bananasInCurrentStage++;

                    CheckAndAdvanceStage(ref currentStage, ref currentSize, ref bananasInCurrentStage, ref pzRow, ref pzCol);
                }
                else
                {
                    remainingBananas.Add(pos);
                }
            }

            while (remainingBananas.Count > 0)
            {
                int bestPos = -1;
                int minDistance = int.MaxValue;
                (int targetR, int targetC) bestTarget = (pzRow, pzCol);

                foreach (int bPos in remainingBananas)
                {
                    int bRow = bPos / 5;
                    int bCol = bPos % 5;

                    int targetR = Math.Clamp(pzRow, bRow - currentSize + 1, bRow);
                    int targetC = Math.Clamp(pzCol, bCol - currentSize + 1, bCol);
                    targetR = Math.Clamp(targetR, 0, 5 - currentSize);
                    targetC = Math.Clamp(targetC, 0, 5 - currentSize);

                    int dist = Math.Abs(pzRow - targetR) + Math.Abs(pzCol - targetC);

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        bestPos = bPos;
                        bestTarget = (targetR, targetC);
                    }
                    else if (dist == minDistance && bestPos != -1)
                    {
                        int curBRow = bestPos / 5;
                        int curBCol = bestPos % 5;

                        if (bRow < curBRow || (bRow == curBRow && bCol < curBCol))
                        {
                            bestPos = bPos;
                            bestTarget = (targetR, targetC);
                        }
                    }
                }

                while (pzRow != bestTarget.targetR || pzCol != bestTarget.targetC)
                {
                    if (pzRow > bestTarget.targetR) pzRow--;
                    else if (pzRow < bestTarget.targetR) pzRow++;
                    else if (pzCol > bestTarget.targetC) pzCol--;
                    else if (pzCol < bestTarget.targetC) pzCol++;

                    for (int i = remainingBananas.Count - 1; i >= 0; i--)
                    {
                        int bPos = remainingBananas[i];
                        if (IsCovered(bPos, pzRow, pzCol, currentSize))
                        {
                            totalBonusWinInCents += bananaValues[bPos];
                            totalBananasCollected++;
                            bananasInCurrentStage++;
                            remainingBananas.RemoveAt(i);

                            CheckAndAdvanceStage(ref currentStage, ref currentSize, ref bananasInCurrentStage, ref pzRow, ref pzCol);
                        }
                    }
                }
            }
        }

        finalStage = currentStage;
        finalSize = currentSize;

        minWinApplied = false;
        if (stageIndex >= 5 && _config.PrimalZoneBonusMinimums.Length > powerLevel)
        {
            double minWinMultiplier = _config.PrimalZoneBonusMinimums[powerLevel];
            long minWinInCents = (long)Math.Round(minWinMultiplier * 100.0);
            if (totalBonusWinInCents < minWinInCents)
            {
                totalBonusWinInCents = minWinInCents;
                minWinApplied = true;
            }
        }

        return totalBonusWinInCents;
    }

    private void CheckAndAdvanceStage(ref int currentStage, ref int currentSize, ref int bananasInCurrentStage, ref int pzRow, ref int pzCol)
    {
        if (currentStage < 3)
        {
            int required = _config.PrimalZoneStageBananasRequired[currentStage];
            if (bananasInCurrentStage >= required)
            {
                currentStage++;
                currentSize = _config.PrimalZoneStageSizes[currentStage];
                bananasInCurrentStage = 0;

                pzRow = Math.Clamp(pzRow, 0, 5 - currentSize);
                pzCol = Math.Clamp(pzCol, 0, 5 - currentSize);
            }
        }
    }

    private static int[] GetLandingWeights(int zoneSize, List<PotLandingWeight> chanceWeights)
    {
        foreach (var lw in chanceWeights)
        {
            if (zoneSize == lw.Threshold)
            {
                return lw.Weights;
            }
        }
        return chanceWeights.FirstOrDefault()?.Weights ?? new int[] { 100, 0, 0, 0 };
    }

    private static int ChooseLandingCount(int[] weights, IRng rng)
    {
        int idx = ChooseWeightedIndex(weights, rng);
        return idx; // 0..3
    }

    private static List<int> SelectUniquePositions(int poolSize, int count, IRng rng)
    {
        List<int> pool = Enumerable.Range(0, poolSize).ToList();
        List<int> chosen = new List<int>();

        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int idx = rng.Next(pool.Count);
            chosen.Add(pool[idx]);
            pool.RemoveAt(idx);
        }

        return chosen;
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
