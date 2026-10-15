using RaceReplay.Bundle;
using RaceReplay.Bundle.Catalog;
using RaceReplay.Bundle.Manifest;

namespace RaceReplay.Cli;

/// <summary>
/// <c>race-replay catalog</c>: regenerates <c>catalog.json</c> from the bundles under a folder (docs/04 §8). In demo
/// mode it refuses real-race bundles, a second line of defence behind the deploy's data-policy check (ADR-0008).
/// </summary>
public sealed class CatalogCommandHandler(TimeProvider time, CliConsole console)
{
    public async Task<int> RunAsync(string bundlesDirectory, string mode, string? outputFile, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(bundlesDirectory);
        if (!Directory.Exists(root))
        {
            await console.Error.WriteLineAsync($"No bundles folder at {root}.");
            return ExitCodes.Data;
        }

        List<CatalogSessionDto> sessions = [];
        foreach (string manifestPath in Directory.EnumerateFiles(root, BundleFiles.Manifest, SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string folder = Path.GetDirectoryName(manifestPath)!;
            string relative = Path.GetRelativePath(root, folder).Replace('\\', '/');
            BundleManifestDto manifest;
            try
            {
                manifest = ReplayBundleReader.ReadManifest(await File.ReadAllBytesAsync(manifestPath, cancellationToken));
            }
            catch (BundleFormatException e)
            {
                await console.Error.WriteLineAsync($"{manifestPath}: {e.Message}");
                return ExitCodes.Data;
            }

            // The manifest is untrusted: only the fixed file name is accepted, never a path it supplies.
            string positions = Path.Combine(folder, BundleFiles.Positions);
            if (relative == "." || manifest.Channels.Positions.File != BundleFiles.Positions
                || !File.Exists(positions) || new FileInfo(positions).Length != manifest.Channels.Positions.Bytes)
            {
                await console.Error.WriteLineAsync($"{folder}: not a complete bundle in its own folder.");
                return ExitCodes.Data;
            }

            if (mode == ReplayCatalog.DemoMode && manifest.Provenance.Source != "synthetic")
            {
                await console.Error.WriteLineAsync(
                    $"{manifestPath}: '{manifest.Provenance.Source}' bundle in a demo catalogue; real races need hosted mode (ADR-0008).");
                return ExitCodes.Data;
            }

            long bytes = new FileInfo(manifestPath).Length + manifest.Channels.Positions.Bytes;
            sessions.Add(ReplayCatalog.Describe(manifest, relative + "/", bytes));
        }

        BundleCatalogDto catalog = new()
        {
            SchemaVersion = ReplayCatalog.SchemaVersion,
            GeneratedUtc = time.GetUtcNow().UtcDateTime,
            Mode = mode,
            Sessions = sessions,
        };

        string output = Path.GetFullPath(outputFile ?? Path.Combine(root, ReplayCatalog.FileName));
        await File.WriteAllBytesAsync(output, ReplayCatalog.Serialize(catalog), cancellationToken);
        await console.Out.WriteLineAsync($"{output} ({sessions.Count} replays, {mode} mode)");
        return ExitCodes.Success;
    }
}