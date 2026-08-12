using System;
using System.Linq;
using PrimalGame.Config;
using SlotFramework.Models;
using SlotFramework.Interfaces;

namespace PrimalGame.Features;

public class ApexSpinsFeature
{
    private readonly PrimalConfig _config;

    public ApexSpinsFeature(PrimalConfig config)
    {
        _config = config;
    }

    public long Run(int powerLevel, int stageIndex, IRng rng, Func<int[][], long> evaluateGridLineWins, out int spinsPlayed, out bool minWinApplied)
    {
        double topSpinAwardMultiplier = _config.ApexSpinsTopAwardMultipliers[powerLevel];
        long topSpinAwardInCents = (long)Math.Round(topSpinAwardMultiplier * 100.0);

        Span<bool> lockedWilds = stackalloc bool[15];
        spinsPlayed = 0;
        long totalBonusWinInCents = 0;

        int[][] screenSymbols = new int[5][]
        {
            new int[3], new int[3], new int[3], new int[3], new int[3]
        };

        while (true)
        {
            spinsPlayed++;

            int chosenIdx = _config.FastApexSpinsReelsetWeights.Sample(rng);
            string reelsetName = $"Reelset{chosenIdx}";

            if (!_config.ApexSpinsReelsets.TryGetValue(reelsetName, out var reelset))
            {
                reelset = _config.ApexSpinsReelsets.Values.FirstOrDefault() ?? _config.BaseReels;
            }

            for (int r = 0; r < 5; r++)
            {
                var strip = reelset.Reels[r];
                int stopIndex = rng.Next(strip.Length);
                screenSymbols[r][0] = reelset.GetSymbolAt(r, stopIndex, 0);
                screenSymbols[r][1] = reelset.GetSymbolAt(r, stopIndex, 1);
                screenSymbols[r][2] = reelset.GetSymbolAt(r, stopIndex, 2);
            }

            for (int r = 0; r < 5; r++)
            {
                for (int row = 0; row < 3; row++)
                {
                    int pos = r * 3 + row;
                    if (lockedWilds[pos])
                    {
                        screenSymbols[r][row] = _config.WildSymbolId;
                    }
                    else if (screenSymbols[r][row] == _config.WildSymbolId)
                    {
                        lockedWilds[pos] = true;
                    }
                }
            }

            long singleSpinWin = evaluateGridLineWins(screenSymbols);
            totalBonusWinInCents += singleSpinWin;

            if (singleSpinWin >= topSpinAwardInCents)
            {
                break;
            }
        }

        minWinApplied = false;
        if (stageIndex >= 5 && _config.ApexSpinsBonusMinimums.Length > powerLevel)
        {
            double minWinMultiplier = _config.ApexSpinsBonusMinimums[powerLevel];
            long minWinInCents = (long)Math.Round(minWinMultiplier * 100.0);
            if (totalBonusWinInCents < minWinInCents)
            {
                totalBonusWinInCents = minWinInCents;
                minWinApplied = true;
            }
        }

        return totalBonusWinInCents;
    }
}
