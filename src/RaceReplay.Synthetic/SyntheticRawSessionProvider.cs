using RaceReplay.Application.Ports;
using RaceReplay.Domain;

namespace RaceReplay.Synthetic;

/// <summary>
/// The synthetic data source (key <c>"synthetic"</c>): generates the race for a key in the reserved synthetic range.
/// It's a production adapter, so the same pipeline runs on it as on OpenF1 (docs/04 §11, FR-17).
/// </summary>
public sealed class SyntheticRawSessionProvider(SyntheticRaceGenerator generator) : IRawSessionProvider
{
    public const string Key = "synthetic";

    public Task<RawSessionData> LoadAsync(SessionKey session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!session.IsSynthetic)
        {
            throw new SessionNotFoundException(session, Key);
        }

        return Task.FromResult(generator.Generate(session.SyntheticSeed).Raw);
    }
}