namespace RaceReplay.Domain;

/// <summary>How a timing gap is expressed.</summary>
public enum GapKind
{
    /// <summary>Not applicable, e.g. for the leader, or an unparseable value.</summary>
    None,

    /// <summary>A time gap in seconds.</summary>
    Seconds,

    /// <summary>One or more laps behind.</summary>
    Laps,
}

/// <summary>A gap as reported by timing: seconds, laps behind, or not applicable (leader).</summary>
public readonly record struct GapValue(GapKind Kind, double Seconds, int Laps)
{
    public static readonly GapValue None = new(GapKind.None, 0, 0);

    public static GapValue FromSeconds(double seconds) => new(GapKind.Seconds, seconds, 0);

    public static GapValue FromLaps(int laps) => new(GapKind.Laps, 0, laps);
}