using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using ClosedXML.Excel;
using CashVortexGame;
using CashVortexGame.Config;
using SlotFramework.Models;
using SlotFramework.Utilities;

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

    public long XWheelTotalWin { get; set; }
    public int[] WheelReachHits { get; set; } = new int[4];
    public long[] WheelRtpWin { get; set; } = new long[4];

    public Dictionary<string, int> WheelPrizeHits { get; set; } = new();
    public Dictionary<string, long> WheelPrizeWins { get; set; } = new();

    public Dictionary<string, int> JackpotHits { get; set; } = new();
    public Dictionary<string, long> JackpotWins { get; set; } = new();

    // Lock & Slingo Bonus stats
    public int LockAndSlingoTriggers { get; set; }
    public long LockAndSlingoTotalWin { get; set; }
    public long LockAndSlingoTotalSpinsPlayed { get; set; }
    public long LockAndSlingoTotalSlingos { get; set; }
    public int LockAndSlingoFullHouses { get; set; }
    public int[] LockAndSlingoLadderHits { get; set; } = new int[13];

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
            if (pot.BonusName.StartsWith("XWheel:") || pot.BonusName.StartsWith("CenterWheel:"))
            {
                XWheelTotalWin += pot.Win;
                var parts = pot.BonusName.Split(':');
                if (parts.Length >= 3)
                {
                    string wStr = parts[1]; // e.g. "W1", "W2", "W3"
                    string prizeName = parts[2];
                    if (wStr.Length >= 2 && int.TryParse(wStr.Substring(1), out int wLevel) && wLevel >= 1 && wLevel <= 3)
                    {
                        WheelReachHits[wLevel]++;
                        WheelRtpWin[wLevel] += pot.Win;

                        string prizeKey = $"{wStr}:{prizeName}";
                        WheelPrizeHits[prizeKey] = WheelPrizeHits.GetValueOrDefault(prizeKey) + 1;
                        WheelPrizeWins[prizeKey] = WheelPrizeWins.GetValueOrDefault(prizeKey) + pot.Win;
                    }
                }
            }
            else if (pot.BonusName.Equals("Lock & Slingo", StringComparison.OrdinalIgnoreCase))
            {
                LockAndSlingoTriggers++;
                LockAndSlingoTotalWin += pot.Win;
                LockAndSlingoTotalSpinsPlayed += pot.SpinsPlayed;
                LockAndSlingoTotalSlingos += pot.CompletedSlingos;

                int slingoIdx = Math.Clamp(pot.CompletedSlingos, 0, 12);
                LockAndSlingoLadderHits[slingoIdx]++;
                if (slingoIdx == 12)
                {
                    LockAndSlingoFullHouses++;
                }
            }
        }

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

        bool trackFullStats = true;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--basic", StringComparison.OrdinalIgnoreCase))
            {
                trackFullStats = false;
            }
            else if (arg.Equals("--full", StringComparison.OrdinalIgnoreCase))
            {
                trackFullStats = true;
            }
            else if (arg.Equals("--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
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

            var config = CashVortexExcelLoader.Load(configSource);

            Console.WriteLine("\nLoaded Configuration Summary:");
            Console.WriteLine("-------------------------------------------------------------------------------------");
            Console.WriteLine($"Table Selections Count: {config.TableSelections.Count}");
            Console.WriteLine($"Special Symbol Types: {config.SpecialSymbolDefs.Count}");
            Console.WriteLine($"Jackpot Types: {config.JackpotCoins.Count}");
            Console.WriteLine($"Cash Strike Values Count: {config.CashStrikeValues.Count}");
            Console.WriteLine($"Cash Coin Values Count: {config.CashCoinValues.Count}");
            Console.WriteLine($"Mini Wheel Prizes: {config.MiniWheelPrizes.Count}");
            Console.WriteLine($"Mega Wheel Prizes: {config.MegaWheelPrizes.Count}");
            Console.WriteLine($"Ultra Wheel Prizes: {config.UltraWheelPrizes.Count}");
            Console.WriteLine($"Slingo Ladder Prizes: {config.SlingoLadderPrizes.Count}");
            Console.WriteLine($"Bonus Landing Base Factor: {config.BonusBaseFactor}");
            Console.WriteLine($"Bonus Outcome Types: {config.BonusOutcomeDefs.Count}");
            Console.WriteLine("-------------------------------------------------------------------------------------\n");

            int totalSpins = 1_000_000;
            int workerCount = Environment.ProcessorCount;
            int spinsPerWorker = totalSpins / workerCount;
            var workers = new CashVortexSimWorkerStats[workerCount];

            Console.WriteLine($"Generating real simulation results ({totalSpins:N0} spins)...\n");

            var sw = Stopwatch.StartNew();

            Parallel.For(0, workerCount, w =>
            {
                int seed = Guid.NewGuid().GetHashCode();
                var localRng = new FastRandom((uint)seed);
                var localEngine = new CashVortexSlotEngine(config);
                int spinsForThisWorker = (w == workerCount - 1) ? (totalSpins - spinsPerWorker * (workerCount - 1)) : spinsPerWorker;
                var localStats = new CashVortexSimWorkerStats(config);

                for (int i = 0; i < spinsForThisWorker; i++)
                {
                    var spinResult = localEngine.Spin(localRng);
                    localStats.Record(spinResult, localEngine);
                }

                workers[w] = localStats;
            });

            sw.Stop();
            Console.WriteLine($"Simulation finished in {sw.ElapsedMilliseconds} ms ({totalSpins / (sw.Elapsed.TotalSeconds):N0} spins/sec across {workerCount} CPU threads)!");

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
            long xWheelTotalWin = 0;

            int[] wheelReachHits = new int[4];
            long[] wheelRtpWin = new long[4];
            var wheelPrizeHits = new Dictionary<string, int>();
            var wheelPrizeWins = new Dictionary<string, long>();

            int lockAndSlingoTriggers = 0;
            long lockAndSlingoTotalWin = 0;
            long lockAndSlingoTotalSpinsPlayed = 0;
            long lockAndSlingoTotalSlingos = 0;
            int lockAndSlingoFullHouses = 0;
            int[] lockAndSlingoLadderHits = new int[13];

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
                xWheelTotalWin += w.XWheelTotalWin;

                lockAndSlingoTriggers += w.LockAndSlingoTriggers;
                lockAndSlingoTotalWin += w.LockAndSlingoTotalWin;
                lockAndSlingoTotalSpinsPlayed += w.LockAndSlingoTotalSpinsPlayed;
                lockAndSlingoTotalSlingos += w.LockAndSlingoTotalSlingos;
                lockAndSlingoFullHouses += w.LockAndSlingoFullHouses;
                for (int s = 0; s <= 12; s++)
                {
                    lockAndSlingoLadderHits[s] += w.LockAndSlingoLadderHits[s];
                }

                for (int l = 1; l <= 3; l++)
                {
                    wheelReachHits[l] += w.WheelReachHits[l];
                    wheelRtpWin[l] += w.WheelRtpWin[l];
                }

                foreach (var kvp in w.WheelPrizeHits)
                {
                    wheelPrizeHits[kvp.Key] = wheelPrizeHits.GetValueOrDefault(kvp.Key) + kvp.Value;
                }
                foreach (var kvp in w.WheelPrizeWins)
                {
                    wheelPrizeWins[kvp.Key] = wheelPrizeWins.GetValueOrDefault(kvp.Key) + kvp.Value;
                }

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
            double xWheelRtp = (double)xWheelTotalWin / (totalSpins * 100.0);
            double lockAndSlingoRtp = (double)lockAndSlingoTotalWin / (totalSpins * 100.0);
            double hitFreq = (double)winSpins / totalSpins;

            Console.WriteLine($"\nSimulation complete!");
            Console.WriteLine($"  - Total RTP: {totalRtp:P2}");
            Console.WriteLine($"    - Line Payout RTP: {lineWinRtp:P2}");
            Console.WriteLine($"    - Wheel Features Direct RTP: {xWheelRtp:P2}");
            Console.WriteLine($"    - Lock & Slingo™ Bonus RTP: {lockAndSlingoRtp:P2}");
            Console.WriteLine($"  - Hit Frequency: {hitFreq:P2}");
            Console.WriteLine($"  - Total Slingo Lines Completed: {totalSlingoLines:N0} (1 in {((double)totalSpins / Math.Max(1, totalSlingoLines)):F2} spins)");

            Console.WriteLine("\n[Lock & Slingo™ Bonus Breakdown]");
            double lnsTrigChance = (double)lockAndSlingoTriggers / totalSpins;
            double avgLnsWin = lockAndSlingoTriggers > 0 ? (double)lockAndSlingoTotalWin / (lockAndSlingoTriggers * 100.0) : 0;
            double avgLnsSpins = lockAndSlingoTriggers > 0 ? (double)lockAndSlingoTotalSpinsPlayed / lockAndSlingoTriggers : 0;
            double avgLnsSlingos = lockAndSlingoTriggers > 0 ? (double)lockAndSlingoTotalSlingos / lockAndSlingoTriggers : 0;

            Console.WriteLine($"  - Total Triggers: {lockAndSlingoTriggers:N0} (1 in {((double)totalSpins / Math.Max(1, lockAndSlingoTriggers)):F2} spins | {lnsTrigChance:P4})");
            Console.WriteLine($"  - Total Bonus RTP: {lockAndSlingoRtp:P2}");
            Console.WriteLine($"  - Average Bonus Win: {avgLnsWin:F2}x bet");
            Console.WriteLine($"  - Average Spins per Bonus: {avgLnsSpins:F2}");
            Console.WriteLine($"  - Average Slingos per Bonus: {avgLnsSlingos:F2}");
            Console.WriteLine($"  - Full House Hits (25 cells): {lockAndSlingoFullHouses:N0} ({((double)lockAndSlingoFullHouses / Math.Max(1, lockAndSlingoTriggers)):P2} of bonuses)");

            Console.WriteLine("\n[Special Symbol Hits]");
            Console.WriteLine($"  - Jackpot Coins: {jackpotCoinHits:N0}");
            Console.WriteLine($"  - Mini Vortexes: {miniVortexHits:N0}");
            Console.WriteLine($"  - Mega Vortexes: {megaVortexHits:N0}");
            Console.WriteLine($"  - Ultra Vortexes: {ultraVortexHits:N0}");
            Console.WriteLine($"  - Mini Strikes: {miniStrikeHits:N0}");
            Console.WriteLine($"  - Mega Strikes: {megaStrikeHits:N0}");
            Console.WriteLine($"  - Ultra Strikes: {ultraStrikeHits:N0}");
            Console.WriteLine($"  - X Wheel Triggers: {xWheelHits:N0} (Hit Chance: {((double)xWheelHits / totalSpins):P2})");

            Console.WriteLine("\n[Wheel Feature Breakdown]");
            for (int wL = 1; wL <= 3; wL++)
            {
                string wName = wL == 1 ? "Mini Wheel (Wheel 1)" : (wL == 2 ? "Mega Wheel (Wheel 2)" : "Ultra Wheel (Wheel 3)");
                int reach = wheelReachHits[wL];
                double reachSpinsChance = (double)reach / totalSpins;
                double reachTrigChance = xWheelHits > 0 ? (double)reach / xWheelHits : 0;
                double wRtp = (double)wheelRtpWin[wL] / (totalSpins * 100.0);
                Console.WriteLine($"  - {wName}: Reached = {reach:N0} times ({reachSpinsChance:P2} of spins | {reachTrigChance:P2} of triggers) | Direct RTP = {wRtp:P4}");
            }

            if (trackFullStats)
            {
                Console.WriteLine("\n[Wheel Detailed Prize Stats]");
                for (int wL = 1; wL <= 3; wL++)
                {
                    string wTag = $"W{wL}";
                    string wTitle = wL == 1 ? "Wheel 1 (Mini)" : (wL == 2 ? "Wheel 2 (Mega)" : "Wheel 3 (Ultra)");
                    int wheelTotalSpins = wheelReachHits[wL];
                    Console.WriteLine($"  --- {wTitle} Prizes ---");

                    var prizeList = wL == 1 ? config.MiniWheelPrizes : (wL == 2 ? config.MegaWheelPrizes : config.UltraWheelPrizes);
                    foreach (var prizeDef in prizeList)
                    {
                        string pKey = $"{wTag}:{prizeDef.PrizeString}";
                        int hits = wheelPrizeHits.GetValueOrDefault(pKey);
                        long win = wheelPrizeWins.GetValueOrDefault(pKey);
                        double hitChanceWheel = wheelTotalSpins > 0 ? (double)hits / wheelTotalSpins : 0;
                        double hitChanceSpins = (double)hits / totalSpins;
                        double prizeRtp = (double)win / (totalSpins * 100.0);

                        Console.WriteLine($"    * {prizeDef.PrizeString,-15}: Hits = {hits,6:N0} | Wheel Chance = {hitChanceWheel,7:P2} | Total Chance = {hitChanceSpins,7:P4} | RTP = {prizeRtp,7:P4}");
                    }
                }

                Console.WriteLine("\n[Lock & Slingo Ladder Achievements]");
                for (int s = 0; s <= 12; s++)
                {
                    if (s == 11) continue; // Ladder skips 11
                    int sHits = lockAndSlingoLadderHits[s];
                    double sChance = lockAndSlingoTriggers > 0 ? (double)sHits / lockAndSlingoTriggers : 0;
                    Console.WriteLine($"  - Slingo {s,2} Line(s): Hits = {sHits,6:N0} | {sChance,7:P2} of bonus rounds");
                }
            }

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
            ws.Cell(rowIdx, 1).Value = "Wheel Features Direct RTP"; ws.Cell(rowIdx, 2).Value = $"{xWheelRtp:P2}"; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Lock & Slingo™ Bonus RTP"; ws.Cell(rowIdx, 2).Value = $"{lockAndSlingoRtp:P2}"; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Hit Frequency"; ws.Cell(rowIdx, 2).Value = $"{hitFreq:P2}"; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Slingo Lines Completed"; ws.Cell(rowIdx, 2).Value = totalSlingoLines; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Lock & Slingo Triggers"; ws.Cell(rowIdx, 2).Value = lockAndSlingoTriggers; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Average Bonus Win (x bet)"; ws.Cell(rowIdx, 2).Value = $"{avgLnsWin:F2}"; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Full House Hits"; ws.Cell(rowIdx, 2).Value = lockAndSlingoFullHouses; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Jackpot Coin Hits"; ws.Cell(rowIdx, 2).Value = jackpotCoinHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Mini Vortex Hits"; ws.Cell(rowIdx, 2).Value = miniVortexHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Mega Vortex Hits"; ws.Cell(rowIdx, 2).Value = megaVortexHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Ultra Vortex Hits"; ws.Cell(rowIdx, 2).Value = ultraVortexHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Mini Strike Hits"; ws.Cell(rowIdx, 2).Value = miniStrikeHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Mega Strike Hits"; ws.Cell(rowIdx, 2).Value = megaStrikeHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "Ultra Strike Hits"; ws.Cell(rowIdx, 2).Value = ultraStrikeHits; rowIdx++;
            ws.Cell(rowIdx, 1).Value = "X Wheel Triggers"; ws.Cell(rowIdx, 2).Value = xWheelHits; rowIdx++;

            for (int wL = 1; wL <= 3; wL++)
            {
                ws.Cell(rowIdx, 1).Value = $"Wheel {wL} Reached Hits"; ws.Cell(rowIdx, 2).Value = wheelReachHits[wL]; rowIdx++;
                ws.Cell(rowIdx, 1).Value = $"Wheel {wL} Direct RTP"; ws.Cell(rowIdx, 2).Value = $"{((double)wheelRtpWin[wL] / (totalSpins * 100.0)):P4}"; rowIdx++;
            }

            ws.Columns().AdjustToContents();

            // Add X Wheel Details Worksheet
            var wsWheel = workbook.Worksheets.Add("Wheel Details");
            wsWheel.Cell(1, 1).Value = "Wheel";
            wsWheel.Cell(1, 2).Value = "Prize";
            wsWheel.Cell(1, 3).Value = "Hits";
            wsWheel.Cell(1, 4).Value = "Wheel Hit Chance %";
            wsWheel.Cell(1, 5).Value = "Total Spins Hit Chance %";
            wsWheel.Cell(1, 6).Value = "Direct Win";
            wsWheel.Cell(1, 7).Value = "Direct RTP %";
            wsWheel.Row(1).Style.Font.Bold = true;

            int wRow = 2;
            for (int wL = 1; wL <= 3; wL++)
            {
                string wTag = $"W{wL}";
                string wTitle = wL == 1 ? "Wheel 1 (Mini)" : (wL == 2 ? "Wheel 2 (Mega)" : "Wheel 3 (Ultra)");
                int wheelTotalSpins = wheelReachHits[wL];
                var prizeList = wL == 1 ? config.MiniWheelPrizes : (wL == 2 ? config.MegaWheelPrizes : config.UltraWheelPrizes);

                foreach (var prizeDef in prizeList)
                {
                    string pKey = $"{wTag}:{prizeDef.PrizeString}";
                    int hits = wheelPrizeHits.GetValueOrDefault(pKey);
                    long win = wheelPrizeWins.GetValueOrDefault(pKey);
                    double hitChanceWheel = wheelTotalSpins > 0 ? (double)hits / wheelTotalSpins : 0;
                    double hitChanceSpins = (double)hits / totalSpins;
                    double prizeRtp = (double)win / (totalSpins * 100.0);

                    wsWheel.Cell(wRow, 1).Value = wTitle;
                    wsWheel.Cell(wRow, 2).Value = prizeDef.PrizeString;
                    wsWheel.Cell(wRow, 3).Value = hits;
                    wsWheel.Cell(wRow, 4).Value = $"{hitChanceWheel:P2}";
                    wsWheel.Cell(wRow, 5).Value = $"{hitChanceSpins:P4}";
                    wsWheel.Cell(wRow, 6).Value = win;
                    wsWheel.Cell(wRow, 7).Value = $"{prizeRtp:P4}";
                    wRow++;
                }
            }
            wsWheel.Columns().AdjustToContents();

            // Add Lock & Slingo Details Worksheet
            var wsLns = workbook.Worksheets.Add("Lock & Slingo Details");
            wsLns.Cell(1, 1).Value = "Slingo Level";
            wsLns.Cell(1, 2).Value = "Hits";
            wsLns.Cell(1, 3).Value = "Bonus Round Hit Chance %";
            wsLns.Cell(1, 4).Value = "Total Spins Hit Chance %";
            wsLns.Row(1).Style.Font.Bold = true;

            int lnsRow = 2;
            for (int s = 0; s <= 12; s++)
            {
                if (s == 11) continue;
                int sHits = lockAndSlingoLadderHits[s];
                double sChance = lockAndSlingoTriggers > 0 ? (double)sHits / lockAndSlingoTriggers : 0;
                double sChanceSpins = (double)sHits / totalSpins;

                wsLns.Cell(lnsRow, 1).Value = $"Slingo {s}";
                wsLns.Cell(lnsRow, 2).Value = sHits;
                wsLns.Cell(lnsRow, 3).Value = $"{sChance:P2}";
                wsLns.Cell(lnsRow, 4).Value = $"{sChanceSpins:P4}";
                lnsRow++;
            }
            wsLns.Columns().AdjustToContents();

            workbook.SaveAs(resultsPath);
            Console.WriteLine("Results successfully written to Excel workbook!");
            Console.WriteLine("=========================================================================================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] Simulation failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.WriteLine("=========================================================================================");
        }
    }
}
