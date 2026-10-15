namespace RaceReplay.Domain;

/// <summary>Where a replay's data came from. <see cref="Unknown"/> is never valid, so a default can't pass as synthetic.</summary>
public enum DataSource
{
    Unknown,
    Synthetic,
    OpenF1,
}

/// <summary>
/// Where a replay came from and under what terms. Real (OpenF1) data must carry its attribution and licence
/// (DATA-LICENSE.md, NFR-12); synthetic data carries the seed that reproduces it.
/// </summary>
public sealed record Provenance
{
    public const string OpenF1Attribution = "Data: OpenF1 — https://openf1.org";
    public const string OpenF1Licence = "CC BY-NC-SA 4.0";

    public Provenance(
        DataSource source,
        DateTimeOffset retrievedUtc,
        string builder,
        string? attribution = null,
        string? licence = null,
        int? seed = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(builder);
        if (retrievedUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The retrieval time must be a UTC timestamp.", nameof(retrievedUtc));
        }

        switch (source)
        {
            case DataSource.Synthetic when seed is null:
                throw new ArgumentException("Synthetic data needs the seed that generated it.", nameof(seed));
            case DataSource.OpenF1 when seed is not null:
                throw new ArgumentException("Only synthetic data has a seed.", nameof(seed));
            case DataSource.OpenF1 when string.IsNullOrWhiteSpace(attribution) || string.IsNullOrWhiteSpace(licence):
                throw new ArgumentException("OpenF1 data must carry its attribution and licence.", nameof(attribution));
            case DataSource.Synthetic or DataSource.OpenF1:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, "The data source must be known.");
        }

        Source = source;
        RetrievedUtc = retrievedUtc;
        Builder = builder;
        Attribution = attribution;
        Licence = licence;
        Seed = seed;
    }

    public DataSource Source { get; }

    /// <summary>When the data was fetched or generated (UTC).</summary>
    public DateTimeOffset RetrievedUtc { get; }

    /// <summary>The tool and version that built the replay, e.g. <c>race-replay 0.1.0+3f2c1ab</c>.</summary>
    public string Builder { get; }

    public string? Attribution { get; }

    public string? Licence { get; }

    /// <summary>The generator seed of a synthetic race.</summary>
    public int? Seed { get; }

    public bool IsSynthetic => Source == DataSource.Synthetic;

    public static Provenance ForSynthetic(int seed, DateTimeOffset generatedUtc, string builder) =>
        new(DataSource.Synthetic, generatedUtc, builder, seed: seed);

    public static Provenance ForOpenF1(DateTimeOffset retrievedUtc, string builder) =>
        new(DataSource.OpenF1, retrievedUtc, builder, OpenF1Attribution, OpenF1Licence);
}