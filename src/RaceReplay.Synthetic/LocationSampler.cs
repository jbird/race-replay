using RaceReplay.Domain;

namespace RaceReplay.Synthetic;

/// <summary>
/// Observes the simulated race the way the real feed does: integer (x, y, z) in track units at about 3.7 Hz per car,
/// each car on its own phase. This is the clean profile; fault injection (dropouts, spikes, placeholders, snaps)
/// arrives with the v1 fault profiles (docs/04 §11.2).
/// </summary>
internal static class LocationSampler
{
    private const double BaseElevation = 100; // track units; z is carried through but never used
    private const double ElevationSwing = 50;

    public static RawLocationSample[] Sample(
        RaceSimulation race,
        int car,
        DriverNumber driver,
        TrackModel track,
        DateTimeOffset raceStartUtc,
        double sampleRateHz,
        double phase)
    {
        double interval = 1 / sampleRateHz;
        double first = race.StartTime + (phase * interval);
        int count = (int)Math.Floor((race.EndTime[car] - first) / interval) + 1;
        RawLocationSample[] samples = new RawLocationSample[Math.Max(count, 0)];
        double unitsPerMetre = track.UnitsPerMetre;
        for (int k = 0; k < samples.Length; k++)
        {
            double time = first + (k * interval);
            double s = race.ProgressAt(car, time) * unitsPerMetre;
            TrackPoint p = track.PointAt(s);
            double z = BaseElevation + (ElevationSwing * Math.Sin(2 * Math.PI * s / track.LapLength));
            samples[k] = new RawLocationSample(
                raceStartUtc.AddTicks((long)Math.Round(time * TimeSpan.TicksPerSecond, MidpointRounding.ToEven)),
                driver,
                (int)Math.Round(p.X, MidpointRounding.ToEven),
                (int)Math.Round(p.Y, MidpointRounding.ToEven),
                (int)Math.Round(z, MidpointRounding.ToEven));
        }

        return samples;
    }
}