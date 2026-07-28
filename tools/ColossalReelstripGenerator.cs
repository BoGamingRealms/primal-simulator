using System;
using System.Collections.Generic;
using System.Linq;

namespace ReelstripGeneratorTool;

/// <summary>
/// Dedicated reelset generator for Colossal Spins.
/// Middle 3 reels (Reels 1, 2, 3) are tied together with 3x3 colossal blocks.
/// First and last reels (Reels 0 and 4) follow standard reelset rules.
/// </summary>
public class ColossalReelstripGenerator
{
    public class Distribution
    {
        public Dictionary<int, int> Reel0 { get; set; } = new();
        public Dictionary<int, int> Reel1 { get; set; } = new();
        public Dictionary<int, int> Reel2 { get; set; } = new();
        public Dictionary<int, int> Reel3 { get; set; } = new();
        public Dictionary<int, int> Reel4 { get; set; } = new();

        public Dictionary<int, int> GetForReel(int reelIndex) => reelIndex switch
        {
            0 => Reel0,
            1 => Reel1,
            2 => Reel2,
            3 => Reel3,
            4 => Reel4,
            _ => throw new ArgumentOutOfRangeException(nameof(reelIndex))
        };
    }

    private struct StackItem
    {
        public int Id;
        public int Size;
        public StackItem(int id, int size)
        {
            Id = id;
            Size = size;
        }
    }

    private readonly Random _rnd;

    public ColossalReelstripGenerator(int? seed = null)
    {
        _rnd = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public List<int>[] Generate(Distribution dist)
    {
        var reels = new List<int>[5];

        // Reel 0 (Standard rules)
        reels[0] = GenerateStandardReel(dist.Reel0);

        // Reels 1, 2, 3 (Middle 3 reels tied together as 3x3 colossal blocks)
        var middleReel = GenerateColossalMiddleReel(dist.Reel1);
        reels[1] = middleReel;
        reels[2] = new List<int>(middleReel);
        reels[3] = new List<int>(middleReel);

        // Reel 4 (Standard rules)
        reels[4] = GenerateStandardReel(dist.Reel4);

        return reels;
    }

    private List<int> GenerateColossalMiddleReel(Dictionary<int, int> reelDist)
    {
        // Verify all symbol counts are multiples of 3
        foreach (var kvp in reelDist)
        {
            if (kvp.Value > 0 && kvp.Value % 3 != 0)
            {
                throw new ArgumentException($"Symbol {kvp.Key} count ({kvp.Value}) on middle reels is not a multiple of 3!");
            }
        }

        var activeSymbols = reelDist
            .Where(kvp => kvp.Value > 0)
            .Select(kvp => (id: kvp.Key, count: kvp.Value))
            .ToList();

        List<int> flatReel = new List<int>();
        bool isValid = false;
        int attempts = 0;

        while (!isValid && attempts < 50000)
        {
            attempts++;

            var lowPayBlocks = new List<StackItem>();
            var highPayBlocks = new List<StackItem>();

            foreach (var item in activeSymbols)
            {
                int numBlocks = item.count / 3;
                for (int i = 0; i < numBlocks; i++)
                {
                    var stack = new StackItem(item.id, 3);
                    if (IsSpecialOrHighPay(item.id))
                    {
                        highPayBlocks.Add(stack);
                    }
                    else
                    {
                        lowPayBlocks.Add(stack);
                    }
                }
            }

            Shuffle(lowPayBlocks);
            Shuffle(highPayBlocks);

            List<StackItem> interleaved;
            if (lowPayBlocks.Count >= highPayBlocks.Count)
            {
                interleaved = Interleave(lowPayBlocks, highPayBlocks);
            }
            else
            {
                interleaved = Interleave(highPayBlocks, lowPayBlocks);
            }

            flatReel = new List<int>();
            foreach (var block in interleaved)
            {
                flatReel.Add(block.Id);
                flatReel.Add(block.Id);
                flatReel.Add(block.Id);
            }

            isValid = IsValidColossalReel(flatReel);
        }

        if (!isValid)
        {
            throw new InvalidOperationException("Failed to generate a valid colossal middle reel after 50,000 attempts.");
        }

        return flatReel;
    }

    private bool IsValidColossalReel(List<int> flatReel)
    {
        int len = flatReel.Count;
        for (int i = 0; i < len; i += 3)
        {
            int currentSym = flatReel[i];
            int nextSym = flatReel[(i + 3) % len];

            // Rule: No two adjacent colossal blocks can have the same symbol ID
            if (currentSym == nextSym)
            {
                return false;
            }

            // Rule: Special/high pay blocks cannot be adjacent
            if (IsSpecialOrHighPay(currentSym) && IsSpecialOrHighPay(nextSym))
            {
                return false;
            }
        }
        return true;
    }

    private bool IsSpecialOrHighPay(int id)
    {
        return id >= 5; // Symbols 5-7 (H3-H1), 8 (Wild), 9-13 (Pots/Specials), 14 (Fire Core)
    }

    private List<int> GenerateStandardReel(Dictionary<int, int> reelDist)
    {
        var standardGen = new ReelstripGenerator(_rnd.Next());
        var dist = new ReelstripGenerator.Distribution();
        dist.Reel0 = reelDist;
        return standardGen.Generate(dist)[0];
    }

    private void Shuffle<T>(List<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = _rnd.Next(n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    private List<StackItem> Interleave(List<StackItem> largerList, List<StackItem> smallerList)
    {
        var result = new List<StackItem>();
        int j = 0;
        if (smallerList.Count == 0) return largerList;

        double ratio = (double)smallerList.Count / largerList.Count;
        for (int i = 0; i < largerList.Count; i++)
        {
            result.Add(largerList[i]);
            int nextJ = (int)Math.Round((i + 1) * ratio);
            while (j < nextJ && j < smallerList.Count)
            {
                result.Add(smallerList[j]);
                j++;
            }
        }
        while (j < smallerList.Count)
        {
            result.Add(smallerList[j]);
            j++;
        }
        return result;
    }
}
