using Xunit;
using CashVortexGame;
using CashVortexGame.Config;
using SlotFramework.Utilities;

namespace CashVortexGame.Tests;

public class CashVortexEngineTests
{
    [Fact]
    public void CashVortexSlotEngine_Spin_InitializesAndSpinsSuccessfully()
    {
        var config = new CashVortexConfig();
        var engine = new CashVortexSlotEngine(config);
        var rng = new FastRandom(42);

        var result = engine.Spin(rng);

        Assert.NotNull(result);
        Assert.Equal(5, result.ScreenSymbols.Length);
        Assert.NotNull(result.PotPowersAfter);
        Assert.Equal(3, result.PotPowersAfter.Length);
    }
}
