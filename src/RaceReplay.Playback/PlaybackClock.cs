namespace RaceReplay.Playback;

/// <summary>What components that display time need (docs/02 §6, Interface Segregation).</summary>
public interface IPlaybackClockReader
{
    /// <summary>Seconds since the replay's T0.</summary>
    double Position { get; }

    double Duration { get; }

    double Rate { get; }

    bool IsPlaying { get; }
}

/// <summary>What playback controls need.</summary>
public interface IPlaybackControl
{
    void Play();

    void Pause();

    void Seek(double position);

    void SetRate(double rate);

    /// <summary>Starts a scrub: playback pauses until <see cref="EndScrub"/>.</summary>
    void BeginScrub();

    /// <summary>Ends a scrub, resuming playback if it was playing when the scrub began.</summary>
    void EndScrub();
}

/// <summary>The full clock, which only the render loop holds. Decorators (e.g. red-flag skipping, FR-14) implement it too.</summary>
public interface IPlaybackClock : IPlaybackClockReader, IPlaybackControl
{
    /// <summary>Advances by the time since the previous frame, using the <c>requestAnimationFrame</c> timestamp (ms).</summary>
    /// <returns>The new position.</returns>
    double Advance(double frameTimestampMs);
}

/// <summary>Playback settings (docs/05 §4.1).</summary>
public sealed record PlaybackOptions
{
    /// <summary>Rates the UI offers, slowest first.</summary>
    public IReadOnlyList<double> Rates { get; init; } = [0.25, 0.5, 1, 2, 4, 8, 16, 32, 64];

    /// <summary>Longest frame step applied, so returning to a background tab doesn't jump minutes ahead.</summary>
    public double MaxFrameDeltaMs { get; init; } = 250;

    public double MinRate => Rates.Min();

    public double MaxRate => Rates.Max();
}

/// <summary>
/// The replay's single source of time (docs/05 §4.1). It takes explicit frame timestamps rather than reading a clock,
/// so tests drive it with plain numbers. Allocation-free on the per-frame path.
/// </summary>
public sealed class PlaybackClock : IPlaybackClock
{
    private readonly double _minRate;
    private readonly double _maxRate;
    private readonly double _maxFrameDeltaMs;
    private double? _lastFrameMs;
    private bool _resumeAfterScrub;

    public PlaybackClock(double duration, PlaybackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!double.IsFinite(duration) || duration < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "The duration must be finite and not negative.");
        }

        if (options.Rates.Count == 0 || options.Rates.Any(r => !double.IsFinite(r) || r <= 0) || !(options.MaxFrameDeltaMs > 0))
        {
            throw new ArgumentException("Rates must be positive and the maximum frame step positive.", nameof(options));
        }

        Duration = duration;
        _minRate = options.MinRate;
        _maxRate = options.MaxRate;
        _maxFrameDeltaMs = options.MaxFrameDeltaMs;
        Rate = Math.Clamp(1.0, _minRate, _maxRate); // real time, or the nearest offered rate
    }

    public double Duration { get; }

    public double Position { get; private set; }

    public bool IsPlaying { get; private set; }

    public double Rate
    {
        get;
        private set => field = value >= _minRate && value <= _maxRate
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Playback rates run from {_minRate}× to {_maxRate}×.");
    }

    public void SetRate(double rate) => Rate = rate;

    /// <summary>Starts playing; from the end, it starts again from the beginning.</summary>
    public void Play()
    {
        if (Position >= Duration)
        {
            Position = 0;
        }

        IsPlaying = true;
        _lastFrameMs = null; // the next frame only records its timestamp
    }

    public void Pause() => IsPlaying = false;

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is NaN.</exception>
    public void Seek(double position)
    {
        if (double.IsNaN(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Can't seek to NaN.");
        }

        Position = Math.Clamp(position, 0, Duration);
    }

    public void BeginScrub()
    {
        _resumeAfterScrub = IsPlaying;
        IsPlaying = false;
    }

    public void EndScrub()
    {
        if (_resumeAfterScrub)
        {
            _resumeAfterScrub = false;
            Play();
        }
    }

    public double Advance(double frameTimestampMs)
    {
        if (!double.IsFinite(frameTimestampMs))
        {
            return Position;
        }

        if (IsPlaying && _lastFrameMs is double last)
        {
            // Never backwards, and capped so a long pause between frames doesn't jump ahead.
            double dt = Math.Clamp(frameTimestampMs - last, 0, _maxFrameDeltaMs) / 1000.0;
            Position = Math.Min(Position + (dt * Rate), Duration);
            if (Position >= Duration)
            {
                IsPlaying = false;
            }
        }

        _lastFrameMs = frameTimestampMs;
        return Position;
    }
}