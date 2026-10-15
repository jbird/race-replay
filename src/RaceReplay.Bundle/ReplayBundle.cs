using System.Text.Json;

using RaceReplay.Bundle.Manifest;
using RaceReplay.Bundle.Rrb;
using RaceReplay.Domain;

namespace RaceReplay.Bundle;

/// <summary>The file names a bundle consists of, in either layout (docs/04 §7.1).</summary>
public static class BundleFiles
{
    public const string Manifest = "manifest.json";
    public const string Positions = "positions.rrb";
}

/// <summary>A written bundle: the manifest and the binary channel files, ready to store.</summary>
public sealed record ReplayBundle(BundleManifestDto Manifest, ReadOnlyMemory<byte> ManifestJson, ReadOnlyMemory<byte> Positions);

/// <summary>Writes a replay as a bundle. Output is a pure function of the replay, so rebuilds are byte-identical (NFR-11).</summary>
public static class ReplayBundleWriter
{
    public static ReplayBundle Write(ReplaySession replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        byte[] positions = PositionsFile.Write(replay);
        BundleManifestDto manifest = ManifestMapper.ToDto(replay, positions);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest, BundleJsonContext.Default.BundleManifestDto);
        return new ReplayBundle(manifest, json, positions);
    }
}

/// <summary>
/// Reads a bundle back into a replay. Bundle bytes are untrusted (users open files from anywhere), so every problem,
/// from malformed JSON to an out-of-range offset to a domain invariant, surfaces as <see cref="BundleFormatException"/>.
/// </summary>
public static class ReplayBundleReader
{
    /// <summary>Parses and version-checks a manifest.</summary>
    /// <exception cref="BundleFormatException">The manifest is malformed or from an unsupported major version.</exception>
    public static BundleManifestDto ReadManifest(ReadOnlySpan<byte> manifestJson)
    {
        BundleManifestDto? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(manifestJson, BundleJsonContext.Default.BundleManifestDto);
        }
        catch (JsonException e)
        {
            throw new BundleFormatException($"The manifest is not valid: {e.Message}", e);
        }

        if (manifest is null)
        {
            throw new BundleFormatException("The manifest is empty.");
        }

        ManifestMapper.CheckVersion(manifest);
        return manifest;
    }

    /// <exception cref="BundleFormatException">The bundle is malformed, inconsistent, or from an unsupported version.</exception>
    public static ReplaySession Read(ReadOnlySpan<byte> manifestJson, ReadOnlySpan<byte> positions)
    {
        BundleManifestDto manifest = ReadManifest(manifestJson);
        if (positions.Length != manifest.Channels.Positions.Bytes)
        {
            throw new BundleFormatException(
                $"{BundleFiles.Positions} is {positions.Length} bytes; the manifest says {manifest.Channels.Positions.Bytes}.");
        }

        try
        {
            Driver[] drivers = ManifestMapper.Drivers(manifest.Drivers);
            ReplayTimeline timeline = ManifestMapper.Timeline(manifest.Timeline);
            IReadOnlyDictionary<DriverNumber, PositionChannels> channels = PositionsFile.Read(
                positions, [.. drivers.Select(d => d.Number)], timeline.SampleCount, timeline.SampleRateHz);

            CarChannels[] cars =
            [
                .. drivers.Select(d =>
                {
                    PositionChannels c = channels[d.Number];
                    return new CarChannels(d.Number, c.X, c.Y, c.Valid, c.Progress);
                }),
            ];

            return new ReplaySession(
                ManifestMapper.Session(manifest.Session),
                drivers,
                ManifestMapper.Track(manifest.Track),
                timeline,
                cars,
                GapSeries.Empty,
                [],
                ManifestMapper.Provenance(manifest.Provenance));
        }
        catch (ArgumentException e)
        {
            // Domain constructors enforce the replay's invariants; a violation means the bundle is inconsistent.
            throw new BundleFormatException($"The bundle is inconsistent: {e.Message}", e);
        }
    }
}