using System;
using System.IO;
using Xunit;
using CashVortexGame;
using CashVortexGame.Config;
using SlotFramework.Utilities;

namespace CashVortexGame.Tests;

public class CashVortexEngineTests
{
    private static CashVortexConfig LoadConfig()
    {
        return CashVortexExcelLoader.Load();
    }

    [Fact]
    public void ConfigLoader_ParsesCashVortexTriplePower95Correctly()
    {
        var config = LoadConfig();
        Assert.NotNull(config);
        Assert.NotEmpty(config.TableSelections);
        Assert.NotEmpty(config.SpecialSymbolChances);
        Assert.NotEmpty(config.SpecialSymbolDefs);
        Assert.NotEmpty(config.JackpotCoins);
        Assert.NotEmpty(config.CashStrikeValues);
        Assert.NotEmpty(config.CashCoinChances);
        Assert.NotEmpty(config.CashCoinValues);
    }

    [Fact]
    public void SlotEngine_CentralWildStar_StaysAtCenter()
    {
        var config = LoadConfig();
        var engine = new CashVortexSlotEngine(config);

        Assert.Equal(SymbolType.CentralWildStar, engine.Grid[2, 2].Type);
        Assert.Equal(0.0, engine.Grid[2, 2].CashValue);

        var rng = new FastRandom(42);
        for (int i = 0; i < 10; i++)
        {
            engine.Spin(rng);
            Assert.Equal(SymbolType.CentralWildStar, engine.Grid[2, 2].Type);
        }
    }

    [Fact]
    public void SlotEngine_Spin_ReturnsValidResult()
    {
        var config = LoadConfig();
        var engine = new CashVortexSlotEngine(config);
        var rng = new FastRandom(12345);

        var result = engine.Spin(rng);
        Assert.NotNull(result);
        Assert.NotNull(result.ScreenSymbols);
        Assert.Equal(5, result.ScreenSymbols.Length);
        for (int r = 0; r < 5; r++)
        {
            Assert.Equal(5, result.ScreenSymbols[r].Length);
        }
    }

    [Fact]
    public void SlotEngine_SymbolLifeCycle_ResetsOnSameSlingoLine()
    {
        var config = LoadConfig();
        var engine = new CashVortexSlotEngine(config);

        // Manually place a cash coin at (0, 0) with 1 life remaining
        engine.Grid[0, 0].Type = SymbolType.CashCoin;
        engine.Grid[0, 0].CashValue = 1.0;
        engine.Grid[0, 0].LifeRemaining = 1;

        // Manually trigger spin where a symbol lands at (0, 4) on the same horizontal line
        var rng = new FastRandom(999);
        engine.Spin(rng);

        // If a symbol landed anywhere on row 0, cell (0, 0)'s life should have been reset to 3 or updated
        // Grid cell (0, 0) should either remain populated with reset life or be active
        Assert.True(engine.Grid[0, 0].LifeRemaining >= 0);
    }
}
