using System;
using System.Collections.Generic;
using SlotFramework.Models;
using SlotFramework.Utilities;

namespace CashVortexGame.Config;

public enum WheelPrizeType
{
    Multiplier,
    UltraStrike,
    Jackpot,
    LockAndSlingo,
    Upgrade
}

public class WheelPrizeDef
{
    public int PrizeId { get; set; }
    public string PrizeString { get; set; } = string.Empty;
    public int Weight { get; set; }
    public WheelPrizeType Type { get; set; }
    public double ParameterValue { get; set; } = 0.0;
    public string? JackpotType { get; set; }
}

public class TableSelection
{
    public int TableId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Weight { get; set; }
}

public class SpecialSymbolChance
{
    public int TableId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int SpecialSymbolWeight { get; set; }
    public int NoSpecialSymbolWeight { get; set; }
}

public class SpecialSymbolDef
{
    public int SymbolId { get; set; }
    public string SymbolName { get; set; } = string.Empty;
    public int Weight { get; set; }
}

public class JackpotCoinDef
{
    public int JackpotId { get; set; }
    public string JackpotName { get; set; } = string.Empty;
    public double Multiplier { get; set; }
    public int Weight { get; set; }
}

public class CashValueDef
{
    public double Multiplier { get; set; }
    public int Weight { get; set; }
}

public class CashCoinChance
{
    public int TableId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int CoinWeight { get; set; }
    public int BlankWeight { get; set; }
}

public class CashVortexConfig
{
    public string GameName { get; set; } = "Cash Vortex - Triple Power";

    public List<TableSelection> TableSelections { get; set; } = new();
    public List<SpecialSymbolChance> SpecialSymbolChances { get; set; } = new();
    public List<SpecialSymbolDef> SpecialSymbolDefs { get; set; } = new();
    public List<JackpotCoinDef> JackpotCoins { get; set; } = new();
    public List<CashValueDef> CashStrikeValues { get; set; } = new();
    public List<CashCoinChance> CashCoinChances { get; set; } = new();
    public List<CashValueDef> CashCoinValues { get; set; } = new();

    public List<WheelPrizeDef> MiniWheelPrizes { get; set; } = new();
    public List<WheelPrizeDef> MegaWheelPrizes { get; set; } = new();
    public List<WheelPrizeDef> UltraWheelPrizes { get; set; } = new();

    // Fast sampling structures
    public WeightTable TableSelectionWeights { get; set; } = new(Array.Empty<int>());
    public Dictionary<int, WeightTable> SpecialSymbolChanceWeights { get; set; } = new();
    public WeightTable SpecialSymbolTypeWeights { get; set; } = new(Array.Empty<int>());
    public WeightTable JackpotTypeWeights { get; set; } = new(Array.Empty<int>());
    public WeightTable CashStrikeValueWeights { get; set; } = new(Array.Empty<int>());
    public Dictionary<int, WeightTable> CashCoinChanceWeights { get; set; } = new();
    public WeightTable CashCoinValueWeights { get; set; } = new(Array.Empty<int>());

    public WeightTable MiniWheelWeightTable { get; set; } = new(Array.Empty<int>());
    public WeightTable MegaWheelWeightTable { get; set; } = new(Array.Empty<int>());
    public WeightTable UltraWheelWeightTable { get; set; } = new(Array.Empty<int>());

    public void BuildWeightTables()
    {
        TableSelectionWeights = new WeightTable(TableSelections.Select(t => t.Weight).ToArray());

        SpecialSymbolChanceWeights.Clear();
        foreach (var ssc in SpecialSymbolChances)
        {
            SpecialSymbolChanceWeights[ssc.TableId] = new WeightTable(new[] { ssc.SpecialSymbolWeight, ssc.NoSpecialSymbolWeight });
        }

        SpecialSymbolTypeWeights = new WeightTable(SpecialSymbolDefs.Select(s => s.Weight).ToArray());
        JackpotTypeWeights = new WeightTable(JackpotCoins.Select(j => j.Weight).ToArray());
        CashStrikeValueWeights = new WeightTable(CashStrikeValues.Select(c => c.Weight).ToArray());

        CashCoinChanceWeights.Clear();
        foreach (var ccc in CashCoinChances)
        {
            CashCoinChanceWeights[ccc.TableId] = new WeightTable(new[] { ccc.CoinWeight, ccc.BlankWeight });
        }

        CashCoinValueWeights = new WeightTable(CashCoinValues.Select(c => c.Weight).ToArray());

        MiniWheelWeightTable = new WeightTable(MiniWheelPrizes.Select(p => p.Weight).ToArray());
        MegaWheelWeightTable = new WeightTable(MegaWheelPrizes.Select(p => p.Weight).ToArray());
        UltraWheelWeightTable = new WeightTable(UltraWheelPrizes.Select(p => p.Weight).ToArray());
    }
}
