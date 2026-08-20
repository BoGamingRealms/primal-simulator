using System;
using System.Collections.Generic;
using System.Linq;
using SlotFramework.Interfaces;
using SlotFramework.Models;
using SlotFramework.Utilities;
using CashVortexGame.Config;

namespace CashVortexGame;

public enum SymbolType
{
    CentralWildStar,
    Blank,
    CashCoin,
    JackpotCoin,
    MiniVortex,
    MegaVortex,
    UltraVortex,
    MiniStrike,
    MegaStrike,
    UltraStrike,
    XWheel
}

public class GridCell
{
    public int Row { get; set; }
    public int Col { get; set; }
    public SymbolType Type { get; set; }
    public double CashValue { get; set; }
    public string? JackpotType { get; set; }
    public int LifeRemaining { get; set; } // 3, 2, 1, or 0
    public bool WonThisSpin { get; set; }
    public bool JustLanded { get; set; }
}

public class CashVortexSlotEngine : ISlotEngine
{
    private readonly CashVortexConfig _config;
    private readonly GridCell[,] _grid = new GridCell[5, 5];
    private int _spinsInCurrentStage = 0;

    // 12 Slingo Lines (indices 0..24, row-major):
    // 5 Horizontal, 5 Vertical, 2 Diagonal
    private readonly List<int[]> _slingoLines = new()
    {
        // 5 Horizontal Lines
        new[] { 0, 1, 2, 3, 4 },
        new[] { 5, 6, 7, 8, 9 },
        new[] { 10, 11, 12, 13, 14 }, // Row 2 (center)
        new[] { 15, 16, 17, 18, 19 },
        new[] { 20, 21, 22, 23, 24 },

        // 5 Vertical Lines
        new[] { 0, 5, 10, 15, 20 },
        new[] { 1, 6, 11, 16, 21 },
        new[] { 2, 7, 12, 17, 22 }, // Col 2 (center)
        new[] { 3, 8, 13, 18, 23 },
        new[] { 4, 9, 14, 19, 24 },

        // 2 Diagonal Lines
        new[] { 0, 6, 12, 18, 24 },  // Main diagonal (center)
        new[] { 4, 8, 12, 16, 20 }   // Anti diagonal (center)
    };

    public GridCell[,] Grid => _grid;
    public int CurrentStage => 1;
    public int CurrentCycleSpin => _spinsInCurrentStage;
    public int TotalCollectedCount => 0;

    public CashVortexSlotEngine(CashVortexConfig config)
    {
        _config = config;
        InitializeGrid();
    }

    private void InitializeGrid()
    {
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                _grid[r, c] = new GridCell
                {
                    Row = r,
                    Col = c,
                    Type = (r == 2 && c == 2) ? SymbolType.CentralWildStar : SymbolType.Blank,
                    CashValue = 0.0,
                    LifeRemaining = (r == 2 && c == 2) ? int.MaxValue : 0
                };
            }
        }
    }

    public void Reset()
    {
        _spinsInCurrentStage = 0;
        InitializeGrid();
    }

    public SpinResult Spin(IRng rng)
    {
        _spinsInCurrentStage++;

        var spinResult = new SpinResult
        {
            StopIndexes = new int[5],
            ScreenSymbols = new int[5][]
        };

        // Step A: Prepare Grid for New Spin
        PrepareGridForNewSpin();

        // Step B: Select Active Table (0, 1, or 2)
        int tableIndex = _config.TableSelectionWeights.Sample(rng);

        // Step C: Decide Special Symbol Landing
        var specialChanceWeights = _config.SpecialSymbolChanceWeights[tableIndex];
        int specialRoll = specialChanceWeights.Sample(rng); // 0 = Special Symbol, 1 = No Special Symbol

        bool specialSymbolLanded = false;
        List<GridCell> newlyLandedCells = new();

        var emptyPositions = GetEmptyPositions();

        if (specialRoll == 0 && emptyPositions.Count > 0)
        {
            specialSymbolLanded = true;
            int specialTypeIdx = _config.SpecialSymbolTypeWeights.Sample(rng);
            int posIdx = rng.Next(emptyPositions.Count);
            var targetPos = emptyPositions[posIdx];
            emptyPositions.RemoveAt(posIdx);

            var cell = _grid[targetPos.r, targetPos.c];
            cell.JustLanded = true;
            cell.LifeRemaining = 3;

            switch (specialTypeIdx)
            {
                case 0: // Jackpot Coin
                    cell.Type = SymbolType.JackpotCoin;
                    int jpIdx = _config.JackpotTypeWeights.Sample(rng);
                    var jpDef = _config.JackpotCoins[jpIdx];
                    cell.JackpotType = jpDef.JackpotName;
                    cell.CashValue = jpDef.Multiplier;
                    break;
                case 1: // Mini Vortex
                    cell.Type = SymbolType.MiniVortex;
                    cell.CashValue = 0.0;
                    break;
                case 2: // Mega Vortex
                    cell.Type = SymbolType.MegaVortex;
                    cell.CashValue = 0.0;
                    break;
                case 3: // Ultra Vortex
                    cell.Type = SymbolType.UltraVortex;
                    cell.CashValue = 0.0;
                    break;
                case 4: // Mini Strike
                    cell.Type = SymbolType.MiniStrike;
                    cell.CashValue = SampleCashStrikeValue(rng);
                    break;
                case 5: // Mega Strike
                    cell.Type = SymbolType.MegaStrike;
                    cell.CashValue = SampleCashStrikeValue(rng);
                    break;
                case 6: // Ultra Strike
                    cell.Type = SymbolType.UltraStrike;
                    cell.CashValue = SampleCashStrikeValue(rng);
                    break;
                case 7: // X Wheel
                    cell.Type = SymbolType.XWheel;
                    cell.CashValue = 1.0;
                    break;
            }
            newlyLandedCells.Add(cell);
        }

        // Step D: Fill Remaining Empty Positions with Cash Coins or Blanks
        var coinChanceWeights = _config.CashCoinChanceWeights[tableIndex];
        int cashCoinsLandedCount = 0;

        foreach (var pos in emptyPositions)
        {
            int outcome = coinChanceWeights.Sample(rng); // 0 = Cash Coin, 1 = Blank
            var cell = _grid[pos.r, pos.c];

            if (outcome == 0)
            {
                cell.Type = SymbolType.CashCoin;
                cell.CashValue = SampleCashCoinValue(rng);
                cell.LifeRemaining = 3;
                cell.JustLanded = true;
                newlyLandedCells.Add(cell);
                cashCoinsLandedCount++;
            }
            else
            {
                cell.Type = SymbolType.Blank;
                cell.CashValue = 0.0;
                cell.LifeRemaining = 0;
            }
        }

        // Edge Case: Guaranteed 1 Coin if 0 special symbols and 0 cash coins landed
        if (!specialSymbolLanded && cashCoinsLandedCount == 0 && emptyPositions.Count > 0)
        {
            int forcedIdx = rng.Next(emptyPositions.Count);
            var pos = emptyPositions[forcedIdx];
            var cell = _grid[pos.r, pos.c];
            cell.Type = SymbolType.CashCoin;
            cell.CashValue = SampleCashCoinValue(rng);
            cell.LifeRemaining = 3;
            cell.JustLanded = true;
            newlyLandedCells.Add(cell);
        }

        // Step E: Execute Special Symbol Landing Actions (Strikes then Vortexes)
        ExecuteSpecialSymbolActions(newlyLandedCells);

        // Step F: Execute X Wheel Feature if X Symbol Landed
        RunXWheelFeature(rng, spinResult, newlyLandedCells);

        // Step G: Apply Symbol Life Cycle Reset for Line-Sharing Existing Symbols
        ApplyLifeCycleResets(newlyLandedCells);

        // Step H: Evaluate 12 Slingo Lines & Center Wild Wheel Bonus
        EvaluateSlingoLines(rng, spinResult);

        // Populate ScreenSymbols matrix for visualization / compatibility
        for (int r = 0; r < 5; r++)
        {
            spinResult.ScreenSymbols[r] = new int[5];
            for (int c = 0; c < 5; c++)
            {
                spinResult.ScreenSymbols[r][c] = (int)_grid[r, c].Type;
            }
        }

        return spinResult;
    }

    public SpinResult FreeSpin(IRng rng, int currentFreeSpinIndex, int totalFreeSpins)
    {
        return Spin(rng);
    }

    private void PrepareGridForNewSpin()
    {
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                if (r == 2 && c == 2) continue; // Central Wild Star

                var cell = _grid[r, c];
                cell.JustLanded = false;

                if (cell.WonThisSpin || cell.LifeRemaining <= 1)
                {
                    cell.Type = SymbolType.Blank;
                    cell.CashValue = 0.0;
                    cell.LifeRemaining = 0;
                    cell.JackpotType = null;
                    cell.WonThisSpin = false;
                }
                else
                {
                    cell.LifeRemaining--;
                }
            }
        }
    }

    private List<(int r, int c)> GetEmptyPositions()
    {
        var empty = new List<(int r, int c)>();
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                if (r == 2 && c == 2) continue;
                if (_grid[r, c].Type == SymbolType.Blank)
                {
                    empty.Add((r, c));
                }
            }
        }
        return empty;
    }

    private double SampleCashStrikeValue(IRng rng)
    {
        int idx = _config.CashStrikeValueWeights.Sample(rng);
        return _config.CashStrikeValues[idx].Multiplier;
    }

    private double SampleCashCoinValue(IRng rng)
    {
        int idx = _config.CashCoinValueWeights.Sample(rng);
        return _config.CashCoinValues[idx].Multiplier;
    }

    private void ExecuteSpecialSymbolActions(List<GridCell> newlyLanded)
    {
        // 1. Process Cash Strikes first (distribute cash boost)
        foreach (var cell in newlyLanded)
        {
            if (cell.Type == SymbolType.MiniStrike)
            {
                var targets = GetOrthogonalNeighbors(cell.Row, cell.Col);
                foreach (var t in targets)
                {
                    if (IsValuableTarget(t.Type))
                    {
                        t.CashValue += cell.CashValue;
                    }
                }
            }
            else if (cell.Type == SymbolType.MegaStrike)
            {
                var targets = GetSameLineCells(cell.Row, cell.Col);
                foreach (var t in targets)
                {
                    if (t != cell && IsValuableTarget(t.Type))
                    {
                        t.CashValue += cell.CashValue;
                    }
                }
            }
            else if (cell.Type == SymbolType.UltraStrike)
            {
                for (int r = 0; r < 5; r++)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        var t = _grid[r, c];
                        if (t != cell && IsValuableTarget(t.Type))
                        {
                            t.CashValue += cell.CashValue;
                        }
                    }
                }
            }
        }

        // 2. Process Cash Vortexes second (gather cash values)
        foreach (var cell in newlyLanded)
        {
            if (cell.Type == SymbolType.MiniVortex)
            {
                var targets = GetOrthogonalNeighbors(cell.Row, cell.Col);
                double sum = 0.0;
                foreach (var t in targets)
                {
                    if (IsValuableTarget(t.Type))
                    {
                        sum += t.CashValue;
                    }
                }
                cell.CashValue = sum;
            }
            else if (cell.Type == SymbolType.MegaVortex)
            {
                var targets = GetSameLineCells(cell.Row, cell.Col);
                double sum = 0.0;
                foreach (var t in targets)
                {
                    if (t != cell && IsValuableTarget(t.Type))
                    {
                        sum += t.CashValue;
                    }
                }
                cell.CashValue = sum;
            }
            else if (cell.Type == SymbolType.UltraVortex)
            {
                double sum = 0.0;
                for (int r = 0; r < 5; r++)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        var t = _grid[r, c];
                        if (t != cell && IsValuableTarget(t.Type))
                        {
                            sum += t.CashValue;
                        }
                    }
                }
                cell.CashValue = sum;
            }
        }
    }

    private static bool IsValuableTarget(SymbolType type)
    {
        return type == SymbolType.CashCoin ||
               type == SymbolType.MiniVortex ||
               type == SymbolType.MegaVortex ||
               type == SymbolType.UltraVortex ||
               type == SymbolType.XWheel ||
               type == SymbolType.MiniStrike ||
               type == SymbolType.MegaStrike ||
               type == SymbolType.UltraStrike;
    }

    private List<GridCell> GetOrthogonalNeighbors(int row, int col)
    {
        var neighbors = new List<GridCell>();
        int[] dr = { -1, 1, 0, 0 };
        int[] dc = { 0, 0, -1, 1 };

        for (int i = 0; i < 4; i++)
        {
            int nr = row + dr[i];
            int nc = col + dc[i];
            if (nr >= 0 && nr < 5 && nc >= 0 && nc < 5)
            {
                neighbors.Add(_grid[nr, nc]);
            }
        }
        return neighbors;
    }

    private List<GridCell> GetSameLineCells(int row, int col)
    {
        var cellIndex = row * 5 + col;
        var matchingCells = new HashSet<GridCell>();

        foreach (var line in _slingoLines)
        {
            if (line.Contains(cellIndex))
            {
                foreach (var idx in line)
                {
                    int r = idx / 5;
                    int c = idx % 5;
                    matchingCells.Add(_grid[r, c]);
                }
            }
        }
        return matchingCells.ToList();
    }

    private void ApplyLifeCycleResets(List<GridCell> newlyLanded)
    {
        foreach (var cell in newlyLanded)
        {
            var lineCells = GetSameLineCells(cell.Row, cell.Col);
            foreach (var existing in lineCells)
            {
                if (existing.Type != SymbolType.Blank && existing.Type != SymbolType.CentralWildStar)
                {
                    existing.LifeRemaining = 3;
                }
            }
        }
    }

    private void EvaluateSlingoLines(IRng rng, SpinResult spinResult)
    {
        int completedLinesCount = 0;
        bool triggeredCenterWildWheelBonus = false;

        for (int lineId = 0; lineId < _slingoLines.Count; lineId++)
        {
            var line = _slingoLines[lineId];
            bool lineComplete = true;
            double lineCashSum = 0.0;
            bool passesThroughCenter = line.Contains(12);

            foreach (var idx in line)
            {
                int r = idx / 5;
                int c = idx % 5;
                var cell = _grid[r, c];

                if (cell.Type == SymbolType.Blank)
                {
                    lineComplete = false;
                    break;
                }
                lineCashSum += cell.CashValue;
            }

            if (lineComplete)
            {
                completedLinesCount++;
                long linePayout = (long)Math.Round(lineCashSum * 100);

                spinResult.LineWins.Add(new LineWin
                {
                    LineId = lineId + 1,
                    Payout = linePayout
                });
                spinResult.TotalWin += linePayout;

                if (passesThroughCenter)
                {
                    triggeredCenterWildWheelBonus = true;
                }

                // Mark non-central symbols on winning line to be removed at start of next spin
                foreach (var idx in line)
                {
                    int r = idx / 5;
                    int c = idx % 5;
                    if (r != 2 || c != 2)
                    {
                        _grid[r, c].WonThisSpin = true;
                    }
                }
            }
        }

        // Multiple lines going through center wild in a single spin only trigger the Wheel Bonus once
        if (triggeredCenterWildWheelBonus)
        {
            RunCenterWildWheelBonus(rng, spinResult);
        }
    }

    private void RunCenterWildWheelBonus(IRng rng, SpinResult spinResult)
    {
        int currentWheel = 1;

        while (currentWheel <= 3)
        {
            var weightTable = currentWheel switch
            {
                1 => _config.MiniWheelWeightTable,
                2 => _config.MegaWheelWeightTable,
                3 => _config.UltraWheelWeightTable,
                _ => _config.UltraWheelWeightTable
            };

            var prizeList = currentWheel switch
            {
                1 => _config.MiniWheelPrizes,
                2 => _config.MegaWheelPrizes,
                3 => _config.UltraWheelPrizes,
                _ => _config.UltraWheelPrizes
            };

            if (weightTable == null || prizeList == null || prizeList.Count == 0) break;

            int idx = weightTable.Sample(rng);
            var prize = prizeList[idx];

            if (prize.Type == WheelPrizeType.Upgrade)
            {
                spinResult.TriggeredPotBonuses.Add(new TriggeredPotBonus
                {
                    PotIndex = 1,
                    BonusName = $"CenterWheel:W{currentWheel}:Upgrade",
                    Win = 0
                });

                currentWheel++;
                if (currentWheel > 3) currentWheel = 3;
                continue;
            }

            long prizeWinCents = 0;

            switch (prize.Type)
            {
                case WheelPrizeType.Multiplier:
                    double mult = prize.ParameterValue > 0 ? prize.ParameterValue : 2.0;
                    for (int r = 0; r < 5; r++)
                    {
                        for (int c = 0; c < 5; c++)
                        {
                            var cell = _grid[r, c];
                            if (cell.Type != SymbolType.Blank && cell.Type != SymbolType.CentralWildStar && cell.Type != SymbolType.JackpotCoin)
                            {
                                cell.CashValue *= mult;
                            }
                        }
                    }
                    break;

                case WheelPrizeType.UltraStrike:
                    double strike = prize.ParameterValue;
                    for (int r = 0; r < 5; r++)
                    {
                        for (int c = 0; c < 5; c++)
                        {
                            var cell = _grid[r, c];
                            if (cell.Type != SymbolType.Blank && cell.Type != SymbolType.CentralWildStar && cell.Type != SymbolType.JackpotCoin)
                            {
                                cell.CashValue += strike;
                            }
                        }
                    }
                    break;

                case WheelPrizeType.Jackpot:
                    double jpMult = 5.0;
                    if (!string.IsNullOrEmpty(prize.JackpotType))
                    {
                        var match = _config.JackpotCoins.FirstOrDefault(j => j.JackpotName.Equals(prize.JackpotType, StringComparison.OrdinalIgnoreCase));
                        if (match != null) jpMult = match.Multiplier;
                        else if (prize.JackpotType.Contains("Mega", StringComparison.OrdinalIgnoreCase)) jpMult = 50.0;
                        else if (prize.JackpotType.Contains("Ultra", StringComparison.OrdinalIgnoreCase)) jpMult = 500.0;
                    }
                    prizeWinCents = (long)Math.Round(jpMult * 100);
                    spinResult.TotalWin += prizeWinCents;
                    break;

                case WheelPrizeType.LockAndSlingo:
                    PlayLockAndSlingoBonus(rng, spinResult);
                    break;
            }

            spinResult.TriggeredPotBonuses.Add(new TriggeredPotBonus
            {
                PotIndex = 1,
                BonusName = $"CenterWheel:W{currentWheel}:{prize.PrizeString}",
                Win = prizeWinCents
            });

            break;
        }
    }

    public void PlayLockAndSlingoBonus(IRng rng, SpinResult spinResult)
    {
        // 5x5 grid without central wild star (starts empty with 25 positions)
        var bonusGrid = new GridCell[5, 5];
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                bonusGrid[r, c] = new GridCell
                {
                    Row = r,
                    Col = c,
                    Type = SymbolType.Blank,
                    CashValue = 0.0,
                    LifeRemaining = 0
                };
            }
        }

        int currentLives = 3;
        int bonusSpinsCount = 0;
        long bonusDirectJackpotWin = 0;

        while (currentLives > 0)
        {
            int emptyCount = GetBonusEmptyPositionsCount(bonusGrid);
            if (emptyCount == 0) // Full House
            {
                break;
            }

            bonusSpinsCount++;

            // Spaces left bucket (0: >20, 1: >15, 2: >10, 3: >5, 4: >0)
            int bucket = emptyCount > 20 ? 0 : (emptyCount > 15 ? 1 : (emptyCount > 10 ? 2 : (emptyCount > 5 ? 3 : 4)));

            int landingWeight = _config.BonusLandingWeightsByLifeAndBucket[currentLives, bucket];
            if (landingWeight <= 0) landingWeight = 50;

            bool lands = rng.Next(_config.BonusBaseFactor) < landingWeight;

            if (lands)
            {
                currentLives = 3; // Reset lives to 3

                var weightTable = (bucket < _config.BonusOutcomeWeightsByBucket.Length && _config.BonusOutcomeWeightsByBucket[bucket].TotalWeight > 0)
                    ? _config.BonusOutcomeWeightsByBucket[bucket]
                    : null;

                BonusOutcomeDef? outcomeDef = null;
                if (weightTable != null && _config.BonusOutcomeDefs.Count > 0)
                {
                    int outcomeIdx = weightTable.Sample(rng);
                    if (outcomeIdx >= 0 && outcomeIdx < _config.BonusOutcomeDefs.Count)
                    {
                        outcomeDef = _config.BonusOutcomeDefs[outcomeIdx];
                    }
                }

                var emptyPositions = GetBonusEmptyPositions(bonusGrid);
                var newlyLandedBonusCells = new List<GridCell>();

                if (outcomeDef != null && outcomeDef.Items.Count > 0)
                {
                    foreach (var item in outcomeDef.Items)
                    {
                        for (int i = 0; i < item.Count; i++)
                        {
                            if (emptyPositions.Count == 0) break;

                            int posIdx = rng.Next(emptyPositions.Count);
                            var pos = emptyPositions[posIdx];
                            emptyPositions.RemoveAt(posIdx);

                            var cell = bonusGrid[pos.r, pos.c];
                            cell.Type = item.Type;
                            cell.JustLanded = true;
                            cell.LifeRemaining = int.MaxValue; // Permanent lock in bonus

                            switch (item.Type)
                            {
                                case SymbolType.CashCoin:
                                    cell.CashValue = SampleBonusCashCoinValue(rng);
                                    break;

                                case SymbolType.JackpotCoin:
                                    int jpIdx = (_config.BonusJackpotWeights.TotalWeight > 0)
                                        ? _config.BonusJackpotWeights.Sample(rng)
                                        : rng.Next(Math.Max(1, _config.JackpotCoins.Count));
                                    var jpList = _config.BonusJackpotCoins.Count > 0 ? _config.BonusJackpotCoins : _config.JackpotCoins;
                                    var jpDef = jpList[Math.Min(jpIdx, jpList.Count - 1)];
                                    cell.JackpotType = jpDef.JackpotName;
                                    cell.CashValue = jpDef.Multiplier;
                                    break;

                                case SymbolType.MiniStrike:
                                    // Sample strike type (Mini, Mega, Ultra)
                                    int strTypeIdx = (_config.BonusCashStrikeTypeWeights.TotalWeight > 0)
                                        ? _config.BonusCashStrikeTypeWeights.Sample(rng)
                                        : 0;
                                    cell.Type = strTypeIdx switch
                                    {
                                        1 => SymbolType.MegaStrike,
                                        2 => SymbolType.UltraStrike,
                                        _ => SymbolType.MiniStrike
                                    };
                                    cell.CashValue = SampleBonusCashStrikeValue(rng);
                                    break;

                                case SymbolType.MiniVortex:
                                    cell.CashValue = 0.0;
                                    break;

                                case SymbolType.XWheel:
                                    cell.CashValue = 1.0;
                                    break;
                            }
                            newlyLandedBonusCells.Add(cell);
                        }
                    }
                }
                else if (emptyPositions.Count > 0)
                {
                    // Fallback land 1 Cash Coin
                    int posIdx = rng.Next(emptyPositions.Count);
                    var pos = emptyPositions[posIdx];
                    var cell = bonusGrid[pos.r, pos.c];
                    cell.Type = SymbolType.CashCoin;
                    cell.CashValue = SampleBonusCashCoinValue(rng);
                    cell.JustLanded = true;
                    newlyLandedBonusCells.Add(cell);
                }

                // Execute Strikes and Vortexes on the bonus grid
                ExecuteBonusSpecialSymbolActions(bonusGrid, newlyLandedBonusCells);

                // If any X Symbol landed, trigger bonus wheel
                if (newlyLandedBonusCells.Any(c => c.Type == SymbolType.XWheel))
                {
                    RunBonusWheelFeature(rng, bonusGrid, spinResult, ref bonusDirectJackpotWin);
                }
            }
            else
            {
                currentLives--;
            }
        }

        // Count completed Slingo lines on the bonus grid (0..12)
        int completedSlingos = CountBonusSlingos(bonusGrid);

        // Find highest achieved Slingo ladder prize
        var ladderPrize = _config.SlingoLadderPrizes
            .Where(p => p.SlingoCount <= completedSlingos && p.Type != WheelPrizeType.Multiplier || p.ParameterValue > 0)
            .OrderByDescending(p => p.SlingoCount)
            .FirstOrDefault();

        double ladderMultiplier = 0.0;

        if (ladderPrize != null)
        {
            switch (ladderPrize.Type)
            {
                case WheelPrizeType.UltraStrike:
                    ladderMultiplier = ladderPrize.ParameterValue;
                    for (int r = 0; r < 5; r++)
                    {
                        for (int c = 0; c < 5; c++)
                        {
                            var cell = bonusGrid[r, c];
                            if (cell.Type != SymbolType.Blank && cell.Type != SymbolType.JackpotCoin)
                            {
                                cell.CashValue += ladderPrize.ParameterValue;
                            }
                        }
                    }
                    break;

                case WheelPrizeType.Multiplier:
                    if (ladderPrize.ParameterValue > 0)
                    {
                        ladderMultiplier = ladderPrize.ParameterValue;
                        for (int r = 0; r < 5; r++)
                        {
                            for (int c = 0; c < 5; c++)
                            {
                                var cell = bonusGrid[r, c];
                                if (cell.Type != SymbolType.Blank && cell.Type != SymbolType.JackpotCoin)
                                {
                                    cell.CashValue *= ladderPrize.ParameterValue;
                                }
                            }
                        }
                    }
                    break;

                case WheelPrizeType.Jackpot:
                    double jpVal = 5.0;
                    if (ladderPrize.JackpotType != null)
                    {
                        if (ladderPrize.JackpotType.Contains("Mega", StringComparison.OrdinalIgnoreCase)) jpVal = 50.0;
                        else if (ladderPrize.JackpotType.Contains("Ultra", StringComparison.OrdinalIgnoreCase)) jpVal = 500.0;
                        else jpVal = 5.0;
                    }
                    ladderMultiplier = jpVal;
                    bonusDirectJackpotWin += (long)Math.Round(jpVal * 100);
                    break;
            }
        }

        // Sum all locked symbol cash values on the bonus grid
        double boardCashSum = 0.0;
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                if (bonusGrid[r, c].Type != SymbolType.Blank)
                {
                    boardCashSum += bonusGrid[r, c].CashValue;
                }
            }
        }

        long boardPayoutCents = (long)Math.Round(boardCashSum * 100);
        long totalBonusWinCents = boardPayoutCents + bonusDirectJackpotWin;

        spinResult.TotalWin += totalBonusWinCents;

        spinResult.TriggeredPotBonuses.Add(new TriggeredPotBonus
        {
            PotIndex = 2,
            BonusName = "Lock & Slingo",
            Win = totalBonusWinCents,
            SpinsPlayed = bonusSpinsCount,
            CompletedSlingos = completedSlingos,
            CashValuesSum = boardCashSum,
            LadderPrize = ladderMultiplier
        });
    }

    private int CountBonusSlingos(GridCell[,] bonusGrid)
    {
        int completed = 0;
        foreach (var line in _slingoLines)
        {
            bool complete = true;
            foreach (var idx in line)
            {
                int r = idx / 5;
                int c = idx % 5;
                if (bonusGrid[r, c].Type == SymbolType.Blank)
                {
                    complete = false;
                    break;
                }
            }
            if (complete) completed++;
        }
        return completed;
    }

    private static int GetBonusEmptyPositionsCount(GridCell[,] bonusGrid)
    {
        int empty = 0;
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                if (bonusGrid[r, c].Type == SymbolType.Blank) empty++;
            }
        }
        return empty;
    }

    private static List<(int r, int c)> GetBonusEmptyPositions(GridCell[,] bonusGrid)
    {
        var list = new List<(int r, int c)>();
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                if (bonusGrid[r, c].Type == SymbolType.Blank)
                {
                    list.Add((r, c));
                }
            }
        }
        return list;
    }

    private double SampleBonusCashCoinValue(IRng rng)
    {
        int idx = (_config.BonusCashCoinValueWeights.TotalWeight > 0)
            ? _config.BonusCashCoinValueWeights.Sample(rng)
            : _config.CashCoinValueWeights.Sample(rng);
        var list = _config.BonusCashCoinValues.Count > 0 ? _config.BonusCashCoinValues : _config.CashCoinValues;
        return list[Math.Min(idx, list.Count - 1)].Multiplier;
    }

    private double SampleBonusCashStrikeValue(IRng rng)
    {
        int idx = (_config.BonusCashStrikeValueWeights.TotalWeight > 0)
            ? _config.BonusCashStrikeValueWeights.Sample(rng)
            : _config.CashStrikeValueWeights.Sample(rng);
        var list = _config.BonusCashStrikeValues.Count > 0 ? _config.BonusCashStrikeValues : _config.CashStrikeValues;
        return list[Math.Min(idx, list.Count - 1)].Multiplier;
    }

    private void ExecuteBonusSpecialSymbolActions(GridCell[,] bonusGrid, List<GridCell> newlyLanded)
    {
        // 1. Process Strikes
        foreach (var cell in newlyLanded)
        {
            if (cell.Type == SymbolType.MiniStrike)
            {
                int[] dr = { -1, 1, 0, 0 };
                int[] dc = { 0, 0, -1, 1 };
                for (int i = 0; i < 4; i++)
                {
                    int nr = cell.Row + dr[i];
                    int nc = cell.Col + dc[i];
                    if (nr >= 0 && nr < 5 && nc >= 0 && nc < 5)
                    {
                        var t = bonusGrid[nr, nc];
                        if (IsValuableTarget(t.Type)) t.CashValue += cell.CashValue;
                    }
                }
            }
            else if (cell.Type == SymbolType.MegaStrike)
            {
                var lineCells = GetSameLineCellsBonus(bonusGrid, cell.Row, cell.Col);
                foreach (var t in lineCells)
                {
                    if (t != cell && IsValuableTarget(t.Type)) t.CashValue += cell.CashValue;
                }
            }
            else if (cell.Type == SymbolType.UltraStrike)
            {
                for (int r = 0; r < 5; r++)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        var t = bonusGrid[r, c];
                        if (t != cell && IsValuableTarget(t.Type)) t.CashValue += cell.CashValue;
                    }
                }
            }
        }

        // 2. Process Vortexes
        foreach (var cell in newlyLanded)
        {
            if (cell.Type == SymbolType.MiniVortex)
            {
                double sum = 0;
                int[] dr = { -1, 1, 0, 0 };
                int[] dc = { 0, 0, -1, 1 };
                for (int i = 0; i < 4; i++)
                {
                    int nr = cell.Row + dr[i];
                    int nc = cell.Col + dc[i];
                    if (nr >= 0 && nr < 5 && nc >= 0 && nc < 5)
                    {
                        var t = bonusGrid[nr, nc];
                        if (IsValuableTarget(t.Type)) sum += t.CashValue;
                    }
                }
                cell.CashValue = sum;
            }
            else if (cell.Type == SymbolType.MegaVortex)
            {
                double sum = 0;
                var lineCells = GetSameLineCellsBonus(bonusGrid, cell.Row, cell.Col);
                foreach (var t in lineCells)
                {
                    if (t != cell && IsValuableTarget(t.Type)) sum += t.CashValue;
                }
                cell.CashValue = sum;
            }
            else if (cell.Type == SymbolType.UltraVortex)
            {
                double sum = 0;
                for (int r = 0; r < 5; r++)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        var t = bonusGrid[r, c];
                        if (t != cell && IsValuableTarget(t.Type)) sum += t.CashValue;
                    }
                }
                cell.CashValue = sum;
            }
        }
    }

    private List<GridCell> GetSameLineCellsBonus(GridCell[,] bonusGrid, int row, int col)
    {
        var cellIndex = row * 5 + col;
        var matchingCells = new HashSet<GridCell>();

        foreach (var line in _slingoLines)
        {
            if (line.Contains(cellIndex))
            {
                foreach (var idx in line)
                {
                    int r = idx / 5;
                    int c = idx % 5;
                    matchingCells.Add(bonusGrid[r, c]);
                }
            }
        }
        return matchingCells.ToList();
    }

    private void RunBonusWheelFeature(IRng rng, GridCell[,] bonusGrid, SpinResult spinResult, ref long directJackpotWin)
    {
        int currentWheel = 1;

        while (currentWheel <= 3)
        {
            var weightTable = currentWheel switch
            {
                1 => (_config.BonusMiniWheelWeightTable.TotalWeight > 0 ? _config.BonusMiniWheelWeightTable : _config.MiniWheelWeightTable),
                2 => (_config.BonusMegaWheelWeightTable.TotalWeight > 0 ? _config.BonusMegaWheelWeightTable : _config.MegaWheelWeightTable),
                3 => (_config.BonusUltraWheelWeightTable.TotalWeight > 0 ? _config.BonusUltraWheelWeightTable : _config.UltraWheelWeightTable),
                _ => _config.UltraWheelWeightTable
            };

            var prizeList = currentWheel switch
            {
                1 => (_config.BonusMiniWheelPrizes.Count > 0 ? _config.BonusMiniWheelPrizes : _config.MiniWheelPrizes),
                2 => (_config.BonusMegaWheelPrizes.Count > 0 ? _config.BonusMegaWheelPrizes : _config.MegaWheelPrizes),
                3 => (_config.BonusUltraWheelPrizes.Count > 0 ? _config.BonusUltraWheelPrizes : _config.UltraWheelPrizes),
                _ => _config.UltraWheelPrizes
            };

            if (weightTable == null || prizeList == null || prizeList.Count == 0) break;

            int idx = weightTable.Sample(rng);
            var prize = prizeList[idx];

            if (prize.Type == WheelPrizeType.Upgrade)
            {
                currentWheel++;
                if (currentWheel > 3) currentWheel = 3;
                continue;
            }

            switch (prize.Type)
            {
                case WheelPrizeType.Multiplier:
                    double mult = prize.ParameterValue > 0 ? prize.ParameterValue : 2.0;
                    for (int r = 0; r < 5; r++)
                    {
                        for (int c = 0; c < 5; c++)
                        {
                            var cell = bonusGrid[r, c];
                            if (cell.Type != SymbolType.Blank && cell.Type != SymbolType.JackpotCoin)
                            {
                                cell.CashValue *= mult;
                            }
                        }
                    }
                    break;

                case WheelPrizeType.UltraStrike:
                    double strike = prize.ParameterValue;
                    for (int r = 0; r < 5; r++)
                    {
                        for (int c = 0; c < 5; c++)
                        {
                            var cell = bonusGrid[r, c];
                            if (cell.Type != SymbolType.Blank && cell.Type != SymbolType.JackpotCoin)
                            {
                                cell.CashValue += strike;
                            }
                        }
                    }
                    break;

                case WheelPrizeType.Jackpot:
                    double jpMult = 5.0;
                    if (!string.IsNullOrEmpty(prize.JackpotType))
                    {
                        if (prize.JackpotType.Contains("Mega", StringComparison.OrdinalIgnoreCase)) jpMult = 50.0;
                        else if (prize.JackpotType.Contains("Ultra", StringComparison.OrdinalIgnoreCase)) jpMult = 500.0;
                        else jpMult = 5.0;
                    }
                    directJackpotWin += (long)Math.Round(jpMult * 100);
                    break;
            }

            break;
        }
    }

    private void RunXWheelFeature(IRng rng, SpinResult spinResult, List<GridCell> newlyLanded)
    {
        bool xWheelLanded = newlyLanded.Any(c => c.Type == SymbolType.XWheel);
        if (!xWheelLanded) return;

        int currentWheel = 1;

        while (currentWheel <= 3)
        {
            WeightTable weightTable = currentWheel switch
            {
                1 => _config.MiniWheelWeightTable,
                2 => _config.MegaWheelWeightTable,
                3 => _config.UltraWheelWeightTable,
                _ => _config.UltraWheelWeightTable
            };

            var prizeList = currentWheel switch
            {
                1 => _config.MiniWheelPrizes,
                2 => _config.MegaWheelPrizes,
                3 => _config.UltraWheelPrizes,
                _ => _config.UltraWheelPrizes
            };

            if (weightTable == null || prizeList == null || prizeList.Count == 0) break;

            int idx = weightTable.Sample(rng);
            var prize = prizeList[idx];

            if (prize.Type == WheelPrizeType.Upgrade)
            {
                spinResult.TriggeredPotBonuses.Add(new TriggeredPotBonus
                {
                    PotIndex = 1,
                    BonusName = $"XWheel:W{currentWheel}:Upgrade",
                    Win = 0
                });

                currentWheel++;
                if (currentWheel > 3) currentWheel = 3;
                continue;
            }

            long featureWinCents = 0;

            switch (prize.Type)
            {
                case WheelPrizeType.Multiplier:
                    double mult = prize.ParameterValue > 0 ? prize.ParameterValue : 2.0;
                    for (int r = 0; r < 5; r++)
                    {
                        for (int c = 0; c < 5; c++)
                        {
                            var cell = _grid[r, c];
                            if (cell.Type != SymbolType.Blank && cell.Type != SymbolType.CentralWildStar && cell.Type != SymbolType.JackpotCoin)
                            {
                                cell.CashValue *= mult;
                            }
                        }
                    }
                    break;

                case WheelPrizeType.UltraStrike:
                    double strike = prize.ParameterValue;
                    for (int r = 0; r < 5; r++)
                    {
                        for (int c = 0; c < 5; c++)
                        {
                            var cell = _grid[r, c];
                            if (cell.Type != SymbolType.Blank && cell.Type != SymbolType.CentralWildStar && cell.Type != SymbolType.JackpotCoin)
                            {
                                cell.CashValue += strike;
                            }
                        }
                    }
                    break;

                case WheelPrizeType.Jackpot:
                    double jpMult = 5.0;
                    if (!string.IsNullOrEmpty(prize.JackpotType))
                    {
                        var match = _config.JackpotCoins.FirstOrDefault(j => j.JackpotName.Equals(prize.JackpotType, StringComparison.OrdinalIgnoreCase));
                        if (match != null) jpMult = match.Multiplier;
                        else if (prize.JackpotType.Contains("Mega", StringComparison.OrdinalIgnoreCase)) jpMult = 50.0;
                        else if (prize.JackpotType.Contains("Ultra", StringComparison.OrdinalIgnoreCase)) jpMult = 500.0;
                    }
                    featureWinCents = (long)Math.Round(jpMult * 100);
                    spinResult.TotalWin += featureWinCents;
                    break;

                case WheelPrizeType.LockAndSlingo:
                    PlayLockAndSlingoBonus(rng, spinResult);
                    break;
            }

            spinResult.TriggeredPotBonuses.Add(new TriggeredPotBonus
            {
                PotIndex = 1,
                BonusName = $"XWheel:W{currentWheel}:{prize.PrizeString}",
                Win = featureWinCents
            });

            break;
        }
    }
}
