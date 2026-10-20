using RaceReplay.Domain;

namespace RaceReplay.Analysis;

/// <summary>A span of session time.</summary>
internal readonly record struct TimeWindow(DateTimeOffset From, DateTimeOffset To)
{
    /// <summary>Whether a lap from <paramref name="start"/> to <paramref name="end"/> shares time with the window.
    /// Touching it at one instant isn't enough, so a lap that starts as the window ends is clear.</summary>
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => start < To && end > From;
}

/// <summary>
/// Safety car, virtual safety car and red-flag periods, read from race control in the wording spike S1 found
/// (docs/spikes/S1.md, <c>race_control</c> wording; docs/03 §5.1). Messages match whole, ignoring case, so a VSC is
/// never read as an SC.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A safety car runs from <c>SAFETY CAR DEPLOYED</c> to the end of the lap it comes in on: the latest end of
/// any lap in progress when <c>SAFETY CAR IN THIS LAP</c> is shown. Cars further back start that lap after the
/// message, so it's their lap that has to be covered.</item>
/// <item>A VSC runs from <c>VIRTUAL SAFETY CAR DEPLOYED</c> to <c>VIRTUAL SAFETY CAR ENDING</c>, or until a safety
/// car replaces it.</item>
/// <item>A red flag runs from <c>SESSION ABORTED</c> to the next <c>SESSION STARTED</c>, and ends any safety car or
/// VSC: the race restarts under its own procedure (S1: São Paulo 2024).</item>
/// <item>A period that never closes runs to the end of the session.</item>
/// </list>
/// </remarks>
internal static class NeutralisedPeriods
{
    public static IReadOnlyList<TimeWindow> From(IReadOnlyList<RaceControlMessage> messages, IReadOnlyList<RawLap> laps)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(laps);

        // Time order, then text, so the periods don't depend on the order the messages arrived in.
        RaceControlMessage[] ordered = [.. messages.OrderBy(m => m.Timestamp).ThenBy(m => m.Message, StringComparer.Ordinal)];
        List<TimeWindow> periods = [];
        DateTimeOffset? safetyCar = null, virtualSafetyCar = null, redFlag = null;
        foreach (RaceControlMessage message in ordered)
        {
            DateTimeOffset at = message.Timestamp;
            if (Is(message, "SAFETY CAR DEPLOYED"))
            {
                Close(ref virtualSafetyCar, at); // a VSC upgraded to a safety car
                safetyCar ??= at;
            }
            else if (Is(message, "SAFETY CAR IN THIS LAP"))
            {
                Close(ref safetyCar, EndOfLapsInProgress(laps, at));
            }
            else if (Is(message, "VIRTUAL SAFETY CAR DEPLOYED"))
            {
                virtualSafetyCar ??= at;
            }
            else if (Is(message, "VIRTUAL SAFETY CAR ENDING"))
            {
                Close(ref virtualSafetyCar, at);
            }
            else if (Is(message, "SESSION ABORTED"))
            {
                Close(ref safetyCar, at);
                Close(ref virtualSafetyCar, at);
                redFlag ??= at;
            }
            else if (Is(message, "SESSION STARTED"))
            {
                Close(ref redFlag, at);
            }
        }

        Close(ref safetyCar, DateTimeOffset.MaxValue);
        Close(ref virtualSafetyCar, DateTimeOffset.MaxValue);
        Close(ref redFlag, DateTimeOffset.MaxValue);
        return [.. periods.OrderBy(p => p.From).ThenBy(p => p.To)];

        void Close(ref DateTimeOffset? open, DateTimeOffset at)
        {
            if (open is { } from)
            {
                periods.Add(new TimeWindow(from, at < from ? from : at));
                open = null;
            }
        }
    }

    // The latest end of any lap under way at the instant (started at or before it, ending after it), or the instant.
    private static DateTimeOffset EndOfLapsInProgress(IReadOnlyList<RawLap> laps, DateTimeOffset at)
    {
        DateTimeOffset latest = at;
        foreach (RawLap lap in laps)
        {
            if (lap.Start is { } start && start <= at && lap.Duration is { } duration && duration > 0 && double.IsFinite(duration))
            {
                DateTimeOffset end = start.AddTicks((long)Math.Round(duration * TimeSpan.TicksPerSecond));
                if (end > latest)
                {
                    latest = end;
                }
            }
        }

        return latest;
    }

    private static bool Is(RaceControlMessage message, string text) =>
        string.Equals(message.Message.Trim(), text, StringComparison.OrdinalIgnoreCase);
}