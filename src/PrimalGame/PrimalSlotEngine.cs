using System;
using System.Collections.Generic;
using System.Linq;
using SlotFramework.Interfaces;
using SlotFramework.Models;
using PrimalGame.Config;

namespace PrimalGame
{
    public class PrimalSlotEngine : ISlotEngine
    {
        private readonly PrimalConfig _config;
        private string _currentStage = "Stage0";
        private int _spinsInCurrentStage = 0;
        private int _stageIndex = 0;

        public PrimalSlotEngine(PrimalConfig config)
        {
            _config = config;
        }

        public string CurrentStage => _currentStage;
        public int SpinsInCurrentStage => _spinsInCurrentStage;
        public int StageIndex => _stageIndex;

        public SpinResult Spin(IRng rng)
        {
            // 1. Advance stage counter (Base Game progression)
            _spinsInCurrentStage++;
            bool powerUpTriggered = false;

            if (_stageIndex < _config.StageSpinsToNext.Length && _spinsInCurrentStage > _config.StageSpinsToNext[_stageIndex])
            {
                // Advance to next stage if we exceed the threshold
                if (_stageIndex < 6)
                {
                    _stageIndex++;
                    _currentStage = $"Stage{_stageIndex}";
                    if (_stageIndex == 6)
                    {
                        powerUpTriggered = true; // Just entered Stage6!
                    }
                }
                else if (_stageIndex == 6)
                {
                    // Already in Stage6, and completed another 100 spins!
                    powerUpTriggered = true;
                }
                _spinsInCurrentStage = 1; // reset counter for new stage
            }

            // 2. Select Reelset based on Stage Weights
            int chosenReelsetIndex = 0;
            if (_config.BaseGameStageWeights.TryGetValue(_currentStage, out var weights))
            {
                chosenReelsetIndex = ChooseWeightedIndex(weights, rng);
            }
            string reelsetName = $"Reelset{chosenReelsetIndex}";
            
            if (!_config.Reelsets.TryGetValue(reelsetName, out var reelset))
            {
                // Fallback to Reelset0 if not found
                reelset = _config.Reelsets.Values.FirstOrDefault() ?? new ReelSet();
            }

            // 3. Perform the Spin (determine stop index for each of the 5 reels)
            var spinResult = new SpinResult
            {
                StopIndexes = new int[5],
                ScreenSymbols = new int[5][],
                SetRandomBonusPowerToMax = powerUpTriggered
            };

            for (int r = 0; r < 5; r++)
            {
                var strip = reelset.Reels[r];
                int len = strip.Length;
                int stopIndex = rng.Next(len);
                spinResult.StopIndexes[r] = stopIndex;

                // 3 visible symbols on each reel (3 rows)
                spinResult.ScreenSymbols[r] = new int[3];
                spinResult.ScreenSymbols[r][0] = reelset.GetSymbolAt(r, stopIndex, 0);
                spinResult.ScreenSymbols[r][1] = reelset.GetSymbolAt(r, stopIndex, 1);
                spinResult.ScreenSymbols[r][2] = reelset.GetSymbolAt(r, stopIndex, 2);
            }

            // 4. Evaluate Payline Wins
            EvaluateLineWins(spinResult);

            // 5. Evaluate Jackpot Trigger Collections
            EvaluateCollections(spinResult, rng);

            return spinResult;
        }

        public SpinResult FreeSpin(IRng rng, int currentFreeSpinIndex, int totalFreeSpins)
        {
            // Free spins fallback to Stage6 reelset or first available reelset for simplicity
            string reelsetName = "Reelset19"; // often a higher-paying reelset in stage weights
            if (!_config.Reelsets.TryGetValue(reelsetName, out var reelset))
            {
                reelset = _config.Reelsets.Values.FirstOrDefault() ?? new ReelSet();
            }

            var spinResult = new SpinResult
            {
                StopIndexes = new int[5],
                ScreenSymbols = new int[5][],
                Multiplier = _config.FreeSpinsMultiplier
            };

            for (int r = 0; r < 5; r++)
            {
                var strip = reelset.Reels[r];
                int len = strip.Length;
                int stopIndex = rng.Next(len);
                spinResult.StopIndexes[r] = stopIndex;

                spinResult.ScreenSymbols[r] = new int[3];
                spinResult.ScreenSymbols[r][0] = reelset.GetSymbolAt(r, stopIndex, 0);
                spinResult.ScreenSymbols[r][1] = reelset.GetSymbolAt(r, stopIndex, 1);
                spinResult.ScreenSymbols[r][2] = reelset.GetSymbolAt(r, stopIndex, 2);
            }

            EvaluateLineWins(spinResult);
            spinResult.TotalWin *= _config.FreeSpinsMultiplier;

            // Evaluate Jackpot Trigger Collections in Free Spins (without fs multiplier applying to it)
            EvaluateCollections(spinResult, rng);

            return spinResult;
        }

        private void EvaluateLineWins(SpinResult spinResult)
        {
            for (int lineId = 0; lineId < _config.Paylines.Length; lineId++)
            {
                var payline = _config.Paylines[lineId];
                long maxPayout = 0;
                int bestSymId = -1;
                int bestMatchCount = 0;

                // Evaluate each possible paying symbol
                foreach (var sym in _config.Symbols)
                {
                    // Skip Wild, Scatter, and trigger symbols which do not have line payouts
                    if (sym.IsWild || sym.IsScatter || sym.Id >= 9) continue;

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

                    long payout = _config.Paytable.GetPayout(sym.Id, matchCount);
                    if (payout > maxPayout)
                    {
                        maxPayout = payout;
                        bestSymId = sym.Id;
                        bestMatchCount = matchCount;
                    }
                }

                if (maxPayout > 0)
                {
                    spinResult.LineWins.Add(new LineWin
                    {
                        LineId = lineId + 1, // 1-indexed for presentation
                        SymbolId = bestSymId,
                        MatchCount = bestMatchCount,
                        Payout = maxPayout
                    });
                    spinResult.TotalWin += maxPayout;
                }
            }
        }

        private void EvaluateCollections(SpinResult spinResult, IRng rng)
        {
            // 1. Count collectors on Reel 0 and Reel 4
            int collectorsReel0 = 0;
            for (int row = 0; row < 3; row++)
            {
                if (spinResult.ScreenSymbols[0][row] == _config.CollectorSymbolId)
                {
                    collectorsReel0++;
                }
            }

            int collectorsReel4 = 0;
            for (int row = 0; row < 3; row++)
            {
                if (spinResult.ScreenSymbols[4][row] == _config.CollectorSymbolId)
                {
                    collectorsReel4++;
                }
            }

            int totalCollectors = collectorsReel0 + collectorsReel4;

            // 2. Count Jackpot Triggers on the screen
            int triggerCount = 0;
            for (int r = 0; r < 5; r++)
            {
                for (int row = 0; row < 3; row++)
                {
                    if (spinResult.ScreenSymbols[r][row] == _config.JackpotTriggerSymbolId)
                    {
                        triggerCount++;
                    }
                }
            }

            if (triggerCount > 0)
            {
                // Draw a cash value for each landed trigger
                double sumMultipliers = 0.0;
                for (int i = 0; i < triggerCount; i++)
                {
                    if (_config.JackpotTriggerCashValues.Length > 0 && _config.JackpotTriggerCashWeights.Length > 0)
                    {
                        int chosenValIndex = ChooseWeightedIndex(_config.JackpotTriggerCashWeights, rng);
                        sumMultipliers += _config.JackpotTriggerCashValues[chosenValIndex];
                    }
                }

                if (totalCollectors > 0)
                {
                    long featureWinInCents = (long)Math.Round(totalCollectors * sumMultipliers * 100.0);
                    spinResult.FeatureWin = featureWinInCents;
                    spinResult.TotalWin += featureWinInCents;
                    spinResult.CollectorTriggered = true;
                    spinResult.CollectorCount = totalCollectors;
                    spinResult.TotalCollectedMultiplier = sumMultipliers;
                }
                else
                {
                    // No collector in view, but there are jackpot triggers!
                    // Check if jackpot bonus is triggered.
                    int jackpotBonusTriggerWeight = _config.JackpotBonusTriggerChanceWeight; // e.g. 2000
                    if (jackpotBonusTriggerWeight > 0)
                    {
                        // Total chance is triggerCount in jackpotBonusTriggerWeight.
                        if (rng.Next(jackpotBonusTriggerWeight) < triggerCount)
                        {
                            // Trigger Jackpot Bonus!
                            spinResult.JackpotBonusTriggered = true;
                            
                            // Spin the big wheel to win a jackpot!
                            if (_config.JackpotWeights.Length > 0 && _config.JackpotNames.Length > 0)
                            {
                                int chosenJackpotIndex = ChooseWeightedIndex(_config.JackpotWeights, rng);
                                string jpName = _config.JackpotNames[chosenJackpotIndex];
                                double jpMultiplier = _config.JackpotValues[chosenJackpotIndex];
                                
                                long jpWinInCents = (long)Math.Round(jpMultiplier * 100.0);
                                spinResult.WonJackpotName = jpName;
                                spinResult.WonJackpotValue = jpMultiplier;
                                spinResult.JackpotBonusWin = jpWinInCents;
                                
                                spinResult.FeatureWin += jpWinInCents;
                                spinResult.TotalWin += jpWinInCents;
                            }
                        }
                    }
                }
            }
        }

        private int ChooseWeightedIndex(int[] weights, IRng rng)
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
}
