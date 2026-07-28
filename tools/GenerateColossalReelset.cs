using System;
using System.Collections.Generic;
using System.Linq;
using ReelstripGeneratorTool;

public class Program
{
    public static void Main()
    {
        var dist = new ColossalReelstripGenerator.Distribution();

        // Reel 0 distribution
        dist.Reel0[0] = 10;
        dist.Reel0[1] = 0;
        dist.Reel0[2] = 10;
        dist.Reel0[3] = 0;
        dist.Reel0[4] = 10;
        dist.Reel0[5] = 0;
        dist.Reel0[6] = 8;
        dist.Reel0[7] = 0;
        dist.Reel0[8] = 0;

        // Reel 1 (Middle Reels 1, 2, 3 tied together)
        dist.Reel1[0] = 0;
        dist.Reel1[1] = 9;
        dist.Reel1[2] = 0;
        dist.Reel1[3] = 9;
        dist.Reel1[4] = 0;
        dist.Reel1[5] = 9;
        dist.Reel1[6] = 0;
        dist.Reel1[7] = 6;
        dist.Reel1[8] = 0;
        dist.Reel1[14] = 0;

        // Reel 4 distribution
        dist.Reel4[0] = 10;
        dist.Reel4[1] = 10;
        dist.Reel4[2] = 10;
        dist.Reel4[3] = 10;
        dist.Reel4[4] = 10;
        dist.Reel4[5] = 10;
        dist.Reel4[6] = 8;
        dist.Reel4[7] = 6;
        dist.Reel4[8] = 0;

        var generator = new ColossalReelstripGenerator(303);
        var reels = generator.Generate(dist);

        Console.WriteLine("Generated Colossal Spin Reelset:\n");
        for (int r = 0; r < 5; r++)
        {
            string stripStr = string.Join(",", reels[r]);
            Console.WriteLine($"Reel {r} (Length = {reels[r].Count}):");
            Console.WriteLine(stripStr);
            Console.WriteLine();
        }

        Console.WriteLine("Excel CSV Format:");
        for (int r = 0; r < 5; r++)
        {
            Console.WriteLine($"Reel {r}\t" + string.Join(",", reels[r]));
        }
    }
}
