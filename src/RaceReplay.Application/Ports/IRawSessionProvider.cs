using RaceReplay.Domain;

namespace RaceReplay.Application.Ports;

/// <summary>
/// Loads one session's raw data. One implementation per data source, registered as a keyed service
/// (<c>"openf1"</c>, <c>"synthetic"</c>); the contract tests in RaceReplay.TestKit run against every one (docs/02 §6).
/// </summary>
public interface IRawSessionProvider
{
    /// <summary>
    /// Samples are UTC and strictly ascending per driver; each driver's laps are contiguous and in lap order.
    /// </summary>
    /// <exception cref="SessionNotFoundException">The key is outside this provider's range or doesn't exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<RawSessionData> LoadAsync(SessionKey session, CancellationToken cancellationToken);
}

/// <summary>A data source has no session with this key.</summary>
public sealed class SessionNotFoundException : Exception
{
    public SessionNotFoundException()
    {
    }

    public SessionNotFoundException(string message)
        : base(message)
    {
    }

    public SessionNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SessionNotFoundException(SessionKey session, string source)
        : base($"The {source} source has no session {session}.")
    {
        Session = session;
    }

    public SessionKey? Session { get; }
}