using System;
using SlotFramework.Interfaces;

namespace SlotFramework.Utilities;

/// <summary>
/// Pre-calculated cumulative weight table for O(1) / fast branching weighted sampling.
/// Avoids repeated linear summing of weights on every spin.
/// </summary>
public class WeightTable
{
    private readonly int[] _cumulative;
    public int TotalWeight { get; }
    public int Length => _cumulative.Length;

    public WeightTable(int[] weights)
    {
        if (weights == null || weights.Length == 0)
        {
            _cumulative = Array.Empty<int>();
            TotalWeight = 0;
            return;
        }

        _cumulative = new int[weights.Length];
        int sum = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            sum += weights[i];
            _cumulative[i] = sum;
        }
        TotalWeight = sum;
    }

    public int Sample(IRng rng)
    {
        if (TotalWeight <= 0 || _cumulative.Length == 0) return 0;
        int r = rng.Next(TotalWeight);
        
        for (int i = 0; i < _cumulative.Length; i++)
        {
            if (r < _cumulative[i]) return i;
        }
        return 0;
    }
}
