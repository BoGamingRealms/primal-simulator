using System;
using System.Collections.Generic;
using System.Linq;

namespace ReelstripGeneratorTool;

/// <summary>
/// Standalone helper utility to generate game reelstrips based on symbol distribution constraints.
/// Save this code or copy it to any game simulator/tool project as needed.
/// </summary>
public class ReelstripGenerator
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

    public ReelstripGenerator(int? seed = null)
    {
        _rnd = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Generates 5 reelstrips conforming to the distribution and spacing constraints.
    /// </summary>
    public List<int>[] Generate(Distribution dist)
    {
        var reels = new List<int>[5];
        for (int r = 0; r < 5; r++)
        {
            var reelDist = dist.GetForReel(r);
            // Filter out symbol counts that are 0
            var activeSymbols = reelDist
                .Where(kvp => kvp.Value > 0)
                .Select(kvp => (id: kvp.Key, count: kvp.Value))
                .ToList();
            
            List<int> flatReel = new List<int>();
            bool isValid = false;
            int attempts = 0;

            while (!isValid && attempts < 20000)
            {
                attempts++;
                var lowPayStacks = new List<StackItem>();
                var highPayStacks = new List<StackItem>();

                foreach (var item in activeSymbols)
                {
                    var partitions = PartitionCount(item.id, item.count);
                    foreach (var size in partitions)
                    {
                        var stack = new StackItem(item.id, size);
                        if (IsHighPayOrSpecial(item.id))
                        {
                            highPayStacks.Add(stack);
                        }
                        else
                        {
                            lowPayStacks.Add(stack);
                        }
                    }
                }

                // Shuffle both stack lists
                Shuffle(lowPayStacks);
                Shuffle(highPayStacks);

                // Interleave them so they are evenly distributed
                List<StackItem> interleavedStacks;
                if (lowPayStacks.Count >= highPayStacks.Count)
                {
                    interleavedStacks = Interleave(lowPayStacks, highPayStacks);
                }
                else
                {
                    interleavedStacks = Interleave(highPayStacks, lowPayStacks);
                }

                // Flatten into list of symbol IDs
                flatReel = new List<int>();
                foreach (var stack in interleavedStacks)
                {
                    for (int i = 0; i < stack.Size; i++)
                    {
                        flatReel.Add(stack.Id);
                    }
                }

                // Validate the reel against the rules
                isValid = IsValidReel(flatReel);
            }

            if (!isValid)
            {
                throw new InvalidOperationException($"Failed to generate a valid reel strip for Reel {r} after 20,000 attempts under current rules.");
            }

            reels[r] = flatReel;
        }

        return reels;
    }

    private List<int> PartitionCount(int id, int total)
    {
        var list = new List<int>();
        
        // Rule: Symbol 14 is always stack height 3
        if (id == 14)
        {
            if (total % 3 != 0)
            {
                throw new ArgumentException($"Symbol 14 total count ({total}) must be a multiple of 3.");
            }
            int numStacks = total / 3;
            for (int i = 0; i < numStacks; i++)
            {
                list.Add(3);
            }
            return list;
        }

        // Rule: Special symbols (9, 10, 11, 12, 13) are always stack height 1
        if (id >= 9 && id <= 13)
        {
            for (int i = 0; i < total; i++)
            {
                list.Add(1);
            }
            return list;
        }

        // Rule: Normal symbols (0 to 8) can be stack height 1, 2, or maximum 3
        int sum = 0;
        while (sum < total)
        {
            int size = _rnd.Next(1, 4); // Stack height: 1, 2, or 3
            if (sum + size > total)
            {
                size = total - sum;
            }
            list.Add(size);
            sum += size;
        }

        // Shuffle partitions
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = _rnd.Next(n + 1);
            int value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
        return list;
    }

    private bool IsHighPayOrSpecial(int id)
    {
        return id >= 5; // H3(5), H2(6), H1(7), Wild(8), Specials(9-13), Jackpot(14) are high/special
    }

    private bool IsValidReel(List<int> flatReel)
    {
        int len = flatReel.Count;
        
        // 1. Verify symbol 14 rules (stacks of exactly 3, gap of at least 5 normal symbols between stacks)
        var indices14 = new List<int>();
        for (int i = 0; i < len; i++)
        {
            if (flatReel[i] == 14)
                indices14.Add(i);
        }
        
        if (indices14.Count > 0)
        {
            var stackStarts = new List<int>();
            for (int idx = 0; idx < len; idx++)
            {
                if (flatReel[idx] == 14 && flatReel[(idx - 1 + len) % len] != 14)
                {
                    stackStarts.Add(idx);
                }
            }
            
            foreach (int start in stackStarts)
            {
                if (flatReel[(start + 1) % len] != 14 || flatReel[(start + 2) % len] != 14 || flatReel[(start + 3) % len] == 14)
                {
                    return false; // Not a stack of exactly 3
                }
            }
            
            if (stackStarts.Count * 3 != indices14.Count)
            {
                return false; 
            }
            
            stackStarts.Sort();
            for (int i = 0; i < stackStarts.Count; i++)
            {
                int currentStart = stackStarts[i];
                int nextStart = stackStarts[(i + 1) % stackStarts.Count];
                
                int gap;
                if (nextStart > currentStart)
                {
                    gap = nextStart - (currentStart + 2) - 1;
                }
                else
                {
                    gap = (nextStart + len) - (currentStart + 2) - 1;
                }
                
                if (gap < 5)
                {
                    return false; 
                }
            }

            // Check spacing between stack of 14 and any special symbol (9-13)
            foreach (int start in stackStarts)
            {
                for (int d = 1; d <= 4; d++)
                {
                    int leftSym = flatReel[(start - d + len) % len];
                    int rightSym = flatReel[(start + 2 + d) % len];
                    if (leftSym >= 9 && leftSym <= 13) return false;
                    if (rightSym >= 9 && rightSym <= 13) return false;
                }
            }
        }

        // 2. Verify special symbol rules (9, 10, 11, 12, 13: height 1 and distance >= 5 from any special symbol)
        for (int i = 0; i < len; i++)
        {
            int sym = flatReel[i];

            if (sym >= 9 && sym <= 13)
            {
                // Max height 1
                if (flatReel[(i + 1) % len] == sym)
                    return false;

                // At least 5 symbols away from ANY special symbol
                for (int dist = 1; dist < 5; dist++)
                {
                    int neighbor = flatReel[(i + dist) % len];
                    if (neighbor >= 9 && neighbor <= 13)
                        return false;
                }
            }
            else if (sym != 14)
            {
                // Normal symbols: max stack height 3
                if (flatReel[(i + 1) % len] == sym && flatReel[(i + 2) % len] == sym && flatReel[(i + 3) % len] == sym)
                    return false;
            }
        }
        return true;
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
        
        if (smallerList.Count == 0)
        {
            return largerList;
        }

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
