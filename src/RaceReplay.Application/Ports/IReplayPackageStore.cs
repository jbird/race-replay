using RaceReplay.Domain;

namespace RaceReplay.Application.Ports;

/// <summary>How a replay is stored (docs/04 §7.1).</summary>
public enum PackageLayout
{
    /// <summary>A folder of manifest and channel files, as the site serves demo races. The <c>.rrz</c> package arrives in M4.</summary>
    Directory,
}

/// <summary>Persists a built replay. The CLI's file-system store implements it; tests substitute it.</summary>
public interface IReplayPackageStore
{
    /// <summary>Writes the replay under <paramref name="outputDirectory"/> and returns where it went.</summary>
    Task<string> SaveAsync(ReplaySession replay, string outputDirectory, PackageLayout layout, CancellationToken cancellationToken);
}