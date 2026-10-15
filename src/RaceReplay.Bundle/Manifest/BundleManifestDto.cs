using System.Text.Json.Serialization;

namespace RaceReplay.Bundle.Manifest;

// manifest.json, schema 1.0 (docs/04 §7.2). These DTOs are the contract with the browser; the domain carries no
// serialisation attributes. Enumerations travel as explicit strings, mapped in ManifestMapper. Events, gaps and
// the "$schema" link are added in later minor versions (M2, M4).

public sealed record BundleManifestDto
{
    public required string Format { get; init; }

    public required string SchemaVersion { get; init; }

    public required ManifestSessionDto Session { get; init; }

    public required ManifestTimelineDto Timeline { get; init; }

    public required ManifestTrackDto Track { get; init; }

    public required IReadOnlyList<ManifestDriverDto> Drivers { get; init; }

    public required ManifestChannelsDto Channels { get; init; }

    public required ManifestProvenanceDto Provenance { get; init; }
}

public sealed record ManifestSessionDto
{
    public required int Key { get; init; }

    public required int Year { get; init; }

    public required string Type { get; init; }

    public required string Name { get; init; }

    public required string MeetingName { get; init; }

    public required string Circuit { get; init; }

    public required DateTime StartUtc { get; init; }
}

public sealed record ManifestTimelineDto
{
    public required DateTime T0Utc { get; init; }

    public required double SampleRateHz { get; init; }

    public required int SampleCount { get; init; }

    public required double RaceStart { get; init; }

    public required IReadOnlyList<ManifestSegmentDto> Segments { get; init; }
}

public sealed record ManifestSegmentDto
{
    public required double Start { get; init; }

    public required double End { get; init; }

    /// <summary><c>preRace</c>, <c>racing</c>, <c>suspended</c> or <c>postRace</c>.</summary>
    public required string Kind { get; init; }
}

public sealed record ManifestTrackDto
{
    public required double UnitsPerMetre { get; init; }

    /// <summary>Informational; readers derive the lap length from the centreline.</summary>
    public required double LapLength { get; init; }

    public required float DisplayRotationDegrees { get; init; }

    public required ManifestBoundsDto Bounds { get; init; }

    /// <summary>Closed centreline as <c>[x, y]</c> pairs in track units, starting at the start/finish line.</summary>
    public required IReadOnlyList<float[]> Centreline { get; init; }
}

public sealed record ManifestBoundsDto
{
    public required float MinX { get; init; }

    public required float MinY { get; init; }

    public required float MaxX { get; init; }

    public required float MaxY { get; init; }
}

public sealed record ManifestDriverDto
{
    /// <summary>Position in the channel order.</summary>
    public required int Index { get; init; }

    public required int Number { get; init; }

    public required string Acronym { get; init; }

    public required string FullName { get; init; }

    public required string Team { get; init; }

    /// <summary>Six hex digits, e.g. <c>3671C6</c>.</summary>
    public required string Colour { get; init; }
}

public sealed record ManifestChannelsDto
{
    public required ManifestChannelFileDto Positions { get; init; }
}

public sealed record ManifestChannelFileDto
{
    public required string File { get; init; }

    public required long Bytes { get; init; }

    /// <summary>Lower-case hex SHA-256 of the file; the CLI verifies it, the browser checks the length.</summary>
    public required string Sha256 { get; init; }
}

public sealed record ManifestProvenanceDto
{
    /// <summary><c>synthetic</c> or <c>openf1</c>; the deploy's data-policy check reads this (ADR-0008).</summary>
    public required string Source { get; init; }

    public string? Attribution { get; init; }

    public string? Licence { get; init; }

    public required DateTime RetrievedUtc { get; init; }

    public required string Builder { get; init; }

    public int? Seed { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BundleManifestDto))]
internal sealed partial class BundleJsonContext : JsonSerializerContext;