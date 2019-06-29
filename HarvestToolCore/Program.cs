using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Harvest.Api;
using HarvestToolCore;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace HarvestToolCore
{
    public class AnnotatedTimeList
    {
        public TimeEntry TimeEntry { get; set; }
        public string ShortCode { get; set; }

        public override bool Equals(object value)
        {
            var type = value as AnnotatedTimeList;
            return (type != null) && EqualityComparer<TimeEntry>.Default.Equals(type.TimeEntry, TimeEntry) && EqualityComparer<string>.Default.Equals(type.ShortCode, ShortCode);
        }

        public override int GetHashCode()
        {
            int num = 0x7a2f0b42;
            num = (-1521134295 * num) + EqualityComparer<TimeEntry>.Default.GetHashCode(TimeEntry);
            return (-1521134295 * num) + EqualityComparer<string>.Default.GetHashCode(ShortCode);
        }
    }

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

            if (command == "gw")
            {
                // experimental
                var daysAgo = (int) DateTime.Now.DayOfWeek;  // Sunday = 0
                // if its Sunday 0, i want to go back 6.
                // if its Monday 1, i want to go back 0
                daysAgo = (daysAgo == 0) ? 6 : (daysAgo - 1);
                await DoGraph(DateTime.Now.AddDays(-daysAgo), DateTime.Now.Date.AddDays(1));
                return true;
            }


            Console.WriteLine("q to quit, h|? for help");
            return true; 
        }

        private async Task DoGraph(DateTime startDate, DateTime endDate)
        {
            var timeEntries = (await GetManyTimeEntries(
                    fromDate: startDate,
                    toDate: endDate,
                    accountId: client.DefaultAccountId,
                    userId: me.Id))
                .ToList();

            UpdateShortCodes(timeEntries);

            var annotatedList = (from t in timeEntries
                    select new AnnotatedTimeList {TimeEntry = t, ShortCode = GetShortCode(t.Client, t.Project, t.Task)})
                .ToList();

            // most days are .. within 12 hours. 
            // So if we have 2 rows per hour, that's 24 hours. 

            var g = new Griddle<DateTime, int, List<string>>();
            var g2 = new Griddle<DateTime, int, string>(); 

            var colSize = new Dictionary<DateTime, int>();
            var colHeaders = new Dictionary<DateTime, string>();
            var rowHeaders = new Dictionary<int, string>(); 

            foreach (var t in annotatedList.OrderBy(x=>x.TimeEntry.SpentDate).ThenBy(x=>x.TimeEntry.StartedTime))
            {
                var date = t.TimeEntry.SpentDate;
                if (t.TimeEntry.StartedTime.HasValue && t.TimeEntry.EndedTime.HasValue)
                {
                    var st = (int) Math.Round(t.TimeEntry.StartedTime.Value.TotalHours * 2, 0); // 0..47
                    var et = (int) Math.Round(t.TimeEntry.EndedTime.Value.TotalHours * 2, 0); // 0..47
                    for (int h = st; h <= et; h++)
                    {
                        g.Update(date, h, (x) =>
                        {
                            if (x == null) x = new List<string>();
                            x.Add(t.ShortCode);
                            return x;
                        });
                    }
                }
            }

            foreach (var d in g.Keys1.OrderBy(y => y))
            {
                colHeaders[d] = d.ToString("dd ddd");
                var biggest = colHeaders[d].Length; 
                foreach (var h in g.Keys2.OrderBy(x => x))
                {
                    var l = g.Get(d, h);
                    if (l != null)
                    {
                        var joined = String.Join(" ", l);
                        g2.Set(d,h,joined);
                        if (joined.Length > biggest) biggest = joined.Length;
                    }
                    else
                    {
                        g2.Set(d,h,"");
                    }
                }
                colSize[d] = biggest; 
            }

            foreach (var h in g.Keys2.OrderBy(x => x))
            {
                if (h % 2 == 0)
                {
                    rowHeaders[h] = (h / 2).ToString("00");
                }
                else
                {
                    rowHeaders[h] = "  "; 
                }
            }

            // column Headers
            Console.Write("  ");
            foreach (var d in g.Keys1.OrderBy(x => x))
            {
                Console.Write("|");
                Console.Write(colHeaders[d].PadRight(colSize[d]));
            }
            Console.WriteLine();
            Console.Write("--");
            foreach (var d in g.Keys1.OrderBy(x => x))
            {
                Console.Write("+");
                Console.Write("-".PadRight(colSize[d],'-'));
            }
            Console.WriteLine();
            foreach (var h in g.Keys2.OrderBy(x => x))
            {
                Console.Write(rowHeaders[h]);
                foreach (var d in g.Keys1.OrderBy(x => x))
                {
                    Console.Write("|");
                    var x = g2.Get(d, h) ?? "";
                    Console.Write(x.PadRight(colSize[d]));
                }

                Console.WriteLine(); 
            }

            ConsoleWriteLegend(annotatedList);
        }

        // Thoughts on Grammar
        // s                                  restart current timer. 
        // s now doing this other thing       provide a message (everything after =) 
        // s -5 email                        start it 5 minutes ago, with message
        // s .bok -5 bigger text
        // s -5 .bok something                both of these start it 5 minutes ago with message

        // k                               stop current timer
        // k -5 hey                             stop current timer 5 minutes ago and change text to hey
        // sk .bok -30,20 quick meeting          start something 30 minutes ago, end in 20 minutes, quick meeting
        // sk .bok 8:20am,3:10pm stuff         start and stop a timer from 8:20 to 3:10pm with a message

        // c some text                      update last timer text
        // c +additional text               add more text to last timer text
        // c -5                            change it to start at -5 minutes ago
        // c 2:30                          change it to start at 2:30
        // c ,3:10                         change it to end at 3:10    (the : makes it an absolute time)
        // s internal/misc/dev             start timer for internal/misc/dev

        
        // pk -5 .bok phone call     Parallel start (ie, don't edit other things) start 5 minutes ago stop now, phone call

        // Analysis: 
        
        // First word is command -- c, s, k, sk, p, pk
        
        // additional args are parsed: 
        //    IS IT TIME? 
        //      ,E             == "ending time spec" 
        //      S,E            == starting + ending time spec
        //      -1 -2 -5 -10   == "time relative" to now in minutes
        //      -1h            == relative to now in hours
        //      6              == relative to anchor (if E, then anchor = start) 
        //      8:20  12:30    == "absolute".  The : gives it away. AM/PM is guessed
        //                        if not specified by whichever is closer to now

        //    IS IT A CLIENT/PROJECT/TASK       
        //      .xxx           == shortcodes start with a dot
        //      x/y/z          == client/project/task have two slashes.  use .contains() to narrow

        //    If its not the above, then its part of the message. 

        private void DoHelp()
        {
            Console.WriteLine("l|ld       list entries (for today)");
            Console.WriteLine("lw         list entries (for week, Mon-Sun)");
            Console.WriteLine("lm         list entries (for Month, 1st-now) (long)");
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


                var timeEntries = (await GetManyTimeEntries(
                    fromDate: start, 
                    toDate: end, 
                    accountId: client.DefaultAccountId, 
                    userId: me.Id))
                    .ToList();

                UpdateShortCodes(timeEntries);

                var annotatedList = (from t in timeEntries
                        select new AnnotatedTimeList {TimeEntry = t, ShortCode = GetShortCode(t.Client, t.Project, t.Task)})
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

                ConsoleWriteLegend(annotatedList);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }

        private static void ConsoleWriteLegend(List<AnnotatedTimeList> annotatedList)
        {
            Console.WriteLine();
            var annotatedByCodeList = annotatedList.GroupBy(a => a.ShortCode).ToList();
            foreach (var annotatedByCode in annotatedByCodeList.OrderBy(x => x.Key))
            {
                var timeEntry = annotatedByCode.First().TimeEntry;
                var timeSpent = annotatedByCode.Sum(x => x.TimeEntry.Hours);
                Console.WriteLine(
                    $"{annotatedByCode.Key} {timeSpent:F2} {timeEntry.Client.Name} {timeEntry.Project.Name} {timeEntry.Task.Name}");
            }

            Console.WriteLine();
            var total = annotatedList.Sum(a => a.TimeEntry.Hours);
            Console.WriteLine($"TOTAL: {total:F2}");
        }

        private async Task<IEnumerable<TimeEntry>> GetManyTimeEntries(long? userId = null, long? clientId = null, long? projectId = null, bool? isBilled = null, DateTime? updatedSince = null, DateTime? fromDate = null, DateTime? toDate = null, int? perPage = null, long? accountId = null)
        {
            // We have an API throttle that blocks accounts emitting more than 100 requests per 15 seconds. 
            var waitTime = 10000 / 100; 

            List<TimeEntry> manyTimeEntries = new List<TimeEntry>();
            int? page = 1;
            do
            {
                var timeEntriesResponse = await client.GetTimeEntriesAsync(
                    // these are listed out so that any changes in positions .. don't matter.
                    accountId: accountId,
                    userId: userId,
                    clientId: clientId,
                    projectId: projectId,
                    isBilled: isBilled,
                    updatedSince: updatedSince,
                    fromDate: fromDate,
                    toDate: toDate,
                    perPage: perPage,
                    page: page
                );
                manyTimeEntries.AddRange(timeEntriesResponse.TimeEntries);
                page = timeEntriesResponse.NextPage;
                if (page != null) await Task.Delay(waitTime);
            } while (page != null);

            return manyTimeEntries;
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
