namespace RaceReplay.Domain;

/// <summary>What a session is: its key, when and where it ran.</summary>
public sealed record SessionInfo
{
    /// <param name="key">OpenF1 session key, or a synthetic key.</param>
    /// <param name="year">Season.</param>
    /// <param name="type">Session type, e.g. <c>Race</c>.</param>
    /// <param name="name">Session name, e.g. <c>Race</c> or <c>Sprint</c>.</param>
    /// <param name="meetingName">Event name, e.g. <c>Abu Dhabi Grand Prix</c>.</param>
    /// <param name="circuit">Circuit name.</param>
    /// <param name="startUtc">Scheduled start; must be UTC, so manifests don't depend on the build machine.</param>
    public SessionInfo(
        SessionKey key,
        int year,
        string type,
        string name,
        string meetingName,
        string circuit,
        DateTimeOffset startUtc)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(meetingName);
        ArgumentNullException.ThrowIfNull(circuit);
        if (startUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The start must be a UTC timestamp.", nameof(startUtc));
        }

        Key = key;
        Year = year;
        Type = type;
        Name = name;
        MeetingName = meetingName;
        Circuit = circuit;
        StartUtc = startUtc;
    }

    public SessionKey Key { get; }

    public int Year { get; }

    public string Type { get; }

    public string Name { get; }

    public string MeetingName { get; }

    public string Circuit { get; }

    public DateTimeOffset StartUtc { get; }
}

/// <summary>A driver as entered in one session (names and colours can change between sessions).</summary>
public sealed record Driver(
    DriverNumber Number,
    string Acronym,
    string FullName,
    string TeamName,
    RgbColour TeamColour);