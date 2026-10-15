using System.Security.Cryptography;

using RaceReplay.Domain;

namespace RaceReplay.Bundle.Manifest;

/// <summary>Maps between the domain and the manifest contract. Reading assumes untrusted input.</summary>
internal static class ManifestMapper
{
    public const string Format = "race-replay-bundle";
    public const int SchemaMajor = 1;
    public const int SchemaMinor = 0;

    private static readonly (SegmentKind Kind, string Name)[] s_segmentKinds =
    [
        (SegmentKind.PreRace, "preRace"),
        (SegmentKind.Racing, "racing"),
        (SegmentKind.Suspended, "suspended"),
        (SegmentKind.PostRace, "postRace"),
    ];

    private static readonly (DataSource Source, string Name)[] s_sources =
    [
        (DataSource.Synthetic, "synthetic"),
        (DataSource.OpenF1, "openf1"),
    ];

    public static BundleManifestDto ToDto(ReplaySession replay, ReadOnlySpan<byte> positions)
    {
        TrackModel track = replay.Track;
        ReplayTimeline timeline = replay.Timeline;
        Provenance provenance = replay.Provenance;
        return new BundleManifestDto
        {
            Format = Format,
            SchemaVersion = $"{SchemaMajor}.{SchemaMinor}",
            Session = new ManifestSessionDto
            {
                Key = replay.Session.Key.Value,
                Year = replay.Session.Year,
                Type = replay.Session.Type,
                Name = replay.Session.Name,
                MeetingName = replay.Session.MeetingName,
                Circuit = replay.Session.Circuit,
                StartUtc = replay.Session.StartUtc.UtcDateTime,
            },
            Timeline = new ManifestTimelineDto
            {
                T0Utc = timeline.T0Utc.UtcDateTime,
                SampleRateHz = timeline.SampleRateHz,
                SampleCount = timeline.SampleCount,
                RaceStart = timeline.RaceStart,
                Segments = [.. timeline.Segments.Select(s => new ManifestSegmentDto { Start = s.Start, End = s.End, Kind = Name(s.Kind) })],
            },
            Track = new ManifestTrackDto
            {
                UnitsPerMetre = track.UnitsPerMetre,
                LapLength = track.LapLength,
                DisplayRotationDegrees = track.DisplayRotationDegrees,
                Bounds = new ManifestBoundsDto
                {
                    MinX = track.Bounds.MinX,
                    MinY = track.Bounds.MinY,
                    MaxX = track.Bounds.MaxX,
                    MaxY = track.Bounds.MaxY,
                },
                Centreline = [.. track.Centreline.ToArray().Select(p => new[] { p.X, p.Y })],
            },
            Drivers =
            [
                .. replay.Drivers.Select((d, i) => new ManifestDriverDto
                {
                    Index = i,
                    Number = d.Number.Value,
                    Acronym = d.Acronym,
                    FullName = d.FullName,
                    Team = d.TeamName,
                    Colour = d.TeamColour.ToHex(),
                }),
            ],
            Channels = new ManifestChannelsDto
            {
                Positions = new ManifestChannelFileDto
                {
                    File = BundleFiles.Positions,
                    Bytes = positions.Length,
                    Sha256 = Convert.ToHexStringLower(SHA256.HashData(positions)),
                },
            },
            Provenance = new ManifestProvenanceDto
            {
                Source = Name(provenance.Source),
                Attribution = provenance.Attribution,
                Licence = provenance.Licence,
                RetrievedUtc = provenance.RetrievedUtc.UtcDateTime,
                Builder = provenance.Builder,
                Seed = provenance.Seed,
            },
        };
    }

    public static void CheckVersion(BundleManifestDto manifest)
    {
        if (manifest.Format != Format)
        {
            throw new BundleFormatException($"Not a Race Replay manifest (format '{manifest.Format}').");
        }

        string[] parts = manifest.SchemaVersion.Split('.');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out _))
        {
            throw new BundleFormatException($"Unreadable schema version '{manifest.SchemaVersion}'.");
        }

        if (major != SchemaMajor)
        {
            throw new BundleFormatException($"Bundle schema {manifest.SchemaVersion} is not supported; this app reads {SchemaMajor}.x.")
            {
                IsNewerVersion = major > SchemaMajor,
            };
        }
    }

    public static SessionInfo Session(ManifestSessionDto s) =>
        new(new SessionKey(s.Key), s.Year, s.Type, s.Name, s.MeetingName, s.Circuit, Utc(s.StartUtc, "session start"));

    public static Driver[] Drivers(IReadOnlyList<ManifestDriverDto> drivers)
    {
        Driver[] result = new Driver[drivers.Count];
        for (int i = 0; i < drivers.Count; i++)
        {
            ManifestDriverDto d = drivers[i] ?? throw new BundleFormatException($"Driver entry {i} is null.");
            if (d.Index != i)
            {
                throw new BundleFormatException($"Driver {d.Number} is listed at position {i} but has index {d.Index}.");
            }

            if (!RgbColour.TryFromHex(d.Colour, out RgbColour colour))
            {
                throw new BundleFormatException($"Driver {d.Number} has an invalid colour '{d.Colour}'.");
            }

            result[i] = new Driver(new DriverNumber(d.Number), d.Acronym, d.FullName, d.Team, colour);
        }

        return result;
    }

    public static TrackModel Track(ManifestTrackDto t)
    {
        TrackPoint[] centreline = new TrackPoint[t.Centreline.Count];
        for (int i = 0; i < centreline.Length; i++)
        {
            if (t.Centreline[i] is not [float x, float y] || !float.IsFinite(x) || !float.IsFinite(y))
            {
                throw new BundleFormatException($"Centreline point {i} must be a finite [x, y] pair.");
            }

            centreline[i] = new TrackPoint(x, y);
        }

        return new TrackModel(centreline, t.UnitsPerMetre, t.DisplayRotationDegrees);
    }

    public static ReplayTimeline Timeline(ManifestTimelineDto t)
    {
        if (t.SampleCount > Rrb.RrbFormat.MaxSamples)
        {
            throw new BundleFormatException($"Implausible sample count {t.SampleCount}.");
        }

        return new ReplayTimeline(
            Utc(t.T0Utc, "timeline T0"),
            t.SampleRateHz,
            t.SampleCount,
            t.RaceStart,
            [
                .. t.Segments.Select((s, i) => s is null
                    ? throw new BundleFormatException($"Segment {i} is null.")
                    : new TimelineSegment(s.Start, s.End, Kind(s.Kind))),
            ]);
    }

    public static Provenance Provenance(ManifestProvenanceDto p) =>
        new(Source(p.Source), Utc(p.RetrievedUtc, "retrieval time"), p.Builder, p.Attribution, p.Licence, p.Seed);

    private static string Name(SegmentKind kind) => s_segmentKinds.First(k => k.Kind == kind).Name;

    private static SegmentKind Kind(string name) =>
        s_segmentKinds.FirstOrDefault(k => k.Name == name) is { Name: not null } match
            ? match.Kind
            : throw new BundleFormatException($"Unknown segment kind '{name}'.");

    private static string Name(DataSource source) => s_sources.First(s => s.Source == source).Name;

    private static DataSource Source(string name) =>
        s_sources.FirstOrDefault(s => s.Name == name) is { Name: not null } match
            ? match.Source
            : throw new BundleFormatException($"Unknown data source '{name}'.");

    private static DateTimeOffset Utc(DateTime value, string what) =>
        value.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(value)
            : throw new BundleFormatException($"The {what} must be a UTC timestamp ending in 'Z'.");
}