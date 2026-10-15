using System.Globalization;

using RaceReplay.Application.Ports;
using RaceReplay.Bundle;
using RaceReplay.Domain;

namespace RaceReplay.Cli;

/// <summary>
/// Writes replays to disk (docs/04 §7.1). The directory layout puts synthetic races in <c>{out}/{seed}/</c> and real
/// races in <c>{out}/{year}/{key}/</c>. Files are written to a staging folder first and then swapped in, so a reader
/// never sees a half-written bundle.
/// </summary>
public sealed class FileSystemReplayPackageStore : IReplayPackageStore
{
    public async Task<string> SaveAsync(
        ReplaySession replay, string outputDirectory, PackageLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (layout != PackageLayout.Directory)
        {
            throw new NotSupportedException($"The {layout} layout is not supported yet.");
        }

        SessionKey key = replay.Session.Key;
        string relative = key.IsSynthetic
            ? key.SyntheticSeed.ToString(CultureInfo.InvariantCulture)
            : Path.Combine(replay.Session.Year.ToString(CultureInfo.InvariantCulture), key.ToString());
        string target = Path.GetFullPath(Path.Combine(outputDirectory, relative));
        string staging = $"{target}.staging-{Path.GetRandomFileName()}";

        ReplayBundle bundle = ReplayBundleWriter.Write(replay);
        try
        {
            Directory.CreateDirectory(staging);
            await File.WriteAllBytesAsync(Path.Combine(staging, BundleFiles.Manifest), bundle.ManifestJson, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(staging, BundleFiles.Positions), bundle.Positions, cancellationToken);

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(staging, target);
            return target;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }
}