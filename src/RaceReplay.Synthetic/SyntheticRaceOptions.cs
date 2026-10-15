namespace RaceReplay.Synthetic;

/// <summary>Shape of a generated race. The defaults give a full-length race like a real Grand Prix.</summary>
public sealed record SyntheticRaceOptions
{
    public const int MaxDrivers = 20;

    public static SyntheticRaceOptions Default { get; } = new();

    /// <summary>Race distance in laps; by default about 305 km, as in Formula 1.</summary>
    public int? Laps { get; init; }

    /// <summary>Drivers, in teams of two.</summary>
    public int DriverCount { get; init; } = MaxDrivers;

    /// <summary>Location samples per second per car; OpenF1 delivers about 3.7.</summary>
    public double SampleRateHz { get; init; } = 3.7;

    /// <summary>Seconds of observation on the grid before the start.</summary>
    public double PreRaceSeconds { get; init; } = 60;

    /// <summary>Seconds each car keeps circulating after its chequered flag.</summary>
    public double CoolDownSeconds { get; init; } = 60;

    internal void Validate()
    {
        if (Laps is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(Laps), Laps, "A race runs 1–200 laps.");
        }

        if (DriverCount is < 2 or > MaxDrivers || DriverCount % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DriverCount), DriverCount, $"Drivers come in teams of two, 2–{MaxDrivers} in all.");
        }

        if (!(SampleRateHz is > 0 and <= 50))
        {
            throw new ArgumentOutOfRangeException(nameof(SampleRateHz), SampleRateHz, "The sample rate must be in (0, 50] Hz.");
        }

        if (!(PreRaceSeconds is >= 0 and <= 3_600) || !(CoolDownSeconds is >= 0 and <= 3_600))
        {
            throw new ArgumentOutOfRangeException(nameof(PreRaceSeconds), "Pre-race and cool-down times must be 0–3,600 s.");
        }
    }
}