using System;
using System.Collections.Generic;
using System.Linq;
using SlotFramework.Interfaces;
using SlotFramework.Models;
using SlotFramework.Utilities;
using PrimalGame.Config;
using PrimalGame.Features;

namespace PrimalGame
{
    public class PrimalSlotEngine : ISlotEngine
    {
        private readonly PrimalConfig _config;
        private readonly LockSlingoFeature _lockSlingoFeature;
        private readonly ApexSpinsFeature _apexSpinsFeature;
        private readonly ColossalSpinsFeature _colossalSpinsFeature;
        private readonly PrimalZoneFeature _primalZoneFeature;

        private string _currentStage = "Stage0";
        private int _spinsInCurrentStage = 0;
        private int _stageIndex = 0;
        private readonly int[] _potPowers = new int[4] { 0, 0, 0, 0 };

        public int[] PotPowers => _potPowers;

        public PrimalSlotEngine(PrimalConfig config)
        {
            _config = config;
            _lockSlingoFeature = new LockSlingoFeature(config);
            _apexSpinsFeature = new ApexSpinsFeature(config);
            _colossalSpinsFeature = new ColossalSpinsFeature(config);
            _primalZoneFeature = new PrimalZoneFeature(config);
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

            if (powerUpTriggered)
            {
                int p = rng.Next(4);
                if (p == 0)
                {
                    _potPowers[0] = Math.Max(0, _config.LockSlingoSpins.Length - 1);
                }
                else if (p == 1)
                {
                    _potPowers[1] = Math.Max(0, _config.ApexSpinsTopAwardMultipliers.Length - 1);
                }
                else if (p == 2)
                {
                    _potPowers[2] = Math.Max(0, _config.ColossalSpinsCounts.Length - 1);
                }
                else
                {
                    _potPowers[p] = _config.MaxBonusPower;
                }
            }

            // 2. Select Reelset based on Stage Weights
            int chosenReelsetIndex = 0;
            if (_config.FastBaseGameStageWeights.TryGetValue(_currentStage, out var weightTable))
            {
                chosenReelsetIndex = weightTable.Sample(rng);
            }
            string reelsetName = $"Reelset{chosenReelsetIndex}";
            
            if (!_config.Reelsets.TryGetValue(reelsetName, out var reelset))
            {
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

            // 3b. Stampede Spin check (Reelsets 11, 12, 13)
            if (chosenReelsetIndex == 11 || chosenReelsetIndex == 12 || chosenReelsetIndex == 13)
            {
                spinResult.IsStampedeSpin = true;

                if (_config.StampedePotCounts.Length > 0 && _config.FastStampedePotCountWeights.TotalWeight > 0)
                {
                    int kIdx = _config.FastStampedePotCountWeights.Sample(rng);
                    int potsToAdd = _config.StampedePotCounts[Math.Min(kIdx, _config.StampedePotCounts.Length - 1)];
                    spinResult.StampedeAddedPotCount = potsToAdd;

                    Span<int> positions = stackalloc int[15];
                    SelectUniquePositions(15, potsToAdd, rng, positions);
                    for (int i = 0; i < potsToAdd && i < 15; i++)
                    {
                        int pos = positions[i];
                        int r = pos / 3;
                        int row = pos % 3;

                        int potTypeIdx = _config.FastStampedePotTypeWeights.Sample(rng);
                        int symbolId = 10 + Math.Clamp(potTypeIdx, 0, 3);
                        spinResult.ScreenSymbols[r][row] = symbolId;
                    }
                }
            }

            // 4. Evaluate Payline Wins
            EvaluateLineWins(spinResult);

            // 5. Evaluate Jackpot Trigger Collections
            EvaluateCollections(spinResult, rng, chosenReelsetIndex);

            // 6. Evaluate Pot triggers & progress
            EvaluatePots(spinResult, rng);

            return spinResult;
        }

        public SpinResult FreeSpin(IRng rng, int currentFreeSpinIndex, int totalFreeSpins)
        {
            string reelsetName = "Reelset19";
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

            EvaluateCollections(spinResult, rng);

            return spinResult;
        }

        private void EvaluateLineWins(SpinResult spinResult)
        {
            var fastPaytable = _config.FastPaytable;
            var wildId = _config.WildSymbolId;
            var paylines = _config.Paylines;
            var symbols = _config.Symbols;

            for (int lineId = 0; lineId < paylines.Length; lineId++)
            {
                var payline = paylines[lineId];
                long maxPayout = 0;
                int bestSymId = -1;
                int bestMatchCount = 0;

                for (int s = 0; s < symbols.Count; s++)
                {
                    var sym = symbols[s];
                    if (sym.IsWild || sym.IsScatter || sym.Id >= 9) continue;

                    int matchCount = 0;
                    for (int reel = 0; reel < 5; reel++)
                    {
                        int rowIndex = payline[reel];
                        int screenSym = spinResult.ScreenSymbols[reel][rowIndex];

                        if (screenSym == sym.Id || screenSym == wildId)
                        {
                            matchCount++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    long payout = fastPaytable[sym.Id][matchCount];
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
                        LineId = lineId + 1,
                        SymbolId = bestSymId,
                        MatchCount = bestMatchCount,
                        Payout = maxPayout
                    });
                    spinResult.TotalWin += maxPayout;
                }
            }
        }

        private void EvaluateCollections(SpinResult spinResult, IRng rng, int reelsetIndex = 0)
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

            // 2. Count Fire Core symbols on the screen
            int triggerCount = 0;
            for (int r = 0; r < 5; r++)
            {
                for (int row = 0; row < 3; row++)
                {
                    if (spinResult.ScreenSymbols[r][row] == _config.FireCoreSymbolId)
                    {
                        triggerCount++;
                    }
                }
            }

            if (triggerCount > 0)
            {
                bool isSpecialReelset = (reelsetIndex == 8 || reelsetIndex == 9 || reelsetIndex == 10);

                double[] cashValues = isSpecialReelset
                    ? (_config.FireCoreCashValuesSpecial.Length > 0 ? _config.FireCoreCashValuesSpecial : _config.FireCoreCashValues)
                    : (_config.FireCoreCashValuesDefault.Length > 0 ? _config.FireCoreCashValuesDefault : _config.FireCoreCashValues);

                WeightTable cashTable = isSpecialReelset
                    ? (_config.FastFireCoreCashSpecial.TotalWeight > 0 ? _config.FastFireCoreCashSpecial : _config.FastFireCoreCashDefault)
                    : (_config.FastFireCoreCashDefault.TotalWeight > 0 ? _config.FastFireCoreCashDefault : _config.FastFireCoreCashSpecial);

                double sumMultipliers = 0.0;
                for (int i = 0; i < triggerCount; i++)
                {
                    if (cashValues.Length > 0 && cashTable.TotalWeight > 0)
                    {
                        int chosenValIndex = cashTable.Sample(rng);
                        sumMultipliers += cashValues[chosenValIndex];
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
                    int jackpotBonusTriggerWeight = _config.JackpotBonusTriggerChanceWeight;
                    if (jackpotBonusTriggerWeight > 0)
                    {
                        if (rng.Next(jackpotBonusTriggerWeight) < triggerCount)
                        {
                            spinResult.JackpotBonusTriggered = true;
                            
                            if (_config.JackpotNames.Length > 0 && _config.FastJackpotWeights.TotalWeight > 0)
                            {
                                int chosenJackpotIndex = _config.FastJackpotWeights.Sample(rng);
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

        private void EvaluatePots(SpinResult spinResult, IRng rng)
        {
            spinResult.PotPowersBefore = (int[])_potPowers.Clone();

            for (int p = 0; p < 4; p++)
            {
                int symbolId = 10 + p;
                int count = 0;
                for (int r = 0; r < 5; r++)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        if (spinResult.ScreenSymbols[r][row] == symbolId)
                        {
                            count++;
                        }
                    }
                }

                if (count > 0)
                {
                    if (p == 0)
                    {
                        // Pot 1: Lock & Slingo Trigger
                        int maxPower = _config.LockSlingoSpins.Length - 1;
                        int currentPower = Math.Min(maxPower, _potPowers[0]);
                        int chanceWeight = _config.LockSlingoTriggerWeights[currentPower];
                        if (rng.Next(chanceWeight) < count)
                        {
                            int triggeredPower = Math.Min(maxPower, currentPower + (count - 1));
                            long bonusWin = RunLockSlingoBonus(triggeredPower, rng, out int completedSlingos, out double cashSum, out double ladderPrize, out bool minWinApplied);
                            
                            spinResult.TriggeredPotBonuses.Add(new TriggeredPotBonus
                            {
                                PotIndex = 0,
                                BonusName = "Lock & Slingo",
                                Power = triggeredPower,
                                Win = bonusWin,
                                CompletedSlingos = completedSlingos,
                                CashValuesSum = cashSum,
                                LadderPrize = ladderPrize,
                                MinWinApplied = minWinApplied
                            });

                            spinResult.FeatureWin += bonusWin;
                            spinResult.TotalWin += bonusWin;

                            _potPowers[0] = 0; // reset
                        }
                        else
                        {
                            _potPowers[0] = Math.Min(maxPower, currentPower + count);
                        }
                    }
                    else if (p == 1)
                    {
                        // Pot 2: Apex Spins Trigger
                        int maxPower = _config.ApexSpinsTopAwardMultipliers.Length - 1;
                        int currentPower = Math.Min(maxPower, _potPowers[1]);
                        int chanceWeight = _config.ApexSpinsTriggerWeights[currentPower];
                        if (rng.Next(chanceWeight) < count)
                        {
                            int triggeredPower = Math.Min(maxPower, currentPower + (count - 1));
                            long bonusWin = RunApexSpinsBonus(triggeredPower, rng, out int spinsPlayed, out bool minWinApplied);

                            spinResult.TriggeredPotBonuses.Add(new TriggeredPotBonus
                            {
                                PotIndex = 1,
                                BonusName = "Apex Spins",
                                Power = triggeredPower,
                                Win = bonusWin,
                                SpinsPlayed = spinsPlayed,
                                MinWinApplied = minWinApplied
                            });

                            spinResult.FeatureWin += bonusWin;
                            spinResult.TotalWin += bonusWin;

                            _potPowers[1] = 0; // reset
                        }
                        else
                        {
                            _potPowers[1] = Math.Min(maxPower, currentPower + count);
                        }
                    }
                    else if (p == 2)
                    {
                        // Pot 3: Colossal Spins Trigger
                        int maxPower = _config.ColossalSpinsCounts.Length - 1;
                        int currentPower = Math.Min(maxPower, _potPowers[2]);
                        int chanceWeight = _config.ColossalSpinsTriggerWeights[currentPower];
                        if (rng.Next(chanceWeight) < count)
                        {
                            int triggeredPower = Math.Min(maxPower, currentPower + (count - 1));
                            long bonusWin = RunColossalSpinsBonus(triggeredPower, rng, out int spinsPlayed, out bool minWinApplied, out var symbolWins, out var symbolHits);

                            spinResult.TriggeredPotBonuses.Add(new TriggeredPotBonus
                            {
                                PotIndex = 2,
                                BonusName = "Colossal Spins",
                                Power = triggeredPower,
                                Win = bonusWin,
                                SpinsPlayed = spinsPlayed,
                                MinWinApplied = minWinApplied,
                                ColossalSymbolWins = symbolWins,
                                ColossalSymbolHits = symbolHits
                            });

                            spinResult.FeatureWin += bonusWin;
                            spinResult.TotalWin += bonusWin;

                            _potPowers[2] = 0; // reset
                        }
                        else
                        {
                            _potPowers[2] = Math.Min(maxPower, currentPower + count);
                        }
                    }
                    else if (p == 3)
                    {
                        // Pot 4: Primal Zone Bonus (Gorilla) Trigger
                        int maxPower = _config.PrimalZoneTriggerWeights.Length > 0 ? _config.PrimalZoneTriggerWeights.Length - 1 : 0;
                        int currentPower = Math.Min(maxPower, _potPowers[3]);
                        int chanceWeight = (currentPower >= 0 && currentPower < _config.PrimalZoneTriggerWeights.Length) ? _config.PrimalZoneTriggerWeights[currentPower] : 0;
                        if (chanceWeight > 0 && rng.Next(chanceWeight) < count)
                        {
                            int triggeredPower = Math.Min(maxPower, currentPower + (count - 1));
                            long bonusWin = RunPrimalZoneBonus(triggeredPower, rng, out int totalBananas, out int finalStage, out int finalSize, out bool minWinApplied);

                            spinResult.TriggeredPotBonuses.Add(new TriggeredPotBonus
                            {
                                PotIndex = 3,
                                BonusName = "Primal Zone Bonus",
                                Power = triggeredPower,
                                Win = bonusWin,
                                SpinsPlayed = _config.PrimalZoneSpins.Length > triggeredPower ? _config.PrimalZoneSpins[triggeredPower] : 5,
                                BananasCollected = totalBananas,
                                FinalPrimalZoneStage = finalStage,
                                FinalPrimalZoneSize = finalSize,
                                MinWinApplied = minWinApplied
                            });

                            spinResult.FeatureWin += bonusWin;
                            spinResult.TotalWin += bonusWin;

                            _potPowers[3] = 0; // reset
                        }
                        else
                        {
                            _potPowers[3] = Math.Min(maxPower, currentPower + count);
                        }
                    }
                }
            }

            spinResult.PotPowersAfter = (int[])_potPowers.Clone();
        }

        private long RunApexSpinsBonus(int powerLevel, IRng rng, out int spinsPlayed, out bool minWinApplied)
        {
            return _apexSpinsFeature.Run(powerLevel, _stageIndex, rng, EvaluateGridLineWins, out spinsPlayed, out minWinApplied);
        }

        private long RunColossalSpinsBonus(int powerLevel, IRng rng, out int spinsPlayed, out bool minWinApplied, out Dictionary<int, long> colossalSymbolWins, out Dictionary<int, int> colossalSymbolHits)
        {
            return _colossalSpinsFeature.Run(powerLevel, _stageIndex, rng, EvaluateGridLineWins, out spinsPlayed, out minWinApplied, out colossalSymbolWins, out colossalSymbolHits);
        }

        private long RunLockSlingoBonus(int powerLevel, IRng rng, out int completedSlingos, out double cashValuesSum, out double ladderPrize, out bool minWinApplied)
        {
            return _lockSlingoFeature.Run(powerLevel, _stageIndex, rng, out completedSlingos, out cashValuesSum, out ladderPrize, out minWinApplied);
        }

        private long RunPrimalZoneBonus(int powerLevel, IRng rng, out int totalBananasCollected, out int finalStage, out int finalSize, out bool minWinApplied)
        {
            return _primalZoneFeature.Run(powerLevel, _stageIndex, rng, out totalBananasCollected, out finalStage, out finalSize, out minWinApplied);
        }

        private long EvaluateGridLineWins(int[][] screenSymbols)
        {
            var fastPaytable = _config.FastPaytable;
            var wildId = _config.WildSymbolId;
            var paylines = _config.Paylines;
            var symbols = _config.Symbols;
            long totalWin = 0;

            for (int lineId = 0; lineId < paylines.Length; lineId++)
            {
                var payline = paylines[lineId];
                long maxPayout = 0;

                for (int s = 0; s < symbols.Count; s++)
                {
                    var sym = symbols[s];
                    if (sym.IsWild || sym.IsScatter || sym.Id >= 9) continue;

                    int matchCount = 0;
                    for (int reel = 0; reel < 5; reel++)
                    {
                        int rowIndex = payline[reel];
                        int screenSym = screenSymbols[reel][rowIndex];

                        if (screenSym == sym.Id || screenSym == wildId)
                        {
                            matchCount++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    long payout = fastPaytable[sym.Id][matchCount];
                    if (payout > maxPayout)
                    {
                        maxPayout = payout;
                    }
                }

                totalWin += maxPayout;
            }
            return totalWin;
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
}
