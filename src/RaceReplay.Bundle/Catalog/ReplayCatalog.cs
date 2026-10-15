using System.Globalization;
using System.Text.Json;

using RaceReplay.Bundle.Manifest;

namespace RaceReplay.Bundle.Catalog;

/// <summary>
/// <c>bundles/catalog.json</c>: the replays a site serves, and whether it serves demo races only or hosted real races too
/// (docs/04 §8). The mode lives here, not in app settings, so deploys never edit published app files.
/// </summary>
public sealed record BundleCatalogDto
{
    public required string SchemaVersion { get; init; }

    public required DateTime GeneratedUtc { get; init; }

    /// <summary><c>demo</c> or <c>hosted</c>.</summary>
    public required string Mode { get; init; }

    public required IReadOnlyList<CatalogSessionDto> Sessions { get; init; }
}

public sealed record CatalogSessionDto
{
    /// <summary><c>demo-{seed}</c> or <c>{year}-{key}</c>.</summary>
    public required string Id { get; init; }

    public required string Source { get; init; }

    public required string Title { get; init; }

    /// <summary>The bundle's folder relative to the catalogue, ending in <c>/</c>, e.g. <c>demo/1/</c>.</summary>
    public required string Path { get; init; }

    public required string BundleSchema { get; init; }

    public required long Bytes { get; init; }

    public int? Year { get; init; }

    public int? Key { get; init; }

    public int? Seed { get; init; }
}

public static class ReplayCatalog
{
    public const string FileName = "catalog.json";
    public const string DemoMode = "demo";
    public const string HostedMode = "hosted";
    public const string SchemaVersion = "1.0";

    /// <summary>Catalogue entry for a bundle whose manifest is <paramref name="manifest"/>.</summary>
    public static CatalogSessionDto Describe(BundleManifestDto manifest, string path, long bytes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ManifestSessionDto session = manifest.Session;
        bool synthetic = manifest.Provenance.Source == "synthetic";
        int? seed = synthetic ? manifest.Provenance.Seed : null;
        return new CatalogSessionDto
        {
            Id = synthetic
                ? string.Create(CultureInfo.InvariantCulture, $"demo-{seed}")
                : string.Create(CultureInfo.InvariantCulture, $"{session.Year}-{session.Key}"),
            Source = manifest.Provenance.Source,
            Title = synthetic
                ? string.Create(CultureInfo.InvariantCulture, $"Demo GP — {session.Circuit} (seed {seed})")
                : string.Create(CultureInfo.InvariantCulture, $"{session.Year} {session.MeetingName} — {session.Name}"),
            Path = path,
            BundleSchema = manifest.SchemaVersion,
            Bytes = bytes,
            Year = synthetic ? null : session.Year,
            Key = synthetic ? null : session.Key,
            Seed = seed,
        };
    }

    public static byte[] Serialize(BundleCatalogDto catalog) =>
        JsonSerializer.SerializeToUtf8Bytes(catalog, BundleJsonContext.Default.BundleCatalogDto);

    /// <exception cref="BundleFormatException">The catalogue is malformed, of an unsupported version, or points outside the site.</exception>
    public static BundleCatalogDto Deserialize(ReadOnlySpan<byte> json)
    {
        BundleCatalogDto? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize(json, BundleJsonContext.Default.BundleCatalogDto);
        }
        catch (JsonException e)
        {
            throw new BundleFormatException($"The catalogue is not valid: {e.Message}", e);
        }

        if (catalog is null || !catalog.SchemaVersion.StartsWith("1.", StringComparison.Ordinal))
        {
            throw new BundleFormatException($"Catalogue schema '{catalog?.SchemaVersion}' is not supported.");
        }

        if (catalog.Mode is not (DemoMode or HostedMode))
        {
            throw new BundleFormatException($"Unknown catalogue mode '{catalog.Mode}'.");
        }

        foreach (CatalogSessionDto? session in catalog.Sessions)
        {
            if (session is null || !IsSafeRelativeFolder(session.Path))
            {
                throw new BundleFormatException($"Catalogue entry '{session?.Id}' has an unsafe path '{session?.Path}'.");
            }
        }

        return catalog;
    }

    // The app follows these paths, so they must stay inside the bundles folder: relative, no '..', no scheme.
    internal static bool IsSafeRelativeFolder(string path) =>
        path.Length > 1
        && path.EndsWith('/')
        && !path.StartsWith('/')
        && !path.Contains('\\', StringComparison.Ordinal)
        && !path.Contains(':', StringComparison.Ordinal)
        && path.Split('/').All(part => part is not (".." or "."));
}