using System;
using System.Collections.Generic;
using System.Linq;
using SlotFramework.Interfaces;
using SlotFramework.Models;
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
    public SymbolType Type { get; set; } = SymbolType.Blank;
    public double CashValue { get; set; } = 0.0;
    public int LifeRemaining { get; set; } = 0; // 3, 2, 1, 0
    public string? JackpotType { get; set; }
    public bool WonThisSpin { get; set; } = false;
    public bool JustLanded { get; set; } = false;
}

public class CashVortexSlotEngine : ISlotEngine
{
    private readonly CashVortexConfig _config;
    private readonly GridCell[,] _grid = new GridCell[5, 5];
    private readonly List<int[]> _slingoLines = new();

    private string _currentStage = "Stage0";
    private int _spinsInCurrentStage = 0;
    private int _stageIndex = 0;

    public CashVortexSlotEngine(CashVortexConfig config)
    {
        _config = config;
        InitializeGrid();
        InitializeSlingoLines();
    }

    public string CurrentStage => _currentStage;
    public int SpinsInCurrentStage => _spinsInCurrentStage;
    public int StageIndex => _stageIndex;
    public GridCell[,] Grid => _grid;

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
                    Type = SymbolType.Blank,
                    CashValue = 0.0,
                    LifeRemaining = 0
                };
            }
        }
        // Place static Central Wild Star at (2, 2)
        _grid[2, 2].Type = SymbolType.CentralWildStar;
        _grid[2, 2].CashValue = 0.0;
        _grid[2, 2].LifeRemaining = int.MaxValue;
    }

    private void InitializeSlingoLines()
    {
        _slingoLines.Clear();
        // 5 Horizontal lines
        for (int r = 0; r < 5; r++)
        {
            _slingoLines.Add(new[] { r * 5 + 0, r * 5 + 1, r * 5 + 2, r * 5 + 3, r * 5 + 4 });
        }
        // 5 Vertical lines
        for (int c = 0; c < 5; c++)
        {
            _slingoLines.Add(new[] { 0 * 5 + c, 1 * 5 + c, 2 * 5 + c, 3 * 5 + c, 4 * 5 + c });
        }
        // Main Diagonal
        _slingoLines.Add(new[] { 0, 6, 12, 18, 24 });
        // Anti Diagonal
        _slingoLines.Add(new[] { 4, 8, 12, 16, 20 });
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
                    cell.CashValue = 0.0;
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

        // Step F: Apply Symbol Life Cycle Reset for Line-Sharing Existing Symbols
        ApplyLifeCycleResets(newlyLandedCells);

        // Step G: Evaluate 12 Slingo Lines
        EvaluateSlingoLines(spinResult);

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
               type == SymbolType.UltraVortex;
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

    private void EvaluateSlingoLines(SpinResult spinResult)
    {
        int completedLinesCount = 0;

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
                    spinResult.TriggeredPotBonuses.Add(new TriggeredPotBonus
                    {
                        PotIndex = 0,
                        BonusName = "Jackpot Bonus",
                        Power = 1,
                        Win = linePayout
                    });
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
    }
}
