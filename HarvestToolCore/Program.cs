using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Harvest.Api;
using HarvestToolCore;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace HarvestToolCore
{
    class Program
    {
        // ReSharper disable once InconsistentNaming
        public const string HARVEST_DEVELOPER_TOKEN = "HARVEST_DEVELOPER_TOKEN";
        public const string MAPFILE = ".clientprojecttask.json";

        HarvestClient client;
        UserDetails me;
        Dictionary<string, Triple> shortCodeToTriple;
        Dictionary<Triple, string> tripleToShortCode;

        public static void Main(string[] args)
        {
            new Program().asyncMain(args).Wait();
        }

        public async Task asyncMain(string[] args)
        {
            if (!await VerifyConfiguration())
            {
                Console.WriteLine("Aborting");
                return;
            }
            if (args.Length>0)
            {
                await RunCommand(String.Join(" ",args));
            }
            else
            {
                Console.WriteLine("q to quit, h for help");
                await RunLoop();
            }
        }

        public async Task<bool> RunCommand(string command)
        {
            command = command.Trim();
            if (command == "q") return false;
            if (command == "h" || command == "?")
            {
                DoHelp();
                return true; 
            }

            if (command == "l" || command == "ld")
            {
                await DoList(DateTime.Now.Date, DateTime.Now.Date.AddDays(1));
                return true; 
            }

            if (command == "lw")
            {
                var daysAgo = (int) DateTime.Now.DayOfWeek;  // Sunday = 0
                // if its Sunday 0, i want to go back 6.
                // if its Monday 1, i want to go back 0
                daysAgo = (daysAgo == 0) ? 6 : (daysAgo - 1);
                await DoList(DateTime.Now.AddDays(-daysAgo), DateTime.Now.Date.AddDays(1));
                return true;
            }

            if (command == "lm")
            {
                await DoList(DateTime.Now.AddDays(-(DateTime.Now.Day-1)), DateTime.Now.Date.AddDays(1));
                return true;
            }


            Console.WriteLine("q to quit, h|? for help");
            return true; 
        }

        private void DoHelp()
        {
            Console.WriteLine("l|ld       list entries (for today)");
            Console.WriteLine("lw         list entries (for week, Mon-Sun)");
            Console.WriteLine("?|h        this help");
            Console.WriteLine("q          quit");
        }

        public async Task RunLoop()
        {
            bool cont = true; 
            while (cont)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow; 
                Console.Write("HarvestTool> ");
                Console.ForegroundColor = ConsoleColor.White; 
                var cmd = Console.ReadLine();
                cont = await RunCommand(cmd);
            }
        }

        public async Task<bool> VerifyConfiguration()
        {
            try
            {
                var token = Environment.GetEnvironmentVariable(HARVEST_DEVELOPER_TOKEN);
                if (String.IsNullOrWhiteSpace(token))
                    throw new NotSupportedException(
                        $"Must set environment variable {HARVEST_DEVELOPER_TOKEN}, see https://id.getharvest.com/developers to get one");

                client = HarvestClient.FromAccessToken("SunnyHarvestTool", token);

                var accounts = await client.GetAccountsAsync(); 
                if (accounts==null || accounts.Accounts==null) 
                    throw new NotSupportedException("Empty accounts package");
                if (accounts.Accounts.Length>1) throw new NotSupportedException("Do not handle multiple accounts yet");
                Console.WriteLine($"Using Account {accounts.Accounts[0].Id}={accounts.Accounts[0].Name}");
                client.DefaultAccountId = accounts.Accounts[0].Id;

                me = await client.GetMe();
                if (me == null) throw new Exception("unexpected null result");
                Console.WriteLine($"Logged in as {me.FirstName} {me.LastName} ({me.Id})");

                ReadMapFiles(); 

                return true;

            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not verify configuration - {ex.Message}");
                return false; 
            }
        }

        public void ReadMapFiles()
        {
            shortCodeToTriple = new Dictionary<string, Triple>();
            tripleToShortCode = new Dictionary<Triple, string>();
            if (System.IO.File.Exists(MAPFILE))
            {
                var contents = System.IO.File.ReadAllText(MAPFILE);
                shortCodeToTriple = JsonConvert.DeserializeObject<Dictionary<string, Triple>>(contents);
                foreach (var kv in shortCodeToTriple)
                {
                    tripleToShortCode[kv.Value] = kv.Key;
                }
            }
            Console.WriteLine($"Loaded {shortCodeToTriple.Count} short codes from {MAPFILE}");
        }

        public void SaveMapFile()
        {
            System.IO.File.WriteAllText(MAPFILE, 
                JsonConvert.SerializeObject(shortCodeToTriple, Formatting.Indented));
            Console.WriteLine($"Wrote {shortCodeToTriple.Count} entries to {MAPFILE}");
        }

        private string GetShortCode(IdNameModel client, IdNameModel project, IdNameModel task)
        {
            var k = new Triple { Client = client, Project = project, Task = task};
            if (tripleToShortCode.TryGetValue(k, out string v))
            {
                return v; 
            }

            return "???";
        }

        public async Task DoList(DateTime start, DateTime end)
        {
            try
            {
                // https://github.com/zVolodymyr/Harvest.Api


                var tasks = await client.GetTimeEntriesAsync(
                    fromDate: start,
                    toDate: end,
                    userId: me.Id
                );

                UpdateShortCodes(tasks.TimeEntries);

                var annotatedList = (from t in tasks.TimeEntries
                        select new {TimeEntry = t, ShortCode = GetShortCode(t.Client, t.Project, t.Task)})
                    .ToList(); 

                foreach (var dayEntries in annotatedList.GroupBy(x => x.TimeEntry.SpentDate).OrderBy(x=>x.Key))
                {
                    Console.WriteLine();
                    Console.WriteLine($"=== {dayEntries.Key:yyyy-MMM-dd ddd} ===");

                    foreach (var annotatedEntry in dayEntries.OrderBy(u => u.TimeEntry.StartedTime))
                    {

                        var startedTime = "";
                        if (annotatedEntry.TimeEntry.StartedTime.HasValue)
                        {
                            var x = annotatedEntry.TimeEntry.SpentDate.Add(annotatedEntry.TimeEntry.StartedTime.Value);
                            startedTime = x.ToString("hhmmtt").ToLowerInvariant();
                        }

                        var endedTime = "";
                        if (annotatedEntry.TimeEntry.EndedTime.HasValue)
                        {
                            var x = annotatedEntry.TimeEntry.SpentDate.Add(annotatedEntry.TimeEntry.EndedTime.Value);
                            endedTime = x.ToString("hhmmtt").ToLowerInvariant();
                        }

                        Console.WriteLine(
                            $"{startedTime}-{endedTime} {annotatedEntry.TimeEntry.Hours:F2} {annotatedEntry.ShortCode} {annotatedEntry.TimeEntry.Notes}");
                    }
                }

                Console.WriteLine();
                var annotatedByCodeList = annotatedList.GroupBy(a=>a.ShortCode).ToList();
                foreach (var annotatedByCode in annotatedByCodeList.OrderBy(x => x.Key))
                {
                    var timeEntry = annotatedByCode.First().TimeEntry;
                    var timeSpent = annotatedByCode.Sum(x => x.TimeEntry.Hours);
                    Console.WriteLine($"{annotatedByCode.Key} {timeSpent:F2} {timeEntry.Client.Name} {timeEntry.Project.Name} {timeEntry.Task.Name}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }

        private void UpdateShortCodes(IEnumerable<TimeEntry> entries)
        {

            var distinctTriples = entries.GroupBy(x =>
                    new Triple { Client = x.Client, Project = x.Project, Task = x.Task })
                .OrderByDescending(x=>x.Count())
                .ToList();

            int numAdded = 0; 
            foreach (var triple in distinctTriples)
            {
                if (!tripleToShortCode.ContainsKey(triple.Key))
                {
                    string alphaKey = null;

                    var client = triple.Key.Client.Name.Replace(" ", "").ToLowerInvariant();
                    var project = triple.Key.Project.Name.Replace(" ", "").ToLowerInvariant();
                    var task = triple.Key.Task.Name.Replace(" ", "").ToLowerInvariant();

                    var shortest = Math.Min(client.Length, Math.Min(project.Length, task.Length)) - 1;
                    for (var baseLength = 1; baseLength < shortest; baseLength++)
                    {
                        for (ushort what = 0; what < 8; what++)
                        {
                            var t = task.Substring(0, baseLength + ((what & 0x1) >> 0));
                            var p = project.Substring(0, baseLength + ((what & 0x2) >> 1));
                            var c = client.Substring(0, baseLength + ((what & 0x4) >> 2));
                            
                            alphaKey = c + p + t;
                            if (!shortCodeToTriple.ContainsKey(alphaKey)) goto found;
                        }
                    }
                    // if we get here, ??
                    throw new NotSupportedException("We got here");
                found:
                    shortCodeToTriple[alphaKey] = triple.Key;
                    tripleToShortCode[triple.Key] = alphaKey;
                    numAdded++; 
                }
            }

            if (numAdded > 0)
            {
                SaveMapFile();
            }
        }
    }
}
