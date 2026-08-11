using System;
using System.IO;
using CashVortexGame;
using CashVortexGame.Config;
using SlotFramework.Utilities;

namespace CashVortexSimulator;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=========================================================================================");
        Console.WriteLine("                CASH VORTEX - TRIPLE POWER SIMULATOR                                     ");
        Console.WriteLine("=========================================================================================");

        var config = new CashVortexConfig();
        var engine = new CashVortexSlotEngine(config);
        var rng = new FastRandom(12345);

        Console.WriteLine("Running initial test simulation (1,000 spins)...");
        long totalWin = 0;
        for (int i = 0; i < 1000; i++)
        {
            var res = engine.Spin(rng);
            totalWin += res.TotalWin;
        }

        Console.WriteLine($"Simulation complete! Total Win across 1,000 spins: ${totalWin / 100.0:F2}");
        Console.WriteLine("=========================================================================================");
    }
}
