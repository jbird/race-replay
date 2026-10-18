using System.Globalization;

using CsCheck;

using RaceReplay.Domain;
using RaceReplay.Synthetic;

using static RaceReplay.Analysis.Tests.CentrelineSessions;

namespace RaceReplay.Analysis.Tests;

/// <summary>
/// Property tests (docs/06 §4) over random stadiums, speeds, lap counts and directions. Radius ≥ 100 m at ≤ 70 m/s
/// keeps a sample chord's sag under 0.5 m, so the tolerances of <see cref="CentrelineBuilderTests"/> hold throughout.
/// </summary>
public sealed class CentrelineBuilderPropertyTests
{
    private sealed record Scenario(double Straight, double Radius, bool Clockwise, double[] Speeds, int[] Laps)
    {
        public SyntheticTrack Track => Clockwise ? SyntheticTrack.Stadium(Straight, Radius).Clockwise() : SyntheticTrack.Stadium(Straight, Radius);

        public SyntheticCar[] Cars => Numbered([.. Speeds.Select((v, i) => CentrelineBuilderTests.Car(Track, v, Laps[i], start: -150 - (i * 333)))]);

        public override string ToString() => string.Create(
            CultureInfo.InvariantCulture,
            $"stadium {Straight:F0} m × {Radius:F0} m{(Clockwise ? ", clockwise" : "")}, speeds [{string.Join(", ", Speeds.Select(v => v.ToString("F1", CultureInfo.InvariantCulture)))}], laps [{string.Join(", ", Laps)}]");
    }

    // One or two cars with one to four laps each: one to eight laps a session. Speeds within 5 % of each other, so
    // the 107 % rule keeps every lap.
    private static readonly Gen<Scenario> s_scenarios =
        from straight in Gen.Double[100, 1_000]
        from radius in Gen.Double[100, 300]
        from clockwise in Gen.Bool
        from speed in Gen.Double[30, 66]
        from cars in Gen.Int[1, 2]
        from spread in Gen.Double[0, 0.05].Array[cars]
        from laps in Gen.Int[1, 4].Array[cars]
        select new Scenario(straight, radius, clockwise, [.. spread.Select(s => speed * (1 + s))], laps);

    [Fact]
    public void Build_AnyStadium_UniformLoopOfTheRightLengthFromTheLine()
    {
        s_scenarios.Sample(
            scenario =>
            {
                SyntheticTrack track = scenario.Track;

                TrackModel model = Build(Race(scenario.Cars)).Track;

                TrackPoint[] p = model.Centreline.ToArray();
                double segment = model.LapLength / p.Length;
                for (int i = 0; i < p.Length; i++)
                {
                    TrackPoint a = p[i], b = p[(i + 1) % p.Length];
                    Assert.Equal(segment, Math.Sqrt(Math.Pow(b.X - (double)a.X, 2) + Math.Pow(b.Y - (double)a.Y, 2)), segment * 0.01);
                }

                Assert.Equal(track.LengthMetres, model.LapLength / model.UnitsPerMetre, track.LengthMetres * 0.005);
                Assert.InRange(Distance(Metres(p[0]), Metres(track.PointAt(0))), 0, 1);

                // s runs with the cars: point 1 is ahead of point 0, and every step turns the loop the way they do.
                (double x0, double y0) = Metres(p[0]);
                (double x1, double y1) = Metres(p[1]);
                Assert.True(((x1 - x0) * Math.Cos(track.HeadingAt(0))) + ((y1 - y0) * Math.Sin(track.HeadingAt(0))) > 0);
                double sign = scenario.Clockwise ? -1 : 1;
                for (int i = 0; i < p.Length; i++)
                {
                    TrackPoint a = p[i], b = p[(i + 1) % p.Length], c = p[(i + 2) % p.Length];
                    double cross = ((b.X - (double)a.X) * (c.Y - (double)b.Y)) - ((b.Y - (double)a.Y) * (c.X - (double)b.X));
                    Assert.True(sign * cross > -1e-3 * segment * segment, $"point {i + 1} turns against the direction of travel");
                }
            },
            iter: 100,
            print: s => s.ToString());
    }

    [Fact]
    public void Build_AnyStadium_InputOrderChangesNothing()
    {
        s_scenarios.Sample(
            scenario =>
            {
                RawSessionData raw = Race(scenario.Cars);
                RawSessionData shuffled = raw with
                {
                    Laps = [.. raw.Laps.Reverse()],
                    Locations = raw.Locations.Reverse().ToDictionary(p => p.Key, p => p.Value),
                };

                TrackModel a = Build(raw).Track;
                TrackModel b = Build(shuffled).Track;

                Assert.Equal(a.Centreline.ToArray(), b.Centreline.ToArray());
                Assert.Equal(a.DisplayRotationDegrees, b.DisplayRotationDegrees);
            },
            iter: 50,
            print: s => s.ToString());
    }
}