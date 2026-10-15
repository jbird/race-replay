using RaceReplay.Domain;

namespace RaceReplay.Synthetic;

/// <summary>A generated race: the raw data a provider returns, plus the exact circuit it was generated on.</summary>
public sealed record SyntheticRace(RawSessionData Raw, SyntheticCircuit Circuit, int RaceLaps);

/// <summary>
/// Generates a fictional race from a seed (docs/04 §11): circuit, field, race and observations, each from its own
/// random stream. The same seed, options and <see cref="Version"/> always produce the same race.
/// </summary>
public sealed class SyntheticRaceGenerator
{
    /// <summary>Bumped whenever a change alters the output for an existing seed.</summary>
    public const int Version = 1;

    /// <summary>A Grand Prix is the fewest laps exceeding 305 km.</summary>
    public const double RaceDistanceMetres = 305_000;

    private static readonly DateTimeOffset s_firstRaceDay = new(2026, 3, 1, 13, 0, 0, TimeSpan.Zero);

    private readonly SyntheticRaceOptions _options;

    public SyntheticRaceGenerator()
        : this(SyntheticRaceOptions.Default)
    {
    }

    public SyntheticRaceGenerator(SyntheticRaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public SyntheticRace Generate(int seed)
    {
        SessionKey key = SessionKey.Synthetic(seed);
        SyntheticCircuit circuit = CircuitGenerator.Generate(SyntheticRandom.For(seed, RandomChannel.Track));
        Driver[] drivers = FieldGenerator.Generate(SyntheticRandom.For(seed, RandomChannel.Field), _options.DriverCount);
        int raceLaps = _options.Laps ?? (int)Math.Ceiling(RaceDistanceMetres / circuit.LapLengthMetres);

        int[] teamOfCar = [.. Enumerable.Range(0, drivers.Length).Select(c => c / 2)];
        RaceSimulation race = RaceSimulator.Run(
            new SpeedProfile(circuit.Track), teamOfCar, raceLaps, _options, SyntheticRandom.For(seed, RandomChannel.Race));

        // Spread demo races over a season of Sundays-ish dates, without reading the clock.
        DateTimeOffset startUtc = s_firstRaceDay.AddDays(seed % 245);
        Xoshiro256StarStar observation = SyntheticRandom.For(seed, RandomChannel.Observation);
        Dictionary<DriverNumber, IReadOnlyList<RawLocationSample>> locations = new(drivers.Length);
        for (int c = 0; c < drivers.Length; c++)
        {
            double phase = observation.NextDouble();
            locations[drivers[c].Number] = LocationSampler.Sample(
                race, c, drivers[c].Number, circuit.Track, startUtc, _options.SampleRateHz, phase);
        }

        SessionInfo session = new(key, startUtc.Year, "Race", "Race", circuit.MeetingName, circuit.Name, startUtc);
        RawSessionData raw = new(
            session,
            drivers,
            locations,
            Laps(race, drivers, startUtc),
            PitStops: [],
            RaceControl: [],
            Intervals: [],
            PositionChanges: [],
            Results(race, drivers));
        return new SyntheticRace(raw, circuit, raceLaps);
    }

    // Lap 1 starts with the race; each later lap starts when the previous one is completed at the line.
    private static RawLap[] Laps(RaceSimulation race, Driver[] drivers, DateTimeOffset startUtc)
    {
        List<RawLap> laps = [];
        for (int c = 0; c < drivers.Length; c++)
        {
            double previous = 0;
            double[] completions = race.LapCompletions[c];
            for (int lap = 0; lap < completions.Length; lap++)
            {
                laps.Add(new RawLap(
                    drivers[c].Number,
                    lap + 1,
                    startUtc.AddTicks((long)Math.Round(previous * TimeSpan.TicksPerSecond, MidpointRounding.ToEven)),
                    completions[lap] - previous,
                    IsPitOutLap: false));
                previous = completions[lap];
            }
        }

        return [.. laps];
    }

    // Classified by laps completed, then by who took the flag first.
    private static SessionResult[] Results(RaceSimulation race, Driver[] drivers)
    {
        int[] order =
        [
            .. Enumerable.Range(0, drivers.Length)
                .OrderByDescending(c => race.LapCompletions[c].Length)
                .ThenBy(c => race.FinishTime[c])
                .ThenBy(c => c),
        ];

        SessionResult[] results = new SessionResult[order.Length];
        for (int position = 0; position < order.Length; position++)
        {
            int c = order[position];
            results[position] = new SessionResult(
                drivers[c].Number, position + 1, Dnf: false, Dns: false, Dsq: false, race.LapCompletions[c].Length);
        }

        return results;
    }
}