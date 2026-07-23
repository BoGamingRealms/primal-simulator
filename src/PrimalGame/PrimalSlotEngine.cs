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
        private readonly int[] _potPowers = new int[4] { 0, 0, 0, 0 };

        public int[] PotPowers => _potPowers;

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
                else
                {
                    _potPowers[p] = _config.MaxBonusPower;
                }
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

            // 6. Evaluate Pot triggers & progress
            EvaluatePots(spinResult, rng);

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
                // Draw a cash value for each landed trigger
                double sumMultipliers = 0.0;
                for (int i = 0; i < triggerCount; i++)
                {
                    if (_config.FireCoreCashValues.Length > 0 && _config.FireCoreCashWeights.Length > 0)
                    {
                        int chosenValIndex = ChooseWeightedIndex(_config.FireCoreCashWeights, rng);
                        sumMultipliers += _config.FireCoreCashValues[chosenValIndex];
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

        private void EvaluatePots(SpinResult spinResult, IRng rng)
        {
            spinResult.PotPowersBefore = _potPowers.ToArray();

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
                            // Triggered! Power increases by N - 1
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
                            // Not triggered, increase power by count
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
                            // Triggered! Power increases by N - 1
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
                            // Not triggered, increase power by count
                            _potPowers[1] = Math.Min(maxPower, currentPower + count);
                        }
                    }
                    else
                    {
                        // Other pots: placeholder trigger
                        int chanceWeight = _config.PotTriggerChanceWeights[p];
                        if (rng.Next(chanceWeight) < count)
                        {
                            // Triggered!
                            int triggeredPower = _potPowers[p] + (count - 1);
                            spinResult.TriggeredPotBonuses.Add(new TriggeredPotBonus
                            {
                                PotIndex = p,
                                Power = triggeredPower,
                                Win = 0,
                                CompletedSlingos = 0,
                                CashValuesSum = 0,
                                LadderPrize = 0,
                                MinWinApplied = false
                            });
                            
                            _potPowers[p] = 0; // reset
                        }
                        else
                        {
                            // Not triggered, increase power
                            _potPowers[p] += count;
                        }
                    }
                }
            }

            spinResult.PotPowersAfter = _potPowers.ToArray();
        }

        private long RunApexSpinsBonus(int powerLevel, IRng rng, out int spinsPlayed, out bool minWinApplied)
        {
            double topSpinAwardMultiplier = _config.ApexSpinsTopAwardMultipliers[powerLevel];
            long topSpinAwardInCents = (long)Math.Round(topSpinAwardMultiplier * 100.0);

            bool[,] lockedWilds = new bool[5, 3];
            spinsPlayed = 0;
            long totalBonusWinInCents = 0;

            while (true)
            {
                spinsPlayed++;

                // Pick a reelset based on weights
                int chosenIdx = ChooseWeightedIndex(_config.ApexSpinsReelsetWeights, rng);
                string reelsetName = $"Reelset{chosenIdx}";

                if (!_config.ApexSpinsReelsets.TryGetValue(reelsetName, out var reelset))
                {
                    reelset = _config.ApexSpinsReelsets.Values.FirstOrDefault() ?? _config.BaseReels;
                }

                int[][] screenSymbols = new int[5][];
                for (int r = 0; r < 5; r++)
                {
                    screenSymbols[r] = new int[3];
                    var strip = reelset.Reels[r];
                    int stopIndex = rng.Next(strip.Length);
                    screenSymbols[r][0] = reelset.GetSymbolAt(r, stopIndex, 0);
                    screenSymbols[r][1] = reelset.GetSymbolAt(r, stopIndex, 1);
                    screenSymbols[r][2] = reelset.GetSymbolAt(r, stopIndex, 2);
                }

                // Apply locked Wilds & lock any new Wilds
                for (int r = 0; r < 5; r++)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        if (lockedWilds[r, row])
                        {
                            screenSymbols[r][row] = _config.WildSymbolId;
                        }
                        else if (screenSymbols[r][row] == _config.WildSymbolId)
                        {
                            lockedWilds[r, row] = true;
                        }
                    }
                }

                // Evaluate line wins for this spin
                long singleSpinWin = EvaluateGridLineWins(screenSymbols);
                totalBonusWinInCents += singleSpinWin;

                // Stop condition: single spin win >= top spin award
                if (singleSpinWin >= topSpinAwardInCents)
                {
                    break;
                }
            }

            // Guaranteed Bonus Minimum if stage >= 5
            minWinApplied = false;
            if (_stageIndex >= 5 && _config.ApexSpinsBonusMinimums.Length > powerLevel)
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

        private long EvaluateGridLineWins(int[][] screenSymbols)
        {
            long totalWin = 0;
            for (int lineId = 0; lineId < _config.Paylines.Length; lineId++)
            {
                var payline = _config.Paylines[lineId];
                long maxPayout = 0;

                foreach (var sym in _config.Symbols)
                {
                    if (sym.IsWild || sym.IsScatter || sym.Id >= 9) continue;

                    int matchCount = 0;
                    for (int reel = 0; reel < 5; reel++)
                    {
                        int rowIndex = payline[reel];
                        int screenSym = screenSymbols[reel][rowIndex];

                        if (screenSym == sym.Id || screenSym == _config.WildSymbolId)
                        {
                            matchCount++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    long payout = _config.FastPaytable[sym.Id][matchCount];
                    if (payout > maxPayout)
                    {
                        maxPayout = payout;
                    }
                }

                totalWin += maxPayout;
            }
            return totalWin;
        }

        private long RunLockSlingoBonus(int powerLevel, IRng rng, out int completedSlingos, out double cashValuesSum, out double ladderPrize, out bool minWinApplied)
        {
            int totalSpins = _config.LockSlingoSpins[powerLevel];
            bool[] gridLocked = new bool[25];
            double[] gridValues = new double[25];

            for (int spin = 0; spin < totalSpins; spin++)
            {
                int emptyCount = 0;
                for (int i = 0; i < 25; i++) if (!gridLocked[i]) emptyCount++;

                if (emptyCount == 0) break; // Optimization: all spaces locked

                // Find the landing weights for this emptyCount
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
                // rolledIndex: 0 = 3 Fire Cores, 1 = 2 Fire Cores, 2 = 1 Fire Core, 3 = Blank
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
                    // Place coresToLand Fire Cores randomly in remaining empty spaces
                    // Gather empty positions
                    var emptyPositions = new List<int>();
                    for (int i = 0; i < 25; i++)
                    {
                        if (!gridLocked[i]) emptyPositions.Add(i);
                    }

                    // Randomly select coresToLand positions
                    for (int c = 0; c < coresToLand; c++)
                    {
                        int idx = rng.Next(emptyPositions.Count);
                        int pos = emptyPositions[idx];
                        emptyPositions.RemoveAt(idx);

                        gridLocked[pos] = true;
                        // Draw cash value
                        int chosenValIndex = ChooseWeightedIndex(_config.LockSlingoFireCoreWeights, rng);
                        double val = _config.LockSlingoFireCoreValues[chosenValIndex];
                        gridValues[pos] = val;
                    }
                }
            }

            // Calculate winnings
            cashValuesSum = gridValues.Sum();

            // Evaluate completed Slingo lines
            completedSlingos = CountSlingos(gridLocked);

            // Determine Slingo ladder prize
            ladderPrize = GetSlingoLadderPrize(completedSlingos);

            double totalBonusMultiplier = cashValuesSum + ladderPrize;

            // Apply Guaranteed Bonus Minimum if baseGameStageIndex >= 5
            minWinApplied = false;
            if (_stageIndex >= 5)
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

        private int CountSlingos(bool[] gridLocked)
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
}
