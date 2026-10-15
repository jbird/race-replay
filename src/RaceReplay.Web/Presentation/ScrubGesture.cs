using RaceReplay.Playback;

namespace RaceReplay.Web.Presentation;

/// <summary>
/// The scrubber's drag (docs/05 §3.3): pointer down begins a scrub once, each input seeks, and the first of pointer
/// up, pointer cancel or change ends it. Keyboard input seeks without a scrub.
/// </summary>
public sealed class ScrubGesture
{
    /// <summary>True between the pointer going down and the drag ending.</summary>
    public bool IsActive { get; private set; }

    public void Begin(IPlaybackControl? control)
    {
        if (!IsActive)
        {
            IsActive = true;
            control?.BeginScrub();
        }
    }

    /// <param name="control">The open replay's clock, if any.</param>
    /// <param name="value">The range input's value; ignored unless it is a finite number.</param>
    public static void Seek(IPlaybackControl? control, object? value)
    {
        if (InvariantNumber.TryParse(value, out double position))
        {
            control?.Seek(position);
        }
    }

    public void End(IPlaybackControl? control)
    {
        if (IsActive)
        {
            IsActive = false;
            control?.EndScrub();
        }
    }
}