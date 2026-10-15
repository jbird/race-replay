namespace RaceReplay.Web.Loading;

/// <summary>Why a replay couldn't be loaded. Each kind has its own message and recovery (docs/05 §9.3).</summary>
public enum ReplayLoadFailure
{
    /// <summary>The network or the server failed. Retrying may help.</summary>
    Unavailable,

    /// <summary>The replay isn't in the catalogue.</summary>
    NotFound,

    /// <summary>The bundle is from a newer major format version than this app reads.</summary>
    NewerVersion,

    /// <summary>The catalogue or bundle is malformed, inconsistent or too large.</summary>
    Corrupt,
}

/// <summary>A replay or the catalogue couldn't be loaded from the site.</summary>
public sealed class ReplayLoadException : Exception
{
    public ReplayLoadException()
    {
    }

    public ReplayLoadException(string message)
        : base(message)
    {
    }

    public ReplayLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ReplayLoadFailure Failure { get; init; }

    /// <summary>The replay being loaded, or <see langword="null"/> when the catalogue itself failed.</summary>
    public string? ReplayId { get; init; }
}