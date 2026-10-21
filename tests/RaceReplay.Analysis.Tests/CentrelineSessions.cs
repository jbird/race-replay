using RaceReplay.Domain;
using RaceReplay.Synthetic;

namespace RaceReplay.Analysis.Tests;

/// <summary>
/// Small analytic sessions for the centreline tests (docs/06 §5): <see cref="SyntheticCar"/> samples, their true lap
/// records and optional race control, cleaned by <see cref="LocationCleaner"/> as the build does.
/// </summary>
internal static class CentrelineSessions
{
    private static readonly LocationCleaner s_cleaner = new(new CleaningOptions());

    /// <summary>The cars numbered 1, 2, … in order.</summary>
    public static SyntheticCar[] Numbered(params SyntheticCar[] cars) =>
        [.. cars.Select((car, i) => car.WithDriver(new DriverNumber(i + 1)))];

    /// <summary>A session of the cars, with each car's true laps from lap 2 unless <paramref name="laps"/> are given.</summary>
    public static RawSessionData Race(
        IReadOnlyList<SyntheticCar> cars,
        IEnumerable<RawLap>? laps = null,
        IReadOnlyList<RaceControlMessage>? raceControl = null,
        IReadOnlyList<RawPitStop>? pitStops = null,
        IReadOnlyList<SessionResult>? results = null) =>
        new(
            new SessionInfo(SessionKey.Synthetic(1), 2026, "Race", "Race", "Test Grand Prix", "Test Park", SyntheticCar.DefaultStart),
            [.. cars.Select(c => new Driver(c.Driver, "D" + c.Driver.Value, "Test Driver", "Test", new RgbColour(1, 2, 3)))],
            cars.ToDictionary(c => c.Driver, c => c.Samples()),
            [.. laps ?? cars.SelectMany(c => c.Laps(firstLap: 2))],
            pitStops ?? [],
            raceControl ?? [],
            [],
            [],
            results ?? []);

    public static Dictionary<DriverNumber, CleanedTrace> Clean(RawSessionData raw) =>
        raw.Locations.ToDictionary(p => p.Key, p => s_cleaner.Clean(p.Key, p.Value));

    public static CentrelineResult Build(RawSessionData raw, CentrelineOptions? options = null) =>
        new CentrelineBuilder(options ?? new CentrelineOptions()).Build(raw, Clean(raw));

    /// <summary>A race control message at <paramref name="seconds"/> after the cars' first sample.</summary>
    public static RaceControlMessage Message(double seconds, string text) =>
        new(SyntheticCar.DefaultStart.AddSeconds(seconds), "Other", null, null, null, null, text);

    /// <summary>A centreline point in metres from the track's centre.</summary>
    public static (double X, double Y) Metres(TrackPoint p) =>
        ((p.X / SyntheticTrack.UnitsPerMetre) - SyntheticTrack.CentreXMetres, (p.Y / SyntheticTrack.UnitsPerMetre) - SyntheticTrack.CentreYMetres);

    /// <summary>A point of the track's path, in metres from its centre.</summary>
    public static (double X, double Y) Metres((double X, double Y) units) =>
        ((units.X / SyntheticTrack.UnitsPerMetre) - SyntheticTrack.CentreXMetres, (units.Y / SyntheticTrack.UnitsPerMetre) - SyntheticTrack.CentreYMetres);

    /// <summary>How far a centreline point lies from an unrotated <see cref="SyntheticTrack.Stadium"/>'s path, in metres.</summary>
    public static double DistanceToStadium(TrackPoint p, double straight, double radius)
    {
        (double x, double y) = Metres(p);
        double half = straight / 2;
        return Math.Abs(x) <= half
            ? Math.Abs(Math.Abs(y) - radius)
            : Math.Abs(Math.Sqrt(Square(Math.Abs(x) - half) + Square(y)) - radius);
    }

    public static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(Square(b.X - a.X) + Square(b.Y - a.Y));

    private static double Square(double v) => v * v;
}