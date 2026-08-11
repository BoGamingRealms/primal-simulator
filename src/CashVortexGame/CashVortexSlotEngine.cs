using System;
using System.Collections.Generic;
using System.Linq;
using SlotFramework.Interfaces;
using SlotFramework.Models;
using CashVortexGame.Config;

namespace CashVortexGame;

public class CashVortexSlotEngine : ISlotEngine
{
    private readonly CashVortexConfig _config;
    private string _currentStage = "Stage0";
    private int _spinsInCurrentStage = 0;
    private int _stageIndex = 0;
    private readonly int[] _potPowers = new int[3] { 0, 0, 0 }; // Triple Power Pots

    public CashVortexSlotEngine(CashVortexConfig config)
    {
        _config = config;
    }

    public string CurrentStage => _currentStage;
    public int SpinsInCurrentStage => _spinsInCurrentStage;
    public int StageIndex => _stageIndex;
    public int[] PotPowers => _potPowers;

    public SpinResult Spin(IRng rng)
    {
        _spinsInCurrentStage++;

        var spinResult = new SpinResult
        {
            StopIndexes = new int[5],
            ScreenSymbols = new int[5][],
            PotPowersBefore = _potPowers.ToArray()
        };

        // Select Reelset
        ReelSet reelset = _config.Reelsets.Values.FirstOrDefault() ?? _config.BaseReels;

        for (int r = 0; r < 5; r++)
        {
            spinResult.ScreenSymbols[r] = new int[3];
            if (reelset.Reels.Length > r && reelset.Reels[r].Length > 0)
            {
                int len = reelset.Reels[r].Length;
                int stopIndex = rng.Next(len);
                spinResult.StopIndexes[r] = stopIndex;

                spinResult.ScreenSymbols[r][0] = reelset.GetSymbolAt(r, stopIndex, 0);
                spinResult.ScreenSymbols[r][1] = reelset.GetSymbolAt(r, stopIndex, 1);
                spinResult.ScreenSymbols[r][2] = reelset.GetSymbolAt(r, stopIndex, 2);
            }
        }

        EvaluateLineWins(spinResult);
        spinResult.PotPowersAfter = _potPowers.ToArray();

        return spinResult;
    }

    public SpinResult FreeSpin(IRng rng, int currentFreeSpinIndex, int totalFreeSpins)
    {
        return Spin(rng);
    }

    private void EvaluateLineWins(SpinResult spinResult)
    {
        for (int lineId = 0; lineId < _config.Paylines.Length; lineId++)
        {
            var payline = _config.Paylines[lineId];
            long maxPayout = 0;

            foreach (var sym in _config.Symbols)
            {
                if (sym.IsWild || sym.IsScatter) continue;

                int matchCount = 0;
                for (int reel = 0; reel < 5; reel++)
                {
                    int rowIndex = payline[reel];
                    int screenSym = spinResult.ScreenSymbols[reel][rowIndex];

                    if (screenSym == sym.Id || screenSym == _config.WildSymbolId)
                    {
                        matchCount++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (sym.Id >= 0 && sym.Id < 16 && matchCount >= 0 && matchCount < 6)
                {
                    long payout = _config.FastPaytableMatrix[sym.Id, matchCount];
                    if (payout > maxPayout)
                    {
                        maxPayout = payout;
                    }
                }
            }

            if (maxPayout > 0)
            {
                spinResult.LineWins.Add(new LineWin
                {
                    LineId = lineId + 1,
                    Payout = maxPayout
                });
                spinResult.TotalWin += maxPayout;
            }
        }
    }
}
