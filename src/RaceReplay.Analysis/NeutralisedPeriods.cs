using RaceReplay.Domain;

namespace RaceReplay.Analysis;

/// <summary>A span of session time, both ends included.</summary>
internal readonly record struct TimeWindow(DateTimeOffset From, DateTimeOffset To)
{
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => start <= To && end >= From;
}

/// <summary>
/// Safety car, virtual safety car and red-flag periods, read from race control in the wording spike S1 found
/// (docs/spikes/S1.md, <c>race_control</c> wording). Messages match whole, ignoring case, so a VSC is never read as an SC.
/// A period that never closes runs to the end of the session.
/// </summary>
internal static class NeutralisedPeriods
{
    private static readonly (string Opens, string Closes)[] s_kinds =
    [
        ("SAFETY CAR DEPLOYED", "SAFETY CAR IN THIS LAP"), // the SC has no "ending" message (S1)
        ("VIRTUAL SAFETY CAR DEPLOYED", "VIRTUAL SAFETY CAR ENDING"),
        ("SESSION ABORTED", "SESSION STARTED"),            // a red flag, until the restart
    ];

    public static IReadOnlyList<TimeWindow> From(IReadOnlyList<RaceControlMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // Time order, then text, so the periods don't depend on the order the messages arrived in.
        RaceControlMessage[] ordered = [.. messages.OrderBy(m => m.Timestamp).ThenBy(m => m.Message, StringComparer.Ordinal)];
        List<TimeWindow> periods = [];
        foreach ((string opens, string closes) in s_kinds)
        {
            DateTimeOffset? open = null;
            foreach (RaceControlMessage message in ordered)
            {
                if (open is null && Is(message, opens))
                {
                    open = message.Timestamp;
                }
                else if (open is not null && Is(message, closes))
                {
                    periods.Add(new TimeWindow(open.Value, message.Timestamp));
                    open = null;
                }
            }

            if (open is not null)
            {
                periods.Add(new TimeWindow(open.Value, DateTimeOffset.MaxValue));
            }
        }

        return periods;
    }

    private static bool Is(RaceControlMessage message, string text) =>
        string.Equals(message.Message.Trim(), text, StringComparison.OrdinalIgnoreCase);
}