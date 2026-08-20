using System;
using System.Collections.Generic;
using System.Linq;
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

public class SlingoLadderPrizeDef
{
    public int SlingoCount { get; set; }
    public string PrizeString { get; set; } = string.Empty;
    public WheelPrizeType Type { get; set; }
    public double ParameterValue { get; set; }
    public string? JackpotType { get; set; }
}

public class BonusOutcomeItem
{
    public SymbolType Type { get; set; }
    public int Count { get; set; } = 1;
}

public class BonusOutcomeDef
{
    public int OutcomeId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int[] WeightsBySpaceBucket { get; set; } = new int[5];
    public List<BonusOutcomeItem> Items { get; set; } = new();
}

public class CashVortexConfig
{
    public string GameName { get; set; } = "Cash Vortex - Triple Power";

    // Base Game Config
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

    // Lock & Slingo Bonus Config
    public List<SlingoLadderPrizeDef> SlingoLadderPrizes { get; set; } = new();
    public int BonusBaseFactor { get; set; } = 400;
    public int[,] BonusLandingWeightsByLifeAndBucket { get; set; } = new int[4, 5]; // life: 1..3, bucket: 0..4
    public List<BonusOutcomeDef> BonusOutcomeDefs { get; set; } = new();
    public List<JackpotCoinDef> BonusJackpotCoins { get; set; } = new();
    public List<SpecialSymbolDef> BonusCashStrikeTypes { get; set; } = new();
    public List<CashValueDef> BonusCashStrikeValues { get; set; } = new();
    public List<CashValueDef> BonusCashCoinValues { get; set; } = new();

    public List<WheelPrizeDef> BonusMiniWheelPrizes { get; set; } = new();
    public List<WheelPrizeDef> BonusMegaWheelPrizes { get; set; } = new();
    public List<WheelPrizeDef> BonusUltraWheelPrizes { get; set; } = new();

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

    // Bonus sampling structures
    public WeightTable[] BonusOutcomeWeightsByBucket { get; set; } = Array.Empty<WeightTable>();
    public WeightTable BonusJackpotWeights { get; set; } = new(Array.Empty<int>());
    public WeightTable BonusCashStrikeTypeWeights { get; set; } = new(Array.Empty<int>());
    public WeightTable BonusCashStrikeValueWeights { get; set; } = new(Array.Empty<int>());
    public WeightTable BonusCashCoinValueWeights { get; set; } = new(Array.Empty<int>());

    public WeightTable BonusMiniWheelWeightTable { get; set; } = new(Array.Empty<int>());
    public WeightTable BonusMegaWheelWeightTable { get; set; } = new(Array.Empty<int>());
    public WeightTable BonusUltraWheelWeightTable { get; set; } = new(Array.Empty<int>());

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

        // Build Bonus Weight Tables
        BonusOutcomeWeightsByBucket = new WeightTable[5];
        for (int b = 0; b < 5; b++)
        {
            var bucketWeights = BonusOutcomeDefs.Select(o => (b < o.WeightsBySpaceBucket.Length) ? o.WeightsBySpaceBucket[b] : 0).ToArray();
            BonusOutcomeWeightsByBucket[b] = new WeightTable(bucketWeights);
        }

        BonusJackpotWeights = new WeightTable((BonusJackpotCoins.Count > 0 ? BonusJackpotCoins : JackpotCoins).Select(j => j.Weight).ToArray());
        BonusCashStrikeTypeWeights = new WeightTable(BonusCashStrikeTypes.Select(s => s.Weight).ToArray());
        BonusCashStrikeValueWeights = new WeightTable((BonusCashStrikeValues.Count > 0 ? BonusCashStrikeValues : CashStrikeValues).Select(c => c.Weight).ToArray());
        BonusCashCoinValueWeights = new WeightTable((BonusCashCoinValues.Count > 0 ? BonusCashCoinValues : CashCoinValues).Select(c => c.Weight).ToArray());

        BonusMiniWheelWeightTable = new WeightTable((BonusMiniWheelPrizes.Count > 0 ? BonusMiniWheelPrizes : MiniWheelPrizes).Select(p => p.Weight).ToArray());
        BonusMegaWheelWeightTable = new WeightTable((BonusMegaWheelPrizes.Count > 0 ? BonusMegaWheelPrizes : MegaWheelPrizes).Select(p => p.Weight).ToArray());
        BonusUltraWheelWeightTable = new WeightTable((BonusUltraWheelPrizes.Count > 0 ? BonusUltraWheelPrizes : UltraWheelPrizes).Select(p => p.Weight).ToArray());
    }
}
