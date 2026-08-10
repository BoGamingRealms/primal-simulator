using System;
using System.Collections.Generic;
using System.Linq;
using PrimalGame.Config;
using SlotFramework.Models;
using SlotFramework.Interfaces;

namespace PrimalGame.Features;

public class ColossalSpinsFeature
{
    private readonly PrimalConfig _config;

    public ColossalSpinsFeature(PrimalConfig config)
    {
        _config = config;
    }

    public long Run(int powerLevel, int stageIndex, IRng rng, Func<int[][], long> evaluateGridLineWins, out int spinsPlayed, out bool minWinApplied, out Dictionary<int, long> colossalSymbolWins, out Dictionary<int, int> colossalSymbolHits)
    {
        int totalSpins = _config.ColossalSpinsCounts.Length > powerLevel ? _config.ColossalSpinsCounts[powerLevel] : 5;
        spinsPlayed = totalSpins;
        long totalBonusWinInCents = 0;

        colossalSymbolWins = new Dictionary<int, long>();
        colossalSymbolHits = new Dictionary<int, int>();

        for (int spin = 0; spin < totalSpins; spin++)
        {
            int chosenIdx = ChooseWeightedIndex(_config.ColossalSpinsReelsetWeights, rng);
            string reelsetName = $"Reelset{chosenIdx}";

            if (!_config.ColossalSpinsReelsets.TryGetValue(reelsetName, out var reelset))
            {
                reelset = _config.ColossalSpinsReelsets.Values.FirstOrDefault() ?? _config.BaseReels;
            }

            int len0 = reelset.Reels[0].Length;
            int lenMid = reelset.Reels[1].Length;
            int len4 = reelset.Reels[4].Length;

            int[][] screenSymbols = new int[5][];

            // Reel 0 (independent stop index)
            int stop0 = rng.Next(len0);
            screenSymbols[0] = new int[3];
            screenSymbols[0][0] = reelset.GetSymbolAt(0, stop0, 0);
            screenSymbols[0][1] = reelset.GetSymbolAt(0, stop0, 1);
            screenSymbols[0][2] = reelset.GetSymbolAt(0, stop0, 2);

            // Middle 3 reels (Reels 1, 2, 3) spin TOGETHER with a single shared stop index!
            int stopMid = rng.Next(lenMid);
            for (int r = 1; r <= 3; r++)
            {
                screenSymbols[r] = new int[3];
                screenSymbols[r][0] = reelset.GetSymbolAt(r, stopMid, 0);
                screenSymbols[r][1] = reelset.GetSymbolAt(r, stopMid, 1);
                screenSymbols[r][2] = reelset.GetSymbolAt(r, stopMid, 2);
            }

            // Reel 4 (independent stop index)
            int stop4 = rng.Next(len4);
            screenSymbols[4] = new int[3];
            screenSymbols[4][0] = reelset.GetSymbolAt(4, stop4, 0);
            screenSymbols[4][1] = reelset.GetSymbolAt(4, stop4, 1);
            screenSymbols[4][2] = reelset.GetSymbolAt(4, stop4, 2);

            long spinWin = evaluateGridLineWins(screenSymbols);
            totalBonusWinInCents += spinWin;

            int colossalSym = screenSymbols[1][1];
            if (!colossalSymbolWins.ContainsKey(colossalSym))
            {
                colossalSymbolWins[colossalSym] = 0;
                colossalSymbolHits[colossalSym] = 0;
            }
            colossalSymbolHits[colossalSym]++;
            colossalSymbolWins[colossalSym] += spinWin;
        }

        minWinApplied = false;
        if (stageIndex >= 5 && _config.ColossalSpinsBonusMinimums.Length > powerLevel)
        {
            double minWinMultiplier = _config.ColossalSpinsBonusMinimums[powerLevel];
            long minWinInCents = (long)Math.Round(minWinMultiplier * 100.0);
            if (totalBonusWinInCents < minWinInCents)
            {
                totalBonusWinInCents = minWinInCents;
                minWinApplied = true;
            }
        }

        return totalBonusWinInCents;
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
