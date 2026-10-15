using RaceReplay.Domain;

namespace RaceReplay.Playback;

/// <summary>
/// Answers "where is every car at time t?" in O(cars) without allocating, so seeking and scrubbing are free
/// (docs/05 §4.2, NFR-01, NFR-02). One instance per replay; the caller owns the frame buffer.
/// </summary>
public sealed class FrameSampler
{
    /// <summary>Doubles per car in a frame: x, y, visible (0 or 1), progress (NaN when not derived).</summary>
    public const int Stride = 4;

    private readonly ReplayTimeline _timeline;
    private readonly CarChannels[] _cars;

    public FrameSampler(ReplaySession replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        _timeline = replay.Timeline;
        _cars = [.. replay.Cars];
    }

    public int CarCount => _cars.Length;

    /// <summary>Length of the frame buffer <see cref="Sample"/> fills: cars × <see cref="Stride"/>.</summary>
    public int FrameLength => _cars.Length * Stride;

    /// <summary>
    /// Fills <paramref name="frame"/> for <paramref name="position"/> seconds since T0, in driver order. Between two valid
    /// samples a car is interpolated; at the edge of a data gap it holds the nearest valid sample; with no valid sample
    /// either side it is hidden.
    /// </summary>
    public void Sample(double position, Span<double> frame)
    {
        if (frame.Length < FrameLength)
        {
            throw new ArgumentException($"The frame needs {FrameLength} values, not {frame.Length}.", nameof(frame));
        }

        double f = _timeline.SampleIndexAt(position); // clamped to the grid; NaN maps to 0
        int i = (int)f;
        int j = Math.Min(i + 1, _timeline.SampleCount - 1);
        double a = f - i;

        for (int c = 0; c < _cars.Length; c++)
        {
            CarChannels car = _cars[c];
            Span<double> o = frame.Slice(c * Stride, Stride);
            bool vi = car.Valid[i], vj = car.Valid[j];
            if (vi && vj)
            {
                ReadOnlySpan<float> x = car.X.Span, y = car.Y.Span;
                o[0] = x[i] + ((x[j] - x[i]) * a);
                o[1] = y[i] + ((y[j] - y[i]) * a);
                o[2] = 1;
                o[3] = car.HasProgress ? car.Progress.Span[i] + ((car.Progress.Span[j] - car.Progress.Span[i]) * a) : double.NaN;
            }
            else if (vi || vj)
            {
                int k = vi ? i : j;
                o[0] = car.X.Span[k];
                o[1] = car.Y.Span[k];
                o[2] = 1;
                o[3] = car.HasProgress ? car.Progress.Span[k] : double.NaN;
            }
            else
            {
                o[0] = o[1] = o[3] = double.NaN;
                o[2] = 0;
            }
        }
    }
}