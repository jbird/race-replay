namespace RaceReplay.Domain;

/// <summary>What a session is: its key, when and where it ran.</summary>
/// <param name="Key">OpenF1 session key, or a synthetic key.</param>
/// <param name="Year">Season.</param>
/// <param name="Type">Session type, e.g. <c>Race</c>.</param>
/// <param name="Name">Session name, e.g. <c>Race</c> or <c>Sprint</c>.</param>
/// <param name="MeetingName">Event name, e.g. <c>Abu Dhabi Grand Prix</c>.</param>
/// <param name="Circuit">Circuit name.</param>
/// <param name="StartUtc">Scheduled start.</param>
public sealed record SessionInfo(
    SessionKey Key,
    int Year,
    string Type,
    string Name,
    string MeetingName,
    string Circuit,
    DateTimeOffset StartUtc);

/// <summary>A driver as entered in one session (names and colours can change between sessions).</summary>
public sealed record Driver(
    DriverNumber Number,
    string Acronym,
    string FullName,
    string TeamName,
    RgbColour TeamColour);