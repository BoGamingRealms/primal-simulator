using System;
using System.Collections.Generic;
using PrimalGame.Config;
using SlotFramework.Interfaces;
using SlotFramework.Utilities;

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

        Span<int> positions = stackalloc int[25];
        Span<long> coreValues = stackalloc long[25];
        Span<long> bananaValues = stackalloc long[25];
        Span<int> remainingBananas = stackalloc int[25];

        for (int spin = 0; spin < totalSpins; spin++)
        {
            var fireCoreTable = GetLandingTable(currentSize, _config.FastPrimalZoneFireCoreLandingChanceWeights);
            int numFireCores = fireCoreTable != null ? fireCoreTable.Sample(rng) : 0;

            var bananaTable = GetLandingTable(currentSize, _config.FastPrimalZoneBananaLandingChanceWeights);
            int numBananas = bananaTable != null ? bananaTable.Sample(rng) : 0;

            int totalItems = Math.Min(25, numFireCores + numBananas);
            SelectUniquePositions(25, totalItems, rng, positions);

            int actualCores = Math.Min(numFireCores, totalItems);
            int actualBananas = Math.Min(numBananas, totalItems - actualCores);

            coreValues.Clear();
            bananaValues.Clear();

            for (int i = 0; i < actualCores; i++)
            {
                int pos = positions[i];
                int valIdx = _config.FastPrimalZoneFireCoreWeights.Sample(rng);
                double valMultiplier = _config.PrimalZoneFireCoreValues[valIdx];
                coreValues[pos] = (long)Math.Round(valMultiplier * 100.0);
            }

            for (int i = 0; i < actualBananas; i++)
            {
                int pos = positions[actualCores + i];
                int valIdx = _config.FastPrimalZoneBananaWeights.Sample(rng);
                double valMultiplier = _config.PrimalZoneBananaValues[valIdx];
                bananaValues[pos] = (long)Math.Round(valMultiplier * 100.0);
            }

            static bool IsCovered(int pos, int r, int c, int size)
            {
                int itemRow = pos / 5;
                int itemCol = pos % 5;
                return itemRow >= r && itemRow < r + size && itemCol >= c && itemCol < c + size;
            }

            for (int i = 0; i < actualCores; i++)
            {
                int pos = positions[i];
                if (IsCovered(pos, pzRow, pzCol, currentSize))
                {
                    totalBonusWinInCents += coreValues[pos];
                }
            }

            int remainingCount = 0;
            for (int i = 0; i < actualBananas; i++)
            {
                remainingBananas[remainingCount++] = positions[actualCores + i];
            }

            while (remainingCount > 0)
            {
                // 1. Collect any bananas covered by current zone
                bool collectedAny = false;
                for (int i = remainingCount - 1; i >= 0; i--)
                {
                    int bPos = remainingBananas[i];
                    if (IsCovered(bPos, pzRow, pzCol, currentSize))
                    {
                        totalBonusWinInCents += bananaValues[bPos];
                        totalBananasCollected++;
                        bananasInCurrentStage++;

                        remainingBananas[i] = remainingBananas[remainingCount - 1];
                        remainingCount--;
                        collectedAny = true;

                        CheckAndAdvanceStage(ref currentStage, ref currentSize, ref bananasInCurrentStage, ref pzRow, ref pzCol);
                    }
                }

                if (remainingCount == 0) break;
                if (collectedAny) continue;

                // 2. Select closest remaining banana
                int bestPos = -1;
                int minDistance = int.MaxValue;
                int targetR = pzRow;
                int targetC = pzCol;

                for (int idx = 0; idx < remainingCount; idx++)
                {
                    int bPos = remainingBananas[idx];
                    int bRow = bPos / 5;
                    int bCol = bPos % 5;

                    int tR = Math.Clamp(pzRow, bRow - currentSize + 1, bRow);
                    int tC = Math.Clamp(pzCol, bCol - currentSize + 1, bCol);
                    tR = Math.Clamp(tR, 0, 5 - currentSize);
                    tC = Math.Clamp(tC, 0, 5 - currentSize);

                    int dist = Math.Abs(pzRow - tR) + Math.Abs(pzCol - tC);

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        bestPos = bPos;
                        targetR = tR;
                        targetC = tC;
                    }
                    else if (dist == minDistance && bestPos != -1)
                    {
                        int curBRow = bestPos / 5;
                        int curBCol = bestPos % 5;

                        if (bRow < curBRow || (bRow == curBRow && bCol < curBCol))
                        {
                            bestPos = bPos;
                            targetR = tR;
                            targetC = tC;
                        }
                    }
                }

                // 3. Step 1 unit towards target
                if (pzRow > targetR) pzRow--;
                else if (pzRow < targetR) pzRow++;
                else if (pzCol > targetC) pzCol--;
                else if (pzCol < targetC) pzCol++;
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

    private static WeightTable? GetLandingTable(int zoneSize, List<(int Threshold, WeightTable Table)> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (zoneSize == list[i].Threshold) return list[i].Table;
        }
        return list.Count > 0 ? list[0].Table : null;
    }

    private static void SelectUniquePositions(int poolSize, int count, IRng rng, Span<int> output)
    {
        Span<int> pool = stackalloc int[poolSize];
        for (int i = 0; i < poolSize; i++) pool[i] = i;
        for (int i = 0; i < count && i < poolSize; i++)
        {
            int j = i + rng.Next(poolSize - i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
            output[i] = pool[i];
        }
    }
}
