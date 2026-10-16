#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
#:property TreatWarningsAsErrors=false

// Spike S1 (RR-020, docs/08 §3): what real OpenF1 data looks like for the cases the algorithms must handle.
// Throwaway exploration code, not the production client (that is RR-040/RR-041).
//
// Raw responses are cached under .cache/openf1/s1/ (gitignored, never committed: ADR-0008), so a second run makes no
// network calls and prints identical numbers. CSVs for plotting go to reports/s1/ (also gitignored).
//
// Usage:
//   dotnet run scripts/s1-explore.cs -- races                       2023–2025 race sessions
//   dotnet run scripts/s1-explore.cs -- red-flags                   races with a red flag in race_control
//   dotnet run scripts/s1-explore.cs -- analyse <key> [<key> …]     statistics per race, CSVs, summary
//   dotnet run scripts/s1-explore.cs -- fixture <key> <name> <from-utc> <to-utc> <driver> <driver>
//                                                                   cut a trimmed recorded fixture (docs/06 §9)

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string cacheRoot = Path.Combine(".cache", "openf1", "s1");
string outRoot = Path.Combine("reports", "s1");
string command = args.Length > 0 ? args[0] : "help";
string[] rest = args.Skip(1).ToArray();

using OpenF1 api = new(cacheRoot);
switch (command)
{
    case "races":
        foreach (Session s in await Races(api))
        {
            Console.WriteLine($"{s.SessionKey}\t{s.DateStart:yyyy-MM-dd}\t{s.CircuitShortName}\t{s.CountryName}");
        }

        break;

    case "red-flags":
        foreach (Session s in await Races(api))
        {
            RaceControl[] red = await api.GetArray<RaceControl>($"race_control?session_key={s.SessionKey}&flag=RED");
            if (red.Length > 0)
            {
                Console.WriteLine($"{s.SessionKey}\t{s.DateStart:yyyy-MM-dd}\t{s.CircuitShortName}\t{red.Length} RED\t{red[0].Date:HH:mm:ss}\t{red[0].Message}");
            }
        }

        break;

    case "analyse":
        Directory.CreateDirectory(outRoot);
        foreach (string key in rest)
        {
            Race race = await Race.LoadAsync(api, int.Parse(key, CultureInfo.InvariantCulture));
            Analysis.Run(race, outRoot);
        }

        Analysis.WriteFetches(api, outRoot);
        break;

    case "fixture":
        await Fixture.CutAsync(api, int.Parse(rest[0], CultureInfo.InvariantCulture), rest[1],
            DateTimeOffset.Parse(rest[2], CultureInfo.InvariantCulture), DateTimeOffset.Parse(rest[3], CultureInfo.InvariantCulture),
            [.. rest.Skip(4).Select(d => int.Parse(d, CultureInfo.InvariantCulture))]);
        break;

    default:
        Console.WriteLine("Usage: races | red-flags | analyse <key>… | fixture <key> <name> <from> <to> <driver> <driver>");
        break;
}

Console.Error.WriteLine($"[{api.NetworkRequests} network requests, {api.CacheHits} from cache]");

static async Task<Session[]> Races(OpenF1 api)
{
    List<Session> all = [];
    foreach (int year in new[] { 2023, 2024, 2025 })
    {
        all.AddRange(await api.GetArray<Session>($"sessions?session_type=Race&year={year}"));
    }

    return [.. all.Where(s => s.SessionName == "Race" && s.IsCancelled != true && s.DateEnd < DateTimeOffset.UtcNow.AddHours(-1))
        .OrderBy(s => s.DateStart)];
}

// ---------------------------------------------------------------------------------------------------------------------
// A minimal OpenF1 client: free-tier rate limits (3 req/s, 30 req/min, docs/04 §3.2), retries, and a raw response cache.

sealed class OpenF1 : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://api.openf1.org/v1/"), Timeout = TimeSpan.FromSeconds(120) };
    private readonly string _cache;
    private readonly Queue<DateTime> _sent = new();

    public OpenF1(string cache)
    {
        _cache = cache;
        Directory.CreateDirectory(cache);
    }

    public int NetworkRequests { get; private set; }

    public int CacheHits { get; private set; }

    public List<FetchRecord> Fetches { get; } = [];

    public async Task<T[]> GetArray<T>(string pathAndQuery) =>
        JsonSerializer.Deserialize<T[]>(await GetAsync(pathAndQuery), Json.Options) ?? [];

    public async Task<byte[]> GetAsync(string pathAndQuery)
    {
        string endpoint = pathAndQuery.Split('?')[0];
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pathAndQuery)))[..16].ToLowerInvariant();
        string body = Path.Combine(_cache, $"{endpoint}_{hash}.json");
        string meta = Path.ChangeExtension(body, ".meta.json");
        if (File.Exists(body) && File.Exists(meta))
        {
            CacheHits++;
            FetchRecord cached = JsonSerializer.Deserialize<FetchRecord>(File.ReadAllText(meta), Json.Options)!;
            Fetches.Add(cached with { FromCache = true });
            return File.ReadAllBytes(body);
        }

        for (int attempt = 1; ; attempt++)
        {
            await ThrottleAsync();
            Stopwatch watch = Stopwatch.StartNew();
            NetworkRequests++;
            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(pathAndQuery);
            }
            catch (Exception e) when (attempt < 5 && e is HttpRequestException or TaskCanceledException)
            {
                Console.Error.WriteLine($"  {pathAndQuery}: {e.GetType().Name} after {watch.Elapsed.TotalSeconds:F1} s, retrying");
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
                continue;
            }

            using (response)
            {
                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                watch.Stop();
                bool empty = response.StatusCode == HttpStatusCode.NotFound && Encoding.UTF8.GetString(bytes).Contains("No results", StringComparison.OrdinalIgnoreCase);
                if (response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    TimeSpan wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10 * attempt);
                    Console.Error.WriteLine($"  {pathAndQuery}: HTTP {(int)response.StatusCode}, waiting {wait.TotalSeconds:F0} s");
                    if (attempt >= 6)
                    {
                        throw new HttpRequestException($"{pathAndQuery}: HTTP {(int)response.StatusCode}");
                    }

                    await Task.Delay(wait);
                    continue;
                }

                if (!response.IsSuccessStatusCode && !empty)
                {
                    throw new HttpRequestException($"{pathAndQuery}: HTTP {(int)response.StatusCode} {Encoding.UTF8.GetString(bytes)}");
                }

                if (empty)
                {
                    bytes = "[]"u8.ToArray();
                }

                FetchRecord record = new(pathAndQuery, (int)response.StatusCode, bytes.Length, watch.ElapsedMilliseconds, DateTimeOffset.UtcNow, false);
                File.WriteAllBytes(body, bytes);
                File.WriteAllText(meta, JsonSerializer.Serialize(record, Json.Options));
                Fetches.Add(record);
                Console.Error.WriteLine($"  GET {pathAndQuery}: {bytes.Length:N0} B in {watch.Elapsed.TotalSeconds:F1} s");
                return bytes;
            }
        }
    }

    // Sliding windows: at most 3 requests in any second and 30 in any minute.
    private async Task ThrottleAsync()
    {
        while (true)
        {
            DateTime now = DateTime.UtcNow;
            while (_sent.Count > 0 && now - _sent.Peek() > TimeSpan.FromSeconds(61))
            {
                _sent.Dequeue();
            }

            DateTime[] recent = [.. _sent];
            int lastSecond = recent.Count(t => now - t < TimeSpan.FromSeconds(1.05));
            if (recent.Length < 30 && lastSecond < 3)
            {
                _sent.Enqueue(now);
                return;
            }

            TimeSpan wait = recent.Length >= 30
                ? recent[0].AddSeconds(61) - now
                : recent[^3].AddSeconds(1.05) - now;
            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(50));
        }
    }

    public void Dispose() => _http.Dispose();
}

sealed record FetchRecord(string Url, int Status, long Bytes, long Milliseconds, DateTimeOffset FetchedUtc, bool FromCache);

static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        WriteIndented = false,
    };
}

// ---------------------------------------------------------------------------------------------------------------------
// OpenF1 records: only the fields S1 reads (docs/04 §2).

sealed record Session(int SessionKey, string SessionType, string SessionName, DateTimeOffset DateStart, DateTimeOffset DateEnd,
    int MeetingKey, int CircuitKey, string CircuitShortName, string CountryName, string Location, int Year, bool? IsCancelled);

sealed record Meeting(int MeetingKey, string MeetingName);

sealed record Driver(int DriverNumber, string NameAcronym, string TeamName, string? TeamColour);

sealed record Location(DateTimeOffset Date, int DriverNumber, double X, double Y, double Z);

sealed record Lap(int DriverNumber, int LapNumber, DateTimeOffset? DateStart, double? LapDuration, bool? IsPitOutLap);

sealed record Pit(DateTimeOffset Date, int DriverNumber, int LapNumber, double? PitDuration, double? LaneDuration, double? StopDuration);

sealed record RaceControl(DateTimeOffset Date, string? Category, string? Flag, string? Scope, int? LapNumber, int? DriverNumber, string? Message);

sealed record IntervalRecord(DateTimeOffset Date, int DriverNumber, JsonElement GapToLeader, JsonElement Interval);

sealed record PositionRecord(DateTimeOffset Date, int DriverNumber, int Position);

sealed record Result(int DriverNumber, int? Position, int? NumberOfLaps, bool? Dnf, bool? Dns, bool? Dsq);

sealed record LocationFetch(int Driver, long Bytes, long Milliseconds, int Samples);

sealed class Race
{
    // Official lap lengths in metres (FIA/F1 published figures, current layouts) for the units check (docs/03 §3).
    public static readonly Dictionary<string, double> OfficialLengthMetres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sakhir"] = 5412, ["Jeddah"] = 6174, ["Melbourne"] = 5278, ["Suzuka"] = 5807, ["Shanghai"] = 5451,
        ["Miami"] = 5412, ["Imola"] = 4909, ["Monte Carlo"] = 3337, ["Montreal"] = 4361, ["Catalunya"] = 4657,
        ["Spielberg"] = 4318, ["Silverstone"] = 5891, ["Hungaroring"] = 4381, ["Spa-Francorchamps"] = 7004,
        ["Zandvoort"] = 4259, ["Monza"] = 5793, ["Baku"] = 6003, ["Singapore"] = 4940, ["Austin"] = 5513,
        ["Mexico City"] = 4304, ["Interlagos"] = 4309, ["Las Vegas"] = 6201, ["Lusail"] = 5419, ["Yas Marina Circuit"] = 5281,
    };

    public required Session Session { get; init; }

    public required string MeetingName { get; init; }

    public required Driver[] Drivers { get; init; }

    public required Dictionary<int, Location[]> Locations { get; init; } // per driver, in received order

    public required List<LocationFetch> LocationFetches { get; init; }

    public required Lap[] Laps { get; init; }

    public required Pit[] Pits { get; init; }

    public required RaceControl[] RaceControl { get; init; }

    public required IntervalRecord[] Intervals { get; init; }

    public required PositionRecord[] Positions { get; init; }

    public required Result[] Results { get; init; }

    public string Name => $"{Session.Year} {MeetingName} ({Session.CircuitShortName}, {Session.SessionKey})";

    public static async Task<Race> LoadAsync(OpenF1 api, int key)
    {
        Session session = (await api.GetArray<Session>($"sessions?session_key={key}")).Single();
        Meeting meeting = (await api.GetArray<Meeting>($"meetings?meeting_key={session.MeetingKey}")).Single();
        Driver[] drivers = await api.GetArray<Driver>($"drivers?session_key={key}");
        Dictionary<int, Location[]> locations = [];
        List<LocationFetch> fetches = [];
        foreach (Driver d in drivers.OrderBy(d => d.DriverNumber))
        {
            int before = api.Fetches.Count;
            locations[d.DriverNumber] = await api.GetArray<Location>($"location?session_key={key}&driver_number={d.DriverNumber}");
            FetchRecord f = api.Fetches[before];
            fetches.Add(new LocationFetch(d.DriverNumber, f.Bytes, f.Milliseconds, locations[d.DriverNumber].Length));
        }

        return new Race
        {
            Session = session,
            MeetingName = meeting.MeetingName,
            Drivers = drivers,
            Locations = locations,
            LocationFetches = fetches,
            Laps = await api.GetArray<Lap>($"laps?session_key={key}"),
            Pits = await api.GetArray<Pit>($"pit?session_key={key}"),
            RaceControl = await api.GetArray<RaceControl>($"race_control?session_key={key}"),
            Intervals = await api.GetArray<IntervalRecord>($"intervals?session_key={key}"),
            Positions = await api.GetArray<PositionRecord>($"position?session_key={key}"),
            Results = await api.GetArray<Result>($"session_result?session_key={key}"),
        };
    }
}

// ---------------------------------------------------------------------------------------------------------------------

static class Analysis
{
    private const double MaxSpeed = 120;        // m/s, docs/03 §9
    private const double MaxGap = 2.0;          // s, docs/03 §9

    public static void Run(Race race, string outRoot)
    {
        string dir = Path.Combine(outRoot, race.Session.SessionKey.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        Console.WriteLine();
        Console.WriteLine($"## {race.Name}");
        Console.WriteLine($"Scheduled {race.Session.DateStart:yyyy-MM-dd HH:mm:ss}Z – {race.Session.DateEnd:HH:mm:ss}Z; {race.Drivers.Length} drivers; " +
            $"laps {race.Laps.Length}, pit {race.Pits.Length}, race_control {race.RaceControl.Length}, intervals {race.Intervals.Length}, position {race.Positions.Length}");

        (DateTimeOffset start, DateTimeOffset finish) = RacingWindow(race);
        double unitsPerMetre = Units(race, dir);
        LocationQuality(race, dir, start, finish, unitsPerMetre);
        RaceStart(race, dir, unitsPerMetre);
        PitStops(race, dir, unitsPerMetre);
        RaceControlWording(race, dir);
        IntervalsAndPositions(race, dir);
        GapProxy(race, dir);
        OrderProxy(race, dir);
        Responses(race);
    }

    // Lap 1 start (median over drivers) to the chequered flag.
    private static (DateTimeOffset Start, DateTimeOffset Finish) RacingWindow(Race race)
    {
        DateTimeOffset start = Median(race.Laps.Where(l => l.LapNumber == 1 && l.DateStart is not null).Select(l => l.DateStart!.Value));
        DateTimeOffset finish = race.RaceControl.Where(r => r.Flag == "CHEQUERED").Select(r => r.Date).DefaultIfEmpty(race.Session.DateEnd).Min();
        return (start, finish);
    }

    // Path length of clean laps compared with the official lap length (docs/03 §3: units believed to be decimetres).
    private static double Units(Race race, string dir)
    {
        if (!Race.OfficialLengthMetres.TryGetValue(race.Session.CircuitShortName, out double official))
        {
            Console.WriteLine($"Units: no official length for {race.Session.CircuitShortName}; assuming 10 units/m");
            return 10;
        }

        HashSet<(int, int)> pitLaps = [.. race.Pits.Select(p => (p.DriverNumber, p.LapNumber))];
        double[] durations = [.. race.Laps.Where(l => l.LapDuration is > 0).Select(l => l.LapDuration!.Value)];
        double medianLap = Median(durations);
        List<(int Driver, int Lap, double Units)> laps = [];
        foreach (IGrouping<int, Lap> byDriver in race.Laps.GroupBy(l => l.DriverNumber))
        {
            Lap[] ordered = [.. byDriver.Where(l => l.DateStart is not null).OrderBy(l => l.LapNumber)];
            Location[] samples = Sorted(race, byDriver.Key);
            for (int i = 0; i + 1 < ordered.Length; i++)
            {
                Lap lap = ordered[i], next = ordered[i + 1];
                if (next.LapNumber != lap.LapNumber + 1 || lap.LapNumber < 3 || lap.IsPitOutLap == true || pitLaps.Contains((byDriver.Key, lap.LapNumber))
                    || lap.LapDuration is not double d || d > medianLap * 1.03)
                {
                    continue;
                }

                double length = PathLength(samples, lap.DateStart!.Value, next.DateStart!.Value);
                if (length > 0)
                {
                    laps.Add((byDriver.Key, lap.LapNumber, length));
                }
            }
        }

        File.WriteAllLines(Path.Combine(dir, "lap_lengths.csv"),
            ["driver,lap,path_units,units_per_metre", .. laps.Select(l => $"{l.Driver},{l.Lap},{l.Units:F1},{l.Units / official:F4}")]);
        double[] ratios = [.. laps.Select(l => l.Units / official)];
        double median = Median(ratios);
        Console.WriteLine($"Units: {laps.Count} clean laps (≤ 103 % of the median lap, no pit, lap ≥ 3); path length / official {official:F0} m = " +
            $"median {median:F3}, p5 {Percentile(ratios, 0.05):F3}, p95 {Percentile(ratios, 0.95):F3} units/m");
        return median;
    }

    private static double PathLength(Location[] samples, DateTimeOffset from, DateTimeOffset to)
    {
        double length = 0;
        Location? previous = null;
        foreach (Location s in samples)
        {
            if (s.Date < from || s.Date > to || IsZero(s))
            {
                continue;
            }

            if (previous is not null)
            {
                length += Math.Sqrt(Sq(s.X - previous.X) + Sq(s.Y - previous.Y));
            }

            previous = s;
        }

        return length;
    }

    private static void LocationQuality(Race race, string dir, DateTimeOffset start, DateTimeOffset finish, double unitsPerMetre)
    {
        List<string> rows = ["driver,samples,racing_hours,dt_mean,dt_p95,zeros,duplicates,out_of_order,dropouts_over_2s,longest_dropout_s,over_max_speed"];
        double totalHours = 0;
        int zeros = 0, duplicates = 0, outOfOrder = 0, overSpeed = 0, samplesInRace = 0, rawOverSpeedShortDt = 0, robustOverSpeed = 0;
        List<double> intervals = [], dropouts = [], robustSpeeds = [], frozen = [];
        int wholeSessionZeros = 0, zerosInsideActivity = 0;
        foreach ((int _, Location[] all) in race.Locations)
        {
            Location[] sorted = [.. all.OrderBy(s => s.Date)];
            int first = Array.FindIndex(sorted, s => !IsZero(s)), last = Array.FindLastIndex(sorted, s => !IsZero(s));
            wholeSessionZeros += sorted.Count(IsZero);
            zerosInsideActivity += first < 0 ? 0 : sorted[first..(last + 1)].Count(IsZero);
        }
        List<(double Seconds, bool Suspended, int Samples)> zeroRuns = [];
        (DateTimeOffset From, DateTimeOffset To)? red = RedFlagPeriod(race);
        foreach ((int driver, Location[] received) in race.Locations.OrderBy(p => p.Key))
        {
            int driverOutOfOrder = 0;
            for (int i = 1; i < received.Length; i++)
            {
                if (received[i].Date < received[i - 1].Date)
                {
                    driverOutOfOrder++;
                }
            }

            DateTimeOffset driverFinish = DriverFinish(race, driver, finish);
            Location[] window = [.. received.Where(s => s.Date >= start && s.Date <= driverFinish).OrderBy(s => s.Date)];
            if (window.Length < 2)
            {
                continue;
            }

            int driverZeros = window.Count(IsZero), driverDuplicates = 0, driverOverSpeed = 0;
            List<double> dts = [], driverDropouts = [];
            Location? last = null;
            for (int i = 1; i < window.Length; i++)
            {
                double dt = (window[i].Date - window[i - 1].Date).TotalSeconds;
                if (dt == 0)
                {
                    driverDuplicates++;
                    continue;
                }

                dts.Add(dt);
                if (dt > MaxGap)
                {
                    driverDropouts.Add(dt);
                }
            }

            // Raw consecutive pairs (timestamp jitter inflates these), split by Δt; and a robust speed over pairs at least
            // 0.5 s apart, which is what a plausibility gate should use.
            Location? anchor = null;
            foreach (Location s in window.Where(s => !IsZero(s)))
            {
                if (last is not null)
                {
                    double dt = (s.Date - last.Date).TotalSeconds;
                    double metres = Math.Sqrt(Sq(s.X - last.X) + Sq(s.Y - last.Y)) / unitsPerMetre;
                    if (dt > 0 && dt <= MaxGap && metres / dt > MaxSpeed)
                    {
                        driverOverSpeed++;
                        if (dt < 0.2)
                        {
                            rawOverSpeedShortDt++;
                        }
                    }
                }

                last = s;
                if (anchor is null)
                {
                    anchor = s;
                    continue;
                }

                double span = (s.Date - anchor.Date).TotalSeconds;
                if (span >= 0.5)
                {
                    double v = Math.Sqrt(Sq(s.X - anchor.X) + Sq(s.Y - anchor.Y)) / unitsPerMetre / span;
                    if (span <= MaxGap)
                    {
                        robustSpeeds.Add(v);
                        if (v > MaxSpeed)
                        {
                            robustOverSpeed++;
                        }
                    }

                    anchor = s;
                }
            }

            // Frozen positions: the same non-zero (x, y) repeated for ≥ 1 s while the car covers ground (≥ 30 m/s across
            // the run): a dropout that keeps the cadence but not the data.
            Location[] moving = [.. window.Where(s => !IsZero(s))];
            for (int i = 0; i + 1 < moving.Length; i++)
            {
                int j = i;
                while (j + 1 < moving.Length && moving[j + 1].X == moving[i].X && moving[j + 1].Y == moving[i].Y)
                {
                    j++;
                }

                if (j > i && j + 1 < moving.Length)
                {
                    double held = (moving[j + 1].Date - moving[i].Date).TotalSeconds;
                    double metres = Math.Sqrt(Sq(moving[j + 1].X - moving[i].X) + Sq(moving[j + 1].Y - moving[i].Y)) / unitsPerMetre;
                    if (held >= 1 && metres / held >= 30)
                    {
                        frozen.Add(held);
                    }
                }

                i = j;
            }

            // (0,0,0) runs: OpenF1 fills missing positions with zeros at the normal cadence rather than leaving gaps.
            for (int i = 0; i < window.Length; i++)
            {
                if (!IsZero(window[i]))
                {
                    continue;
                }

                int j = i;
                while (j + 1 < window.Length && IsZero(window[j + 1]))
                {
                    j++;
                }

                DateTimeOffset end = j + 1 < window.Length ? window[j + 1].Date : window[j].Date;
                double length = (end - window[i].Date).TotalSeconds;
                bool suspended = red is var (rf, rt) && window[i].Date >= rf && window[i].Date <= rt;
                zeroRuns.Add((length, suspended, j - i + 1));
                i = j;
            }

            double hours = (window[^1].Date - window[0].Date).TotalHours;
            totalHours += hours;
            zeros += driverZeros;
            duplicates += driverDuplicates;
            outOfOrder += driverOutOfOrder;
            overSpeed += driverOverSpeed;
            samplesInRace += window.Length;
            intervals.AddRange(dts);
            dropouts.AddRange(driverDropouts);
            rows.Add($"{driver},{window.Length},{hours:F3},{dts.Average():F3},{Percentile(dts, 0.95):F3},{driverZeros},{driverDuplicates},{driverOutOfOrder}," +
                $"{driverDropouts.Count},{(driverDropouts.Count > 0 ? driverDropouts.Max() : 0):F1},{driverOverSpeed}");
        }

        File.WriteAllLines(Path.Combine(dir, "location_quality.csv"), rows);
        Console.WriteLine($"Location ({totalHours:F1} car-hours of racing, {samplesInRace:N0} samples): Δt mean {intervals.Average():F3} s " +
            $"({1 / intervals.Average():F2} Hz), p50 {Percentile(intervals, 0.5):F3}, p95 {Percentile(intervals, 0.95):F3}, p99.9 {Percentile(intervals, 0.999):F2}");
        Console.WriteLine($"  per car-hour: (0,0,0) {zeros / totalHours:F2}, duplicates {duplicates / totalHours:F2}, out-of-order {outOfOrder / totalHours:F2}, " +
            $"dropouts > {MaxGap} s {dropouts.Count / totalHours:F2}, over {MaxSpeed} m/s {overSpeed / totalHours:F2}");
        Console.WriteLine($"  dropout lengths: 2–5 s {dropouts.Count(d => d <= 5)}, 5–30 s {dropouts.Count(d => d is > 5 and <= 30)}, " +
            $"30–120 s {dropouts.Count(d => d is > 30 and <= 120)}, > 120 s {dropouts.Count(d => d > 120)}; longest {(dropouts.Count > 0 ? dropouts.Max() : 0):F1} s");
        Console.WriteLine($"  raw pairs over {MaxSpeed} m/s: {overSpeed} ({rawOverSpeedShortDt} with Δt < 0.2 s); over pairs ≥ 0.5 s apart: {robustOverSpeed} " +
            $"({robustOverSpeed / totalHours:F2} per car-hour); speed p99.9 {Percentile(robustSpeeds, 0.999):F1} m/s, max {robustSpeeds.DefaultIfEmpty(0).Max():F1} m/s");
        var racing = zeroRuns.Where(z => !z.Suspended).ToList();
        Console.WriteLine($"  (0,0,0) runs: {zeroRuns.Count} ({zeroRuns.Sum(z => z.Samples):N0} samples), {zeroRuns.Count(z => z.Suspended)} during the red-flag suspension; " +
            $"outside it {racing.Count} runs ({racing.Count / totalHours:F2} per car-hour), lengths 1 sample {racing.Count(z => z.Samples == 1)}, " +
            $"≤ 2 s {racing.Count(z => z.Seconds <= 2)}, 2–30 s {racing.Count(z => z.Seconds is > 2 and <= 30)}, > 30 s {racing.Count(z => z.Seconds > 30)}; " +
            $"longest {racing.Select(z => z.Seconds).DefaultIfEmpty(0).Max():F1} s");
        Console.WriteLine($"  whole session: {wholeSessionZeros:N0} (0,0,0) samples, {zerosInsideActivity:N0} of them between a car's first and last real position");
        foreach ((int d, Location[] all) in race.Locations.OrderBy(p => p.Key))
        {
            Location[] real = [.. all.Where(s => !IsZero(s)).OrderBy(s => s.Date)];
            Result? result = race.Results.FirstOrDefault(r => r.DriverNumber == d);
            bool lap1 = race.Laps.Any(l => l.DriverNumber == d && l.LapNumber == 1 && l.DateStart is not null);
            if (real.Length == 0 || !lap1 || result?.Dns == true)
            {
                Console.WriteLine($"  car {d}: {real.Length:N0} real positions{(real.Length > 0 ? $" {real[0].Date:HH:mm:ss}–{real[^1].Date:HH:mm:ss}" : "")}, lap 1 {(lap1 ? "yes" : "no")}, " +
                    $"laps {race.Laps.Count(l => l.DriverNumber == d)}, result dns {result?.Dns} dnf {result?.Dnf} laps {result?.NumberOfLaps}");
            }
        }
        Console.WriteLine($"  frozen positions (same x,y ≥ 1 s while moving ≥ 30 m/s): {frozen.Count} ({frozen.Count / totalHours:F2} per car-hour); " +
            $"1–2 s {frozen.Count(f => f < 2)}, 2–5 s {frozen.Count(f => f is >= 2 and < 5)}, ≥ 5 s {frozen.Count(f => f >= 5)}; longest {frozen.DefaultIfEmpty(0).Max():F1} s");
    }

    // From the red flag until racing laps resume: the first lap start more than 5 minutes after it.
    private static (DateTimeOffset From, DateTimeOffset To)? RedFlagPeriod(Race race)
    {
        RaceControl? redFlag = race.RaceControl.Where(r => r.Flag == "RED").MinBy(r => r.Date);
        if (redFlag is null)
        {
            return null;
        }

        DateTimeOffset resume = race.Laps.Where(l => l.DateStart > redFlag.Date.AddMinutes(5)).Select(l => l.DateStart!.Value).DefaultIfEmpty(race.Session.DateEnd).Min();
        return (redFlag.Date, resume);
    }

    // A retired car's racing ends at its last lap; everyone else's at the chequered flag (+ 3 minutes for the last finisher).
    private static DateTimeOffset DriverFinish(Race race, int driver, DateTimeOffset chequered)
    {
        Result? result = race.Results.FirstOrDefault(r => r.DriverNumber == driver);
        if (result?.Dnf == true || result?.Dns == true)
        {
            Lap? lastLap = race.Laps.Where(l => l.DriverNumber == driver && l.DateStart is not null).MaxBy(l => l.LapNumber);
            return lastLap?.DateStart?.AddSeconds(lastLap.LapDuration ?? 0) ?? chequered;
        }

        return chequered.AddMinutes(3);
    }

    // Q2: lap 1 date_start, race-control messages, and motion onset from the grid.
    private static void RaceStart(Race race, string dir, double unitsPerMetre)
    {
        DateTimeOffset[] lap1 = [.. race.Laps.Where(l => l.LapNumber == 1 && l.DateStart is not null).Select(l => l.DateStart!.Value).Order()];
        if (lap1.Length == 0)
        {
            Console.WriteLine("Race start: no lap 1 date_start");
            return;
        }

        DateTimeOffset lap1Median = Median(lap1);
        List<(int Driver, DateTimeOffset Onset)> onsets = [];
        foreach ((int driver, Location[] received) in race.Locations)
        {
            Location[] s = [.. received.Where(x => !IsZero(x) && x.Date > lap1Median.AddMinutes(-10) && x.Date < lap1Median.AddMinutes(2)).OrderBy(x => x.Date)];
            double[] speed = Speeds(s, unitsPerMetre);

            // The grid stop: the last stationary stretch (≥ 2 s under 0.5 m/s) ending before lap 1 median + 30 s.
            int stillEnd = -1;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i].Date > lap1Median.AddSeconds(30))
                {
                    break;
                }

                if (speed[i] < 0.5)
                {
                    int j = i;
                    while (j + 1 < s.Length && speed[j + 1] < 0.5)
                    {
                        j++;
                    }

                    if ((s[j].Date - s[i].Date).TotalSeconds >= 2)
                    {
                        stillEnd = j;
                    }

                    i = j;
                }
            }

            if (stillEnd >= 0)
            {
                int k = stillEnd + 1;
                while (k < s.Length && speed[k] < 3)
                {
                    k++;
                }

                if (k < s.Length)
                {
                    onsets.Add((driver, s[k].Date));
                }
            }
        }

        File.WriteAllLines(Path.Combine(dir, "race_start.csv"),
            ["driver,lap1_date_start,motion_onset", .. race.Locations.Keys.Order().Select(d =>
                $"{d},{race.Laps.FirstOrDefault(l => l.DriverNumber == d && l.LapNumber == 1)?.DateStart:O},{onsets.FirstOrDefault(o => o.Driver == d).Onset:O}")]);
        Console.WriteLine($"Race start: scheduled {race.Session.DateStart:HH:mm:ss}; lap 1 date_start median {lap1Median:HH:mm:ss.fff} " +
            $"(spread {(lap1[^1] - lap1[0]).TotalSeconds:F1} s over {lap1.Length} drivers; {lap1.Count(t => t == lap1Median)} identical to the median)");
        foreach (Lap odd in race.Laps.Where(l => l.LapNumber == 1 && l.DateStart is not null && l.DateStart != lap1Median))
        {
            Console.WriteLine($"  lap 1 outlier: driver {odd.DriverNumber} date_start {odd.DateStart:HH:mm:ss.fff} ({(odd.DateStart!.Value - lap1Median).TotalSeconds:F1} s), duration {odd.LapDuration}");
        }

        int[] noLap1 = [.. race.Drivers.Select(d => d.DriverNumber).Where(d => !race.Laps.Any(l => l.DriverNumber == d && l.LapNumber == 1 && l.DateStart is not null))];
        if (noLap1.Length > 0)
        {
            Console.WriteLine($"  no lap 1 date_start: drivers {string.Join(", ", noLap1)}");
        }
        if (onsets.Count > 0)
        {
            DateTimeOffset onsetMedian = Median(onsets.Select(o => o.Onset));
            double[] offsets = [.. onsets.Select(o => (o.Onset - onsetMedian).TotalSeconds)];
            Console.WriteLine($"  motion onset from the grid: median {onsetMedian:HH:mm:ss.fff} ({onsets.Count} drivers, p5..p95 {Percentile(offsets, 0.05):F1}..{Percentile(offsets, 0.95):F1} s); " +
                $"lap 1 date_start − onset = {(lap1Median - onsetMedian).TotalSeconds:F1} s");
        }

        foreach (RaceControl r in race.RaceControl.Where(r => r.Date > lap1Median.AddMinutes(-15) && r.Date < lap1Median.AddMinutes(2)).OrderBy(r => r.Date))
        {
            Console.WriteLine($"  rc {(r.Date - lap1Median).TotalSeconds,7:F1} s  {r.Category}/{r.Flag}: {r.Message}");
        }
    }

    // Q1: what pit.date marks, judged against the speed profile around each stop.
    private static void PitStops(Race race, string dir, double unitsPerMetre)
    {
        List<string> rows = ["driver,lap,pit_date,entry,stop_start,stop_end,exit,lane_duration,stop_duration,pit_minus_entry,pit_minus_stop,pit_minus_exit,lane_measured,stop_measured"];
        List<double> vsEntry = [], vsStop = [], vsExit = [], laneError = [], stopError = [], laneEntryError = [];
        (DateTimeOffset From, DateTimeOffset To)? red = RedFlagPeriod(race);
        int duringRed = 0;
        foreach (Pit pit in race.Pits.OrderBy(p => p.Date))
        {
            if (red is var (rf, rt) && pit.Date >= rf.AddMinutes(-1) && pit.Date <= rt.AddMinutes(2))
            {
                duringRed++; // cars wait in the pit lane during a suspension: not a pit stop
                continue;
            }

            if (!race.Locations.TryGetValue(pit.DriverNumber, out Location[]? received))
            {
                continue;
            }

            Location[] s = [.. received.Where(x => !IsZero(x) && x.Date > pit.Date.AddSeconds(-90) && x.Date < pit.Date.AddSeconds(90)).OrderBy(x => x.Date)];
            if (s.Length < 20)
            {
                continue;
            }

            double[] speed = Smooth(Speeds(s, unitsPerMetre));

            // The stop: the stationary stretch nearest pit.date.
            int best = -1;
            for (int i = 0; i < s.Length; i++)
            {
                if (speed[i] < 1 && (best < 0 || Math.Abs((s[i].Date - pit.Date).TotalSeconds) < Math.Abs((s[best].Date - pit.Date).TotalSeconds)))
                {
                    best = i;
                }
            }

            if (best < 0)
            {
                continue;
            }

            int stopStart = best, stopEnd = best;
            while (stopStart > 0 && speed[stopStart - 1] < 1)
            {
                stopStart--;
            }

            while (stopEnd + 1 < s.Length && speed[stopEnd + 1] < 1)
            {
                stopEnd++;
            }

            // The pit-lane limiter speed: the median over 15 s to 4 s before the stop. Entry and exit are where the car
            // leaves that band (5 m/s above it) on either side.
            double[] approach = [.. Enumerable.Range(0, s.Length)
                .Where(i => s[i].Date >= s[stopStart].Date.AddSeconds(-15) && s[i].Date <= s[stopStart].Date.AddSeconds(-4)).Select(i => speed[i])];
            if (approach.Length < 5)
            {
                continue;
            }

            double limiter = Median(approach);
            int entry = stopStart;
            while (entry > 0 && speed[entry - 1] <= limiter + 5)
            {
                entry--;
            }

            int exit = stopEnd;
            while (exit + 1 < s.Length && speed[exit + 1] <= limiter + 5)
            {
                exit++;
            }

            double e = (pit.Date - s[entry].Date).TotalSeconds, st = (pit.Date - s[stopStart].Date).TotalSeconds, x = (pit.Date - s[exit].Date).TotalSeconds;
            double laneMeasured = (s[exit].Date - s[entry].Date).TotalSeconds, stopMeasured = (s[stopEnd].Date - s[stopStart].Date).TotalSeconds;
            vsEntry.Add(e);
            vsStop.Add(st);
            vsExit.Add(x);
            if (pit.LaneDuration is double lane)
            {
                laneError.Add(laneMeasured - lane);
                laneEntryError.Add((pit.Date.AddSeconds(-lane) - s[entry].Date).TotalSeconds); // if date marks the exit, date − lane ≈ entry
            }

            if (pit.StopDuration is double stop)
            {
                stopError.Add(stopMeasured - stop);
            }

            rows.Add($"{pit.DriverNumber},{pit.LapNumber},{pit.Date:O},{s[entry].Date:O},{s[stopStart].Date:O},{s[stopEnd].Date:O},{s[exit].Date:O}," +
                $"{pit.LaneDuration},{pit.StopDuration},{e:F1},{st:F1},{x:F1},{laneMeasured:F1},{stopMeasured:F1}");
        }

        File.WriteAllLines(Path.Combine(dir, "pit_stops.csv"), rows);
        if (vsStop.Count == 0)
        {
            Console.WriteLine($"Pit: {race.Pits.Length} records, none matched to a stop");
            return;
        }

        if (laneEntryError.Count > 0)
        {
            Console.WriteLine($"  (pit.date − lane_duration) − measured entry: median {Median(laneEntryError):F1} s (p10..p90 {Percentile(laneEntryError, 0.1):F1}..{Percentile(laneEntryError, 0.9):F1})");
        }

        Console.WriteLine($"Pit ({vsStop.Count} of {race.Pits.Length} matched to a stop, {duringRed} during the red flag left out): pit.date − entry median {Median(vsEntry):F1} s (p10..p90 {Percentile(vsEntry, 0.1):F1}..{Percentile(vsEntry, 0.9):F1}); " +
            $"− stop start {Median(vsStop):F1} s; − exit {Median(vsExit):F1} s");
        if (laneError.Count > 0)
        {
            Console.WriteLine($"  measured lane time − lane_duration: median {Median(laneError):F1} s (p10..p90 {Percentile(laneError, 0.1):F1}..{Percentile(laneError, 0.9):F1}, n {laneError.Count})");
        }

        if (stopError.Count > 0)
        {
            Console.WriteLine($"  measured stop time − stop_duration: median {Median(stopError):F1} s (n {stopError.Count})");
        }
    }

    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);

    private static void RaceControlWording(Race race, string dir)
    {
        File.WriteAllLines(Path.Combine(dir, "race_control.csv"),
            ["date,category,flag,scope,lap,driver,message", .. race.RaceControl.OrderBy(r => r.Date).Select(r =>
                $"{r.Date:O},{r.Category},{r.Flag},{r.Scope},{r.LapNumber},{r.DriverNumber},\"{r.Message?.Replace("\"", "\"\"", StringComparison.Ordinal)}\"")]);
        Console.WriteLine($"Race control categories: {string.Join(", ", race.RaceControl.GroupBy(r => r.Category ?? "null").Select(g => $"{g.Key} {g.Count()}"))}; " +
            $"flags: {string.Join(", ", race.RaceControl.Where(r => r.Flag is not null).GroupBy(r => r.Flag).Select(g => $"{g.Key} {g.Count()}"))}");
        foreach (RaceControl r in race.RaceControl.OrderBy(r => r.Date).Where(r =>
            r.Category is "SafetyCar" or "SessionStatus" || r.Flag is "RED" or "CHEQUERED" ||
            (RedFlagPeriod(race) is var (rf, rt) && r.Date >= rf && r.Date <= rt.AddMinutes(3)) ||
            (r.Message ?? "").Contains("RESUM", StringComparison.Ordinal) || (r.Message ?? "").Contains("RESTART", StringComparison.Ordinal) ||
            (r.Flag == "GREEN" && r.Scope == "Track")))
        {
            Console.WriteLine($"  {r.Date:HH:mm:ss} lap {r.LapNumber,2} {r.Category}/{r.Flag}/{r.Scope}: {r.Message}");
        }
    }

    private static readonly Regex LapsBehind = new(@"^\+(\d+) LAPS?$", RegexOptions.Compiled);

    private static void IntervalsAndPositions(Race race, string dir)
    {
        Dictionary<string, int> forms = [];
        foreach (IntervalRecord i in race.Intervals)
        {
            foreach (JsonElement v in new[] { i.GapToLeader, i.Interval })
            {
                string form = v.ValueKind switch
                {
                    JsonValueKind.Number => "number",
                    JsonValueKind.Null or JsonValueKind.Undefined => "null",
                    JsonValueKind.String when LapsBehind.IsMatch(v.GetString()!) => $"\"+N LAP{(v.GetString()!.EndsWith('S') ? "S" : "")}\"",
                    JsonValueKind.String => $"other string \"{v.GetString()}\"",
                    _ => v.ValueKind.ToString(),
                };
                forms[form] = forms.GetValueOrDefault(form) + 1;
            }
        }

        List<double> cadence = [];
        foreach (IGrouping<int, IntervalRecord> g in race.Intervals.GroupBy(i => i.DriverNumber))
        {
            DateTimeOffset[] t = [.. g.Select(i => i.Date).Order()];
            for (int k = 1; k < t.Length; k++)
            {
                cadence.Add((t[k] - t[k - 1]).TotalSeconds);
            }
        }

        DateTimeOffset[] changes = [.. race.Positions.Select(p => p.Date).Order()];
        List<double> changeGaps = [];
        for (int k = 1; k < changes.Length; k++)
        {
            changeGaps.Add((changes[k] - changes[k - 1]).TotalSeconds);
        }

        Console.WriteLine($"Intervals: {race.Intervals.Length:N0} records; value forms {string.Join(", ", forms.OrderByDescending(f => f.Value).Select(f => $"{f.Key} {f.Value:N0}"))}");
        Console.WriteLine($"  per-driver cadence: p50 {Percentile(cadence, 0.5):F2} s, p95 {Percentile(cadence, 0.95):F2} s, max {cadence.Max():F0} s");
        Console.WriteLine($"Position: {race.Positions.Length:N0} records ({race.Positions.Count(p => p.Date < RacingWindow(race).Start):N0} before lap 1); " +
            $"between consecutive records p50 {Percentile(changeGaps, 0.5):F1} s, p95 {Percentile(changeGaps, 0.95):F1} s");
    }

    // Q6 proxy: OpenF1's intervals.gap_to_leader against a lap-line gap from laps.date_start (docs/03 §11 exclusions).
    private static void GapProxy(Race race, string dir)
    {
        Dictionary<int, IntervalRecord[]> intervals = race.Intervals.GroupBy(i => i.DriverNumber).ToDictionary(g => g.Key, g => g.OrderBy(i => i.Date).ToArray());
        List<(DateTimeOffset From, DateTimeOffset To)> neutral = NeutralisedPeriods(race);
        HashSet<(int, int)> pitLaps = [.. race.Pits.SelectMany(p => new[] { (p.DriverNumber, p.LapNumber), (p.DriverNumber, p.LapNumber + 1) })];
        List<string> rows = ["driver,lap,lap_gap,intervals_gap,difference,excluded"];
        List<double> differences = [];
        foreach (IGrouping<int, Lap> lapGroup in race.Laps.Where(l => l.DateStart is not null).GroupBy(l => l.LapNumber))
        {
            Lap leader = lapGroup.MinBy(l => l.DateStart)!;
            foreach (Lap lap in lapGroup.Where(l => l != leader))
            {
                double lapGap = (lap.DateStart!.Value - leader.DateStart!.Value).TotalSeconds;
                string excluded = lap.LapNumber <= 2 ? "lap 1-2"
                    : pitLaps.Contains((lap.DriverNumber, lap.LapNumber)) || lap.IsPitOutLap == true || pitLaps.Contains((leader.DriverNumber, leader.LapNumber)) ? "pit"
                    : neutral.Any(n => lap.DateStart >= n.From.AddSeconds(-lapGap - 120) && leader.DateStart <= n.To) ? "neutralised"
                    : "";
                IntervalRecord? nearest = Nearest(intervals.GetValueOrDefault(lap.DriverNumber), lap.DateStart.Value, 3);
                double? official = nearest?.GapToLeader.ValueKind == JsonValueKind.Number ? nearest.GapToLeader.GetDouble() : null;
                if (official is null && excluded == "")
                {
                    excluded = nearest is null ? "no interval within 3 s" : "lapped or null";
                }

                double difference = official is double o ? lapGap - o : double.NaN;
                rows.Add($"{lap.DriverNumber},{lap.LapNumber},{lapGap:F3},{official:F3},{difference:F3},{excluded}");
                if (excluded == "")
                {
                    differences.Add(Math.Abs(difference));
                }
            }
        }

        File.WriteAllLines(Path.Combine(dir, "gap_proxy.csv"), rows);
        Console.WriteLine($"Gap proxy (lap-line gap from laps.date_start vs nearest intervals.gap_to_leader within 3 s, green-flag laps ≥ 3, no pit laps): " +
            $"n {differences.Count}, |difference| median {Median(differences):F3} s, p95 {Percentile(differences, 0.95):F3} s, " +
            $"share ≤ 0.5 s {differences.Count(d => d <= 0.5) * 100.0 / Math.Max(1, differences.Count):F1} %");
    }

    // Order proxy: rank at the lap line (who crossed the start of lap n first) against the position step function, with
    // the docs/03 §11 tolerance: a match if the official position equals it at any instant within ± 5 s.
    private static void OrderProxy(Race race, string dir)
    {
        Dictionary<int, PositionRecord[]> positions = race.Positions.GroupBy(p => p.DriverNumber).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Date).ToArray());
        List<(DateTimeOffset From, DateTimeOffset To)> neutral = NeutralisedPeriods(race);
        HashSet<(int, int)> pitLaps = [.. race.Pits.SelectMany(p => new[] { (p.DriverNumber, p.LapNumber), (p.DriverNumber, p.LapNumber + 1) })];
        int compared = 0, matched = 0;
        foreach (IGrouping<int, Lap> lapGroup in race.Laps.Where(l => l.DateStart is not null && l.LapNumber >= 3).GroupBy(l => l.LapNumber))
        {
            // Everyone who started lap n, plus cars a lap or more up who started it earlier: rank by crossing time.
            Lap[] crossing = [.. lapGroup.OrderBy(l => l.DateStart)];
            for (int rank = 0; rank < crossing.Length; rank++)
            {
                Lap lap = crossing[rank];
                if (pitLaps.Contains((lap.DriverNumber, lap.LapNumber)) || lap.IsPitOutLap == true || neutral.Any(n => lap.DateStart >= n.From && lap.DateStart <= n.To.AddSeconds(120)))
                {
                    continue;
                }

                // Everyone ahead on the road crossed the start of lap n before this car, cars laps ahead included.
                int derived = rank + 1;
                PositionRecord[]? official = positions.GetValueOrDefault(lap.DriverNumber);
                if (official is null)
                {
                    continue;
                }

                compared++;
                if (PositionAt(official, lap.DateStart!.Value.AddSeconds(-5)) == derived || PositionAt(official, lap.DateStart.Value) == derived
                    || PositionAt(official, lap.DateStart.Value.AddSeconds(5)) == derived
                    || official.Any(p => p.Position == derived && Math.Abs((p.Date - lap.DateStart.Value).TotalSeconds) <= 5))
                {
                    matched++;
                }
            }
        }

        Console.WriteLine($"Order proxy (rank at the lap line vs position, ±5 s, green-flag laps ≥ 3, no pit laps): {matched} of {compared} = {matched * 100.0 / Math.Max(1, compared):F1} %");
    }

    private static int? PositionAt(PositionRecord[] steps, DateTimeOffset t) => steps.LastOrDefault(p => p.Date <= t)?.Position;

    // SC, VSC and red flag, from deployment to the matching end message (or the next green), docs/03 §11 exclusions.
    private static List<(DateTimeOffset From, DateTimeOffset To)> NeutralisedPeriods(Race race)
    {
        List<(DateTimeOffset, DateTimeOffset)> periods = [];
        DateTimeOffset? open = null;
        foreach (RaceControl r in race.RaceControl.OrderBy(r => r.Date))
        {
            string m = r.Message ?? "";
            bool starts = m.Contains("SAFETY CAR DEPLOYED", StringComparison.Ordinal) || r.Flag == "RED";
            bool ends = m.Contains("SAFETY CAR IN THIS LAP", StringComparison.Ordinal) || m.Contains("SAFETY CAR ENDING", StringComparison.Ordinal)
                || (r.Flag == "GREEN" && r.Scope == "Track" && open is not null);
            if (starts && open is null)
            {
                open = r.Date;
            }
            else if (ends && open is DateTimeOffset from)
            {
                periods.Add((from, r.Date.AddSeconds(90)));
                open = null;
            }
        }

        if (open is DateTimeOffset unfinished)
        {
            periods.Add((unfinished, race.Session.DateEnd));
        }

        return periods;
    }

    private static void Responses(Race race)
    {
        LocationFetch[] f = [.. race.LocationFetches];
        Console.WriteLine($"Location responses (whole session per driver): {f.Sum(x => x.Bytes) / 1e6:F1} MB in total; per driver {f.Min(x => x.Bytes) / 1e6:F1}–{f.Max(x => x.Bytes) / 1e6:F1} MB, " +
            $"{f.Min(x => x.Samples):N0}–{f.Max(x => x.Samples):N0} samples; time {f.Min(x => x.Milliseconds) / 1000.0:F1}–{f.Max(x => x.Milliseconds) / 1000.0:F1} s (median {Median(f.Select(x => (double)x.Milliseconds)) / 1000:F1} s), measured when fetched");
    }

    public static void WriteFetches(OpenF1 api, string outRoot) =>
        File.WriteAllLines(Path.Combine(outRoot, "fetches.csv"),
            ["url,status,bytes,ms,fetched_utc,from_cache", .. api.Fetches.Select(f => $"\"{f.Url}\",{f.Status},{f.Bytes},{f.Milliseconds},{f.FetchedUtc:O},{f.FromCache}")]);

    // Helpers --------------------------------------------------------------------------------------------------------

    public static bool IsZero(Location s) => s.X == 0 && s.Y == 0 && s.Z == 0;

    private static Location[] Sorted(Race race, int driver) =>
        race.Locations.TryGetValue(driver, out Location[]? s) ? [.. s.OrderBy(x => x.Date)] : [];

    private static double[] Speeds(Location[] s, double unitsPerMetre)
    {
        double[] speed = new double[s.Length];
        for (int i = 1; i < s.Length; i++)
        {
            double dt = (s[i].Date - s[i - 1].Date).TotalSeconds;
            speed[i] = dt > 0 ? Math.Sqrt(Sq(s[i].X - s[i - 1].X) + Sq(s[i].Y - s[i - 1].Y)) / unitsPerMetre / dt : speed[i - 1];
        }

        if (s.Length > 1)
        {
            speed[0] = speed[1];
        }

        return speed;
    }

    private static double[] Smooth(double[] v)
    {
        double[] r = new double[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(v.Length - 1, i + 1);
            r[i] = v[a..(b + 1)].Average();
        }

        return r;
    }

    private static IntervalRecord? Nearest(IntervalRecord[]? series, DateTimeOffset t, double withinSeconds)
    {
        if (series is null || series.Length == 0)
        {
            return null;
        }

        int lo = 0, hi = series.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (series[mid].Date < t)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        IntervalRecord best = series[lo];
        if (lo > 0 && Math.Abs((series[lo - 1].Date - t).TotalSeconds) < Math.Abs((best.Date - t).TotalSeconds))
        {
            best = series[lo - 1];
        }

        return Math.Abs((best.Date - t).TotalSeconds) <= withinSeconds ? best : null;
    }

    private static double Sq(double v) => v * v;

    public static double Median(IEnumerable<double> values) => Percentile(values, 0.5);

    public static DateTimeOffset Median(IEnumerable<DateTimeOffset> values)
    {
        DateTimeOffset[] sorted = [.. values.Order()];
        return sorted.Length == 0 ? default : sorted[sorted.Length / 2];
    }

    public static double Percentile(IEnumerable<double> values, double q)
    {
        double[] sorted = [.. values.Where(double.IsFinite).Order()];
        return sorted.Length == 0 ? double.NaN : sorted[Math.Min(sorted.Length - 1, (int)Math.Floor(q * sorted.Length))];
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Trimmed recorded fixtures (docs/06 §9): the raw records of two drivers over a short window, unmodified apart from the
// filtering, plus a README with the licence.

static class Fixture
{
    public static async Task CutAsync(OpenF1 api, int key, string name, DateTimeOffset from, DateTimeOffset to, int[] drivers)
    {
        string dir = Path.Combine("tests", "RaceReplay.TestKit", "Fixtures", "OpenF1", name);
        Directory.CreateDirectory(dir);
        JsonArray session = Parse(await api.GetAsync($"sessions?session_key={key}"));
        int meetingKey = session[0]!["meeting_key"]!.GetValue<int>();
        bool InWindow(JsonNode n, string field = "date") =>
            n[field] is JsonNode d && DateTimeOffset.Parse(d.GetValue<string>(), CultureInfo.InvariantCulture) is var t && t >= from && t <= to;
        bool Ours(JsonNode n) => n["driver_number"] is JsonNode d && drivers.Contains(d.GetValue<int>());

        Dictionary<string, JsonArray> files = new()
        {
            ["sessions"] = session,
            ["meetings"] = Parse(await api.GetAsync($"meetings?meeting_key={meetingKey}")),
            ["drivers"] = Filter(Parse(await api.GetAsync($"drivers?session_key={key}")), Ours),
            ["session_result"] = Filter(Parse(await api.GetAsync($"session_result?session_key={key}")), Ours),
            ["laps"] = Filter(Parse(await api.GetAsync($"laps?session_key={key}")), n => Ours(n) && n["date_start"] is JsonNode d
                && DateTimeOffset.Parse(d.GetValue<string>(), CultureInfo.InvariantCulture) is var t && t >= from.AddMinutes(-3) && t <= to),
            ["pit"] = Filter(Parse(await api.GetAsync($"pit?session_key={key}")), n => Ours(n) && InWindow(n)),
            ["race_control"] = Filter(Parse(await api.GetAsync($"race_control?session_key={key}")), n => InWindow(n)),
            ["intervals"] = Filter(Parse(await api.GetAsync($"intervals?session_key={key}")), n => Ours(n) && InWindow(n)),
            ["position"] = Filter(Parse(await api.GetAsync($"position?session_key={key}")), n => Ours(n) && InWindow(n)),
        };
        JsonArray location = [];
        foreach (int d in drivers)
        {
            foreach (JsonNode? n in Filter(Parse(await api.GetAsync($"location?session_key={key}&driver_number={d}")), n => InWindow(n)))
            {
                location.Add(n!.DeepClone());
            }
        }

        files["location"] = location;
        long total = 0;
        foreach ((string endpoint, JsonArray records) in files)
        {
            string path = Path.Combine(dir, $"{endpoint}.json");
            File.WriteAllText(path, records.ToJsonString());
            total += new FileInfo(path).Length;
            Console.WriteLine($"  {endpoint}.json: {records.Count} records, {new FileInfo(path).Length:N0} B");
        }

        string title = $"{session[0]!["year"]} {files["meetings"][0]!["meeting_name"]}, session {key}";
        File.WriteAllText(Path.Combine(dir, "README.md"), $"""
            # OpenF1 fixture: {title}

            Trimmed recorded responses from the [OpenF1](https://openf1.org) API, used by the adapter tests (docs/06 §9).

            - **Licence:** derived from OpenF1 data, which is licensed under
              [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/). So are these files.
              Attribution: **Data: OpenF1 (openf1.org)**. See [DATA-LICENSE](../../../../../DATA-LICENSE.md).
            - **Session:** {key} ({session[0]!["circuit_short_name"]}).
            - **Kept:** drivers {string.Join(" and ", drivers)}, from {from:yyyy-MM-ddTHH:mm:ssZ} to {to:yyyy-MM-ddTHH:mm:ssZ}
              (laps from 3 minutes earlier, so the window has a lap anchor). Session-wide records (`sessions`, `meetings`) are whole.
            - **Unmodified** apart from that filtering: one JSON array per endpoint, as the API returned it.
            - **Cut by** `dotnet run scripts/s1-explore.cs -- fixture {key} {name} {from:yyyy-MM-ddTHH:mm:ssZ} {to:yyyy-MM-ddTHH:mm:ssZ} {string.Join(' ', drivers)}`
              (spike S1, RR-020).
            """);
        Console.WriteLine($"Fixture {name}: {total:N0} B in {files.Count} files");
    }

    private static JsonArray Parse(byte[] json) => JsonNode.Parse(json)!.AsArray();

    private static JsonArray Filter(JsonArray source, Func<JsonNode, bool> keep) =>
        [.. source.Where(n => n is not null && keep(n)).Select(n => n!.DeepClone())];
}
