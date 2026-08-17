using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using CashVortexGame;
using CashVortexGame.Config;
using SlotFramework.Utilities;
using SlotFramework.Models;

namespace CashVortexSimulator;

public class CashVortexSimWorkerStats
{
    public long TotalWin { get; set; }
    public long TotalLineWin { get; set; }
    public int WinSpins { get; set; }
    public int TotalSlingoLinesCompleted { get; set; }

    public int JackpotCoinHits { get; set; }
    public int MiniVortexHits { get; set; }
    public int MegaVortexHits { get; set; }
    public int UltraVortexHits { get; set; }
    public int MiniStrikeHits { get; set; }
    public int MegaStrikeHits { get; set; }
    public int UltraStrikeHits { get; set; }
    public int XWheelHits { get; set; }

    public Dictionary<string, int> JackpotHits { get; set; } = new();
    public Dictionary<string, long> JackpotWins { get; set; } = new();

    public CashVortexSimWorkerStats(CashVortexConfig config)
    {
        foreach (var jp in config.JackpotCoins)
        {
            JackpotHits[jp.JackpotName] = 0;
            JackpotWins[jp.JackpotName] = 0;
        }
    }

    public void Record(SpinResult result, CashVortexSlotEngine engine)
    {
        TotalWin += result.TotalWin;

        if (result.TotalWin > 0)
        {
            WinSpins++;
        }

        foreach (var lw in result.LineWins)
        {
            TotalLineWin += lw.Payout;
            TotalSlingoLinesCompleted++;
        }

        foreach (var pot in result.TriggeredPotBonuses)
        {
            if (pot.BonusName == "Jackpot Bonus")
            {
                // Record Jackpot trigger
            }
        }

        // Count special symbol hits from current grid landing
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                var cell = engine.Grid[r, c];
                if (cell.JustLanded)
                {
                    switch (cell.Type)
                    {
                        case SymbolType.JackpotCoin:
                            JackpotCoinHits++;
                            if (cell.JackpotType != null)
                            {
                                JackpotHits[cell.JackpotType] = JackpotHits.GetValueOrDefault(cell.JackpotType) + 1;
                                long jpWin = (long)Math.Round(cell.CashValue * 100);
                                JackpotWins[cell.JackpotType] = JackpotWins.GetValueOrDefault(cell.JackpotType) + jpWin;
                            }
                            break;
                        case SymbolType.MiniVortex: MiniVortexHits++; break;
                        case SymbolType.MegaVortex: MegaVortexHits++; break;
                        case SymbolType.UltraVortex: UltraVortexHits++; break;
                        case SymbolType.MiniStrike: MiniStrikeHits++; break;
                        case SymbolType.MegaStrike: MegaStrikeHits++; break;
                        case SymbolType.UltraStrike: UltraStrikeHits++; break;
                        case SymbolType.XWheel: XWheelHits++; break;
                    }
                }
            }
        }
    }
}

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=========================================================================================");
        Console.WriteLine("                CASH VORTEX - TRIPLE POWER SIMULATOR                                     ");
        Console.WriteLine("=========================================================================================");

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string downloadsFolder = Path.Combine(userProfile, "Downloads");
        string localDefault = Path.Combine(downloadsFolder, "CashVortexTriplePower95.xlsx");
        if (!File.Exists(localDefault))
        {
            localDefault = "CashVortexTriplePower95.xlsx";
        }
        
        string configSource = File.Exists(localDefault) ? localDefault : CashVortexExcelLoader.DefaultGoogleSheetUrl;
        string resultsPath = Directory.Exists(downloadsFolder) 
            ? Path.Combine(downloadsFolder, "CashVortexTriplePower95_Results.xlsx")
            : "CashVortexTriplePower95_Results.xlsx";

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                configSource = args[++i];
            }
            else if (!arg.StartsWith("-"))
            {
                configSource = arg;
            }
        }

        try
        {
            if (SlotFramework.Utilities.GoogleSheetDownloader.IsOnlineSource(configSource))
            {
                Console.WriteLine($"Loading configuration online from Google Sheet: {configSource}...");
            }
            else
            {
                Console.WriteLine($"Loading configuration from local file: {configSource}...");
            }

            CashVortexConfig config = CashVortexExcelLoader.Load(configSource);

            Console.WriteLine("\nLoaded Configuration Summary:");
            Console.WriteLine(new string('-', 85));
            Console.WriteLine($"Table Selections Count: {config.TableSelections.Count}");
            Console.WriteLine($"Special Symbol Types: {config.SpecialSymbolDefs.Count}");
            Console.WriteLine($"Jackpot Types: {config.JackpotCoins.Count}");
            Console.WriteLine($"Cash Strike Values Count: {config.CashStrikeValues.Count}");
            Console.WriteLine($"Cash Coin Values Count: {config.CashCoinValues.Count}");
            Console.WriteLine(new string('-', 85));

            int totalSpins = 1000000;
            Console.WriteLine($"\nGenerating real simulation results ({totalSpins:N0} spins)...");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int workerCount = Math.Max(1, Environment.ProcessorCount);
            int baseSpinsPerWorker = totalSpins / workerCount;
            var workers = new CashVortexSimWorkerStats[workerCount];

            Parallel.For(0, workerCount, w =>
            {
                int spinsForThisWorker = (w == workerCount - 1)
                    ? baseSpinsPerWorker + (totalSpins - (baseSpinsPerWorker * workerCount))
                    : baseSpinsPerWorker;

                var localEngine = new CashVortexSlotEngine(config);
                var localRng = new FastRandom((ulong)(123456789012345UL + (ulong)w * 9876543210987UL + (ulong)DateTime.UtcNow.Ticks));
                var localStats = new CashVortexSimWorkerStats(config);

                for (int i = 0; i < spinsForThisWorker; i++)
                {
                    var spinResult = localEngine.Spin(localRng);
                    localStats.Record(spinResult, localEngine);
                }

                workers[w] = localStats;
            });

            sw.Stop();
            Console.WriteLine($"\nSimulation finished in {sw.ElapsedMilliseconds} ms ({totalSpins / (sw.Elapsed.TotalSeconds):N0} spins/sec across {workerCount} CPU threads)!");

            // Aggregating statistics
            long totalWin = 0;
            long totalLineWin = 0;
            int winSpins = 0;
            int totalSlingoLines = 0;

            int jackpotCoinHits = 0;
            int miniVortexHits = 0;
            int megaVortexHits = 0;
            int ultraVortexHits = 0;
            int miniStrikeHits = 0;
            int megaStrikeHits = 0;
            int ultraStrikeHits = 0;
            int xWheelHits = 0;

            var jackpotHits = new Dictionary<string, int>();
            var jackpotWins = new Dictionary<string, long>();
            foreach (var jp in config.JackpotCoins)
            {
                jackpotHits[jp.JackpotName] = 0;
                jackpotWins[jp.JackpotName] = 0;
            }

            foreach (var w in workers)
            {
                totalWin += w.TotalWin;
                totalLineWin += w.TotalLineWin;
                winSpins += w.WinSpins;
                totalSlingoLines += w.TotalSlingoLinesCompleted;

                jackpotCoinHits += w.JackpotCoinHits;
                miniVortexHits += w.MiniVortexHits;
                megaVortexHits += w.MegaVortexHits;
                ultraVortexHits += w.UltraVortexHits;
                miniStrikeHits += w.MiniStrikeHits;
                megaStrikeHits += w.MegaStrikeHits;
                ultraStrikeHits += w.UltraStrikeHits;
                xWheelHits += w.XWheelHits;

                foreach (var kvp in w.JackpotHits)
                {
                    jackpotHits[kvp.Key] = jackpotHits.GetValueOrDefault(kvp.Key) + kvp.Value;
                }
                foreach (var kvp in w.JackpotWins)
                {
                    jackpotWins[kvp.Key] = jackpotWins.GetValueOrDefault(kvp.Key) + kvp.Value;
                }
            }

            double totalRtp = (double)totalWin / (totalSpins * 100.0);
            double lineWinRtp = (double)totalLineWin / (totalSpins * 100.0);
            double hitFreq = (double)winSpins / totalSpins;

            Console.WriteLine($"\nSimulation complete!");
            Console.WriteLine($"  - Total RTP: {totalRtp:P2}");
            Console.WriteLine($"    - Line Payout RTP: {lineWinRtp:P2}");
            Console.WriteLine($"  - Hit Frequency: {hitFreq:P2}");
            Console.WriteLine($"  - Total Slingo Lines Completed: {totalSlingoLines:N0} (1 in {((double)totalSpins / Math.Max(1, totalSlingoLines)):F2} spins)");

            Console.WriteLine("\n[Special Symbol Hits]");
            Console.WriteLine($"  - Jackpot Coins: {jackpotCoinHits:N0}");
            Console.WriteLine($"  - Mini Vortexes: {miniVortexHits:N0}");
            Console.WriteLine($"  - Mega Vortexes: {megaVortexHits:N0}");
            Console.WriteLine($"  - Ultra Vortexes: {ultraVortexHits:N0}");
            Console.WriteLine($"  - Mini Strikes: {miniStrikeHits:N0}");
            Console.WriteLine($"  - Mega Strikes: {megaStrikeHits:N0}");
            Console.WriteLine($"  - Ultra Strikes: {ultraStrikeHits:N0}");
            Console.WriteLine($"  - X Wheel Triggers: {xWheelHits:N0}");

            Console.WriteLine("\n[Jackpot Breakdown]");
            foreach (var jp in config.JackpotCoins)
            {
                int hits = jackpotHits.GetValueOrDefault(jp.JackpotName);
                long win = jackpotWins.GetValueOrDefault(jp.JackpotName);
                double jpRtp = (double)win / (totalSpins * 100.0);
                Console.WriteLine($"  - {jp.JackpotName,-6} Jackpot ({jp.Multiplier}x): Hits = {hits,6:N0} | RTP = {jpRtp,8:P4}");
            }

            // Write results Excel
            Console.WriteLine($"\nWriting simulation results to: {resultsPath}");
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Simulation Results");

            ws.Cell(1, 1).Value = "Metric";
            ws.Cell(1, 2).Value = "Value";
            ws.Row(1).Style.Font.Bold = true;

            int rowIdx = 2;
            ws.Cell(rowIdx, 1).Value = "Game Name"; ws.Cell(rowIdx, 2).Value = config.GameName; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Total Spins"; ws.Cell(rowIdx, 2).Value = totalSpins; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Total RTP"; ws.Cell(rowIdx, 2).Value = $"{totalRtp:P2}"; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Line Win RTP"; ws.Cell(rowIdx, 2).Value = $"{lineWinRtp:P2}"; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Hit Frequency"; ws.Cell(rowIdx, 2).Value = $"{hitFreq:P2}"; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Slingo Lines Completed"; ws.Cell(rowIdx, 2).Value = totalSlingoLines; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Jackpot Coin Hits"; ws.Cell(rowIdx, 2).Value = jackpotCoinHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Mini Vortex Hits"; ws.Cell(rowIdx, 2).Value = miniVortexHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Mega Vortex Hits"; ws.Cell(rowIdx, 2).Value = megaVortexHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Ultra Vortex Hits"; ws.Cell(rowIdx, 2).Value = ultraVortexHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Mini Strike Hits"; ws.Cell(rowIdx, 2).Value = miniStrikeHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Mega Strike Hits"; ws.Cell(rowIdx, 2).Value = megaStrikeHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Ultra Strike Hits"; ws.Cell(rowIdx, 2).Value = ultraStrikeHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "X Wheel Triggers"; ws.Cell(rowIdx, 2).Value = xWheelHits; rowIdx++;

            ws.Columns().AdjustToContents();
            workbook.SaveAs(resultsPath);
            Console.WriteLine("Results successfully written to Excel workbook!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] Simulation failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine("=========================================================================================");
    }
}
