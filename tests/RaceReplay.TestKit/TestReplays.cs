using RaceReplay.Domain;

namespace RaceReplay.TestKit;

/// <summary>Small hand-built replays: cars circling a circular track, with known gaps in their data.</summary>
public static class TestReplays
{
    public const double RateHz = 4;
    public const float CentreX = 1_234.5f;
    public const float CentreY = -5_678.25f;
    public const float Radius = 5_000;

    public static readonly DateTimeOffset T0 = new(2026, 3, 2, 12, 59, 0, TimeSpan.Zero);

    private static readonly int[] s_numbers = [44, 16, 1, 63, 81, 4];

    public static ReplaySession Small(int cars = 3, int samples = 101, bool withProgress = false, bool allInvalid = false)
    {
        Driver[] drivers =
        [
            .. s_numbers.Take(cars).Select((n, i) => new Driver(
                new DriverNumber(n), $"D{n:00}"[..3], $"Test DRIVER{n}", $"Team {i / 2}", new RgbColour((byte)(40 * i), 128, 200))),
        ];

        CarChannels[] channels = new CarChannels[cars];
        for (int c = 0; c < cars; c++)
        {
            float[] x = new float[samples];
            float[] y = new float[samples];
            float[] progress = new float[samples];
            bool[] valid = new bool[samples];
            for (int k = 0; k < samples; k++)
            {
                double angle = (k * 0.05) + (c * 0.4);
                x[k] = (float)(CentreX + (Radius * Math.Cos(angle)));
                y[k] = (float)(CentreY + (Radius * Math.Sin(angle)));
                progress[k] = (float)(angle * Radius);
                valid[k] = !allInvalid && (k + (c * 7)) % 17 != 5; // a gap every 17 samples, staggered per car
            }

            channels[c] = new CarChannels(drivers[c].Number, x, y, ValidityMask.FromBools(valid), withProgress ? progress : default);
        }

        double duration = (samples - 1) / RateHz;
        ReplayTimeline timeline = new(T0, RateHz, samples, duration / 5,
        [
            new(0, duration / 5, SegmentKind.PreRace),
            new(duration / 5, duration * 0.9, SegmentKind.Racing),
            new(duration * 0.9, duration, SegmentKind.PostRace),
        ]);

        return new ReplaySession(
            new SessionInfo(SessionKey.Synthetic(7), 2026, "Race", "Race", "Kestrel Grand Prix", "Kestrel Park", T0.AddMinutes(1)),
            drivers,
            new TrackModel(Circle(), displayRotationDegrees: -12.5f),
            timeline,
            channels,
            GapSeries.Empty,
            [],
            Provenance.ForSynthetic(7, T0.AddDays(1), "race-replay test"));
    }

    private static TrackPoint[] Circle()
    {
        TrackPoint[] points = new TrackPoint[100];
        for (int i = 0; i < points.Length; i++)
        {
            double angle = 2 * Math.PI * i / points.Length;
            points[i] = new TrackPoint((float)(CentreX + (Radius * Math.Cos(angle))), (float)(CentreY + (Radius * Math.Sin(angle))));
        }

        return points;
    }
}