#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
#:property TreatWarningsAsErrors=false
#:project ../src/RaceReplay.Analysis/RaceReplay.Analysis.csproj

// M2's analysis on full real races, before M3's OpenF1 provider exists (RR-031, docs/08 §4). It reads the raw
// responses spike S1 cached under .cache/openf1/s1/ (gitignored, never committed: ADR-0008) and never touches the
// network: run `dotnet run scripts/s1-explore.cs -- analyse <key>` first for a race that isn't cached.
//
// Each stage prints what it found per race. RR-031 adds cleaning and the centreline; RR-032 to RR-034 add projection,
// progress and gaps as further stages, until the OpenF1 provider (RR-042) replaces the loader.
//
// Usage (from the repository root):
//   dotnet run scripts/real-races.cs                     the three S1 races: 9662, 9165, 9636
//   dotnet run scripts/real-races.cs -- <key> [<key> …]

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using RaceReplay.Analysis;
using RaceReplay.Domain;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

int[] keys = args.Length > 0 ? [.. args.Select(a => int.Parse(a, CultureInfo.InvariantCulture))] : [9662, 9165, 9636];
Cache cache = new(Path.Combine(".cache", "openf1", "s1"));
LocationCleaner cleaner = new(new CleaningOptions());
CentrelineBuilder centreline = new(new CentrelineOptions());

foreach (int key in keys)
{
    (RawSessionData raw, string circuit) = Loader.Load(cache, key);
    Console.WriteLine($"== {raw.Session.Year} {raw.Session.MeetingName} ({circuit}, {key})");

    // Cleaning (03 §4)
    Dictionary<DriverNumber, CleanedTrace> traces = raw.Locations.ToDictionary(p => p.Key, p => cleaner.Clean(p.Key, p.Value));
    Console.WriteLine($"cleaning: {traces.Values.Sum(t => t.Counts.SamplesIn):N0} samples, " +
        $"{traces.Values.Sum(t => t.Counts.Placeholders):N0} placeholders, {traces.Values.Sum(t => t.Counts.Spikes)} spikes, " +
        $"{traces.Values.Sum(t => t.Counts.Duplicates)} duplicates, runs per car {string.Join(" ", traces.OrderBy(p => p.Key.Value).Select(p => p.Value.Counts.Runs))}");

    // Centreline (03 §5)
    CentrelineResult result = centreline.Build(raw, traces);
    CentrelineDiagnostics d = result.Diagnostics;
    string official = Loader.OfficialLengthMetres.TryGetValue(circuit, out double metres)
        ? $" against {metres:N0} m official ({(d.LapLengthMetres / metres) - 1:+0.0 %;-0.0 %})"
        : "";
    LapExclusions e = d.Excluded;
    Console.WriteLine($"centreline: {d.LapLengthMetres:N0} m{official}, {result.Track.Centreline.Length} points, display rotation {result.Track.DisplayRotationDegrees:F1}°");
    Console.WriteLine($"  laps: {d.LapsChecked} checked; excluded {e.LapOne} lap 1, {e.PitInOrOut} pit in/out, {e.NoTiming} no timing, " +
        $"{e.Neutralised} neutralised, {e.TooSlow} over 107 %, {e.NotCovered} not covered, {e.TooFewPositions} too few positions");
    Console.WriteLine($"  references: {string.Join(", ", d.ReferenceLaps.Select(Describe))}");
    Console.WriteLine($"  discarded by length: {(d.DiscardedByLength.Count == 0 ? "none" : string.Join(", ", d.DiscardedByLength.Select(Describe)))}");
    Console.WriteLine($"  start/finish: {d.Boundaries} lap starts, spread {d.BoundarySpreadMetres:F1} m");
    Console.WriteLine();
}

static string Describe(ReferenceLap l) => $"#{l.Driver.Value} lap {l.LapNumber} ({l.DurationSeconds:F3} s, {l.LengthMetres:N0} m)";

// ---------------------------------------------------------------------------------------------------------------------
// The S1 cache, read-only: the same file names s1-explore.cs writes.

sealed class Cache(string root)
{
    public T[] Get<T>(string pathAndQuery)
    {
        string endpoint = pathAndQuery.Split('?')[0];
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pathAndQuery)))[..16].ToLowerInvariant();
        string body = Path.Combine(root, $"{endpoint}_{hash}.json");
        if (!File.Exists(body))
        {
            throw new FileNotFoundException($"{pathAndQuery} isn't cached in {root}. Run: dotnet run scripts/s1-explore.cs -- analyse <key>", body);
        }

        return JsonSerializer.Deserialize<T[]>(File.ReadAllBytes(body), Json.Options) ?? [];
    }
}

static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };
}

// ---------------------------------------------------------------------------------------------------------------------
// OpenF1 records (only the fields read here) and their mapping to the domain's raw records (docs/04 §2).

sealed record Session(int SessionKey, string SessionType, string SessionName, DateTimeOffset DateStart, int MeetingKey, string CircuitShortName, int Year);

sealed record Meeting(int MeetingKey, string MeetingName);

sealed record Driver(int DriverNumber, string NameAcronym, string? FullName, string? TeamName, string? TeamColour);

sealed record Location(DateTimeOffset Date, int DriverNumber, int X, int Y, int Z);

sealed record Lap(int DriverNumber, int LapNumber, DateTimeOffset? DateStart, double? LapDuration, bool? IsPitOutLap);

sealed record RaceControl(DateTimeOffset Date, string? Category, string? Flag, string? Scope, int? LapNumber, int? DriverNumber, string? Message);

static class Loader
{
    // Official lap lengths in metres (FIA/F1 published figures, current layouts), as in s1-explore.cs.
    public static readonly Dictionary<string, double> OfficialLengthMetres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sakhir"] = 5412, ["Jeddah"] = 6174, ["Melbourne"] = 5278, ["Suzuka"] = 5807, ["Shanghai"] = 5451,
        ["Miami"] = 5412, ["Imola"] = 4909, ["Monte Carlo"] = 3337, ["Montreal"] = 4361, ["Catalunya"] = 4657,
        ["Spielberg"] = 4318, ["Silverstone"] = 5891, ["Hungaroring"] = 4381, ["Spa-Francorchamps"] = 7004,
        ["Zandvoort"] = 4259, ["Monza"] = 5793, ["Baku"] = 6003, ["Singapore"] = 4940, ["Austin"] = 5513,
        ["Mexico City"] = 4304, ["Interlagos"] = 4309, ["Las Vegas"] = 6201, ["Lusail"] = 5419, ["Yas Marina Circuit"] = 5281,
    };

    public static (RawSessionData Raw, string Circuit) Load(Cache cache, int key)
    {
        Session session = cache.Get<Session>($"sessions?session_key={key}").Single();
        Meeting meeting = cache.Get<Meeting>($"meetings?meeting_key={session.MeetingKey}").Single();
        Driver[] drivers = [.. cache.Get<Driver>($"drivers?session_key={key}").OrderBy(d => d.DriverNumber)];

        Dictionary<DriverNumber, IReadOnlyList<RawLocationSample>> locations = drivers.ToDictionary(
            d => new DriverNumber(d.DriverNumber),
            d => (IReadOnlyList<RawLocationSample>)[.. cache.Get<Location>($"location?session_key={key}&driver_number={d.DriverNumber}")
                .Select(l => new RawLocationSample(l.Date.ToUniversalTime(), new DriverNumber(l.DriverNumber), l.X, l.Y, l.Z))]);
        RawLap[] laps = [.. cache.Get<Lap>($"laps?session_key={key}")
            .Select(l => new RawLap(new DriverNumber(l.DriverNumber), l.LapNumber, l.DateStart?.ToUniversalTime(), l.LapDuration, l.IsPitOutLap == true))
            .OrderBy(l => l.Driver.Value).ThenBy(l => l.LapNumber)];
        RaceControlMessage[] raceControl = [.. cache.Get<RaceControl>($"race_control?session_key={key}")
            .Select(m => new RaceControlMessage(m.Date.ToUniversalTime(), m.Category ?? "", m.Flag, m.Scope, m.LapNumber,
                m.DriverNumber is int n ? new DriverNumber(n) : null, m.Message ?? ""))];

        SessionInfo info = new(new SessionKey(key), session.Year, session.SessionType, session.SessionName, meeting.MeetingName,
            session.CircuitShortName, session.DateStart.ToUniversalTime());
        RaceReplay.Domain.Driver[] field = [.. drivers.Select(d => new RaceReplay.Domain.Driver(
            new DriverNumber(d.DriverNumber), d.NameAcronym, d.FullName ?? d.NameAcronym, d.TeamName ?? "",
            RgbColour.TryFromHex(d.TeamColour ?? "", out RgbColour colour) ? colour : new RgbColour(128, 128, 128)))];

        return (new RawSessionData(info, field, locations, laps, [], raceControl, [], [], []), session.CircuitShortName);
    }
}