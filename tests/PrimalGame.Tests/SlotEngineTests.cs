using System;
using System.IO;
using Xunit;
using PrimalGame;
using PrimalGame.Config;
using SlotFramework.Interfaces;
using SlotFramework.Utilities;

namespace PrimalGame.Tests;

public class SlotEngineTests
{
    private static PrimalConfig LoadConfig()
    {
        return ExcelConfigLoader.Load();
    }

    [Fact]
    public void ConfigLoader_PopulatesPaytableMatrixAndRules()
    {
        var config = LoadConfig();
        Assert.NotNull(config);
        Assert.NotEmpty(config.Symbols);
        Assert.NotEmpty(config.Paylines);
        Assert.Equal(20, config.Paylines.Length);
        Assert.NotNull(config.FastPaytableMatrix);
    }

    [Fact]
    public void SlotEngine_StageProgression_IncrementsCorrectly()
    {
        var config = LoadConfig();
        var rng = new FastRandom(12345);
        var engine = new PrimalSlotEngine(config);

        Assert.Equal(0, engine.StageIndex);
        Assert.Equal("Stage0", engine.CurrentStage);

        int spinsToNext = config.StageSpinsToNext.Length > 0 ? config.StageSpinsToNext[0] : 30;

        // Spin spinsToNext times to reach end of Stage 0
        for (int i = 0; i < spinsToNext; i++)
        {
            engine.Spin(rng);
        }

        // Spin one more spin to transition to Stage 1
        engine.Spin(rng);
        Assert.Equal(1, engine.StageIndex);
        Assert.Equal("Stage1", engine.CurrentStage);
    }

    [Fact]
    public void SlotEngine_Spin_ReturnsValidResultGrid()
    {
        var config = LoadConfig();
        var rng = new FastRandom(42);
        var engine = new PrimalSlotEngine(config);

        var result = engine.Spin(rng);
        Assert.NotNull(result);
        Assert.Equal(5, result.ScreenSymbols.Length);
        for (int r = 0; r < 5; r++)
        {
            Assert.Equal(3, result.ScreenSymbols[r].Length);
        }
        Assert.NotNull(result.PotPowersAfter);
        Assert.Equal(4, result.PotPowersAfter.Length);
    }
}
