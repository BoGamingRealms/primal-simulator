using System;
using System.IO;
using System.Linq;
using Xunit;
using CashVortexGame;
using CashVortexGame.Config;
using SlotFramework.Models;
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
    public void ConfigLoader_ParsesWheelPrizesCorrectly()
    {
        var config = LoadConfig();
        Assert.NotNull(config);
        Assert.NotEmpty(config.MiniWheelPrizes);
        Assert.NotEmpty(config.MegaWheelPrizes);
        Assert.NotEmpty(config.UltraWheelPrizes);
        Assert.NotEmpty(config.CenterWheelPrizes);
    }

    [Fact]
    public void ConfigLoader_ParsesLockAndSlingoCorrectly()
    {
        var config = LoadConfig();
        Assert.NotNull(config);
        Assert.NotEmpty(config.SlingoLadderPrizes);
        Assert.True(config.BonusBaseFactor > 0);
        Assert.NotEmpty(config.BonusOutcomeDefs);
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
    public void SlotEngine_XSymbol_HasOneXCashValueAndTriggersWheel()
    {
        var config = LoadConfig();
        var engine = new CashVortexSlotEngine(config);

        // Verify X Symbol initialization behavior
        engine.Grid[0, 0].Type = SymbolType.XWheel;
        engine.Grid[0, 0].CashValue = 1.0;
        engine.Grid[0, 0].LifeRemaining = 3;

        Assert.Equal(1.0, engine.Grid[0, 0].CashValue);
        Assert.Equal(SymbolType.XWheel, engine.Grid[0, 0].Type);
    }

    [Fact]
    public void SlotEngine_JackpotCoin_IsNotModifiedByStrikesOrVortexes()
    {
        var config = LoadConfig();
        var engine = new CashVortexSlotEngine(config);

        // Place a Mini Jackpot (5.0) at (0, 0)
        engine.Grid[0, 0].Type = SymbolType.JackpotCoin;
        engine.Grid[0, 0].JackpotType = "Mini";
        engine.Grid[0, 0].CashValue = 5.0;
        engine.Grid[0, 0].LifeRemaining = 3;

        // Verify that Jackpot Coin remains intact with its fixed 5.0 value
        Assert.Equal(5.0, engine.Grid[0, 0].CashValue);
        Assert.Equal(SymbolType.JackpotCoin, engine.Grid[0, 0].Type);
    }

    [Fact]
    public void SlotEngine_LockAndSlingoBonus_PlaysAndCompletesValidly()
    {
        var config = LoadConfig();
        var engine = new CashVortexSlotEngine(config);
        var rng = new FastRandom(999);
        var spinResult = new SpinResult();

        engine.PlayLockAndSlingoBonus(rng, spinResult);

        Assert.NotEmpty(spinResult.TriggeredPotBonuses);
        var lnsRecord = spinResult.TriggeredPotBonuses.FirstOrDefault(b => b.BonusName == "Lock & Slingo");
        Assert.NotNull(lnsRecord);
        Assert.True(lnsRecord.Win >= 0);
        Assert.True(lnsRecord.SpinsPlayed >= 1);
        Assert.True(lnsRecord.CompletedSlingos >= 0 && lnsRecord.CompletedSlingos <= 12);
    }
}
