using System.Globalization;

using CsCheck;

using RaceReplay.Domain;
using RaceReplay.Synthetic;

namespace RaceReplay.Analysis.Tests;

/// <summary>
/// Property tests (docs/06 §4): a clean constant-speed car with a random mix of faults always cleans back to the clean
/// trace, less exactly the samples the faults spoilt. The trace is cut into blocks of <see cref="Block"/> samples with
/// at most one fault each, away from both ends, so faults are always at least three samples apart.
/// </summary>
public sealed class LocationCleanerPropertyTests
{
    private const int Block = 30;
    private const int FaultAt = 10;            // the sample in its block a point fault hits
    private const int DropoutFrom = 5;         // a dropout removes samples 5 to 24 of its block: about 5.4 s
    private const int DropoutTo = 25;

    // Up to 80 m/s, timestamp jitter within ±0.08 s can't make a clean sample look like a spike: that needs both of
    // its pairs under 0.18 s, but the two together span at least 0.54 − 0.16 s. A spike's neighbours, 43 m apart at
    // most, stay under 120 m/s across that span, so every spike is caught.
    private const double MaxSpeed = 80;
    private const double JitterSd = 0.03;
    private const double JitterMax = 0.08;

    private static readonly LocationCleaner s_cleaner = new(new CleaningOptions());
    private static readonly SyntheticTrack s_circle = SyntheticTrack.Circle(400);

    private enum Fault
    {
        None,
        Spike,
        Placeholder,
        Duplicate,
        OutOfOrder,
        Dropout,
    }

    private sealed record Scenario(double Speed, double StartProgress, int? JitterSeed, Fault[] Faults)
    {
        public override string ToString() => string.Create(
            CultureInfo.InvariantCulture,
            $"speed {Speed:F1} m/s, start {StartProgress:F0} m, jitter seed {JitterSeed?.ToString(CultureInfo.InvariantCulture) ?? "none"}, faults [{string.Join(", ", Faults)}]");
    }

    private static readonly Gen<Scenario> s_scenarios =
        from speed in Gen.Double[5, MaxSpeed]
        from start in Gen.Double[0, 3_000]
        from jittered in Gen.Bool
        from seed in Gen.Int[0, 1_000_000]
        from blocks in Gen.Int[3, 12]
        from faults in Gen.Enum<Fault>().Array[blocks]
        select new Scenario(speed, start, jittered ? seed : null, faults);

    [Fact]
    public void Clean_RandomFaults_LeavesExactlyTheUnspoiltSamples()
    {
        s_scenarios.Sample(
            scenario =>
            {
                (SyntheticCar clean, SyntheticCar faulty, HashSet<int> spoilt, Fault[] used) = Build(scenario);
                IReadOnlyList<RawLocationSample> expected = [.. clean.Samples().Where((_, i) => !spoilt.Contains(i))];

                CleanedTrace trace = s_cleaner.Clean(faulty.Driver, faulty.Samples());

                Assert.Equal(expected, trace.Runs.SelectMany(run => run));
                Assert.Equal(1 + used.Count(f => f == Fault.Dropout), trace.Runs.Count);
                Assert.Equal(used.Count(f => f == Fault.Spike), trace.Counts.Spikes);
                Assert.Equal(used.Count(f => f == Fault.Placeholder), trace.Counts.Placeholders);
                Assert.Equal(used.Count(f => f == Fault.Duplicate), trace.Counts.Duplicates);
                Assert.Equal(used.Count(f => f == Fault.OutOfOrder), trace.Counts.OutOfOrder);
            },
            iter: 300,
            print: s => s.ToString());
    }

    [Fact]
    public void Clean_RandomFaults_RunsAreStrictlyIncreasingWithoutPlaceholdersOrLongGaps()
    {
        s_scenarios.Sample(
            scenario =>
            {
                (_, SyntheticCar faulty, _, _) = Build(scenario);
                IReadOnlyList<RawLocationSample> input = faulty.Samples();

                CleanedTrace trace = s_cleaner.Clean(faulty.Driver, input);

                RawLocationSample[] all = [.. trace.Runs.SelectMany(run => run)];
                Assert.All(all.Zip(all.Skip(1)), pair => Assert.True(pair.Second.Timestamp > pair.First.Timestamp));
                Assert.DoesNotContain(all, s => s.X == 0 && s.Y == 0 && s.Z == 0);
                Assert.All(trace.Runs, run => Assert.All(run.Zip(run.Skip(1)),
                    pair => Assert.True((pair.Second.Timestamp - pair.First.Timestamp).TotalSeconds <= 2.0)));
                HashSet<RawLocationSample> given = [.. input];
                Assert.All(all, sample => Assert.Contains(sample, given));
            },
            iter: 300,
            print: s => s.ToString());
    }

    // The clean car, the faulty one, the clean indices the faults spoilt, and the faults actually placed (the first
    // and last blocks stay clean, so no fault touches either end of the trace).
    private static (SyntheticCar Clean, SyntheticCar Faulty, HashSet<int> Spoilt, Fault[] Used) Build(Scenario scenario)
    {
        int count = scenario.Faults.Length * Block;
        SyntheticCar clean = SyntheticCar.ConstantSpeed(s_circle, scenario.Speed, scenario.StartProgress)
            .WithDuration((count - 1) / SyntheticCar.DefaultSampleRateHz);
        if (scenario.JitterSeed is int seed)
        {
            clean = clean.WithTimestampJitter(JitterSd, JitterMax, seed);
        }

        SyntheticCar faulty = clean;
        HashSet<int> spoilt = [];
        List<Fault> used = [];
        for (int b = 1; b < scenario.Faults.Length - 1; b++)
        {
            int first = b * Block;
            Fault fault = scenario.Faults[b];
            used.Add(fault);
            switch (fault)
            {
                case Fault.Spike:
                    faulty = faulty.WithSpike(first + FaultAt);
                    spoilt.Add(first + FaultAt);
                    break;
                case Fault.Placeholder:
                    faulty = faulty.WithZeroPlaceholder(first + FaultAt);
                    spoilt.Add(first + FaultAt);
                    break;
                case Fault.Duplicate:
                    faulty = faulty.WithDuplicate(first + FaultAt); // an identical copy, so the kept sample is unchanged
                    break;
                case Fault.OutOfOrder:
                    faulty = faulty.WithOutOfOrder(first + FaultAt);
                    break;
                case Fault.Dropout:
                    // Half a sample interval early on both sides, so exactly samples 5 to 24 of the block go.
                    faulty = faulty.WithDropout(Seconds(first + DropoutFrom - 0.5), Seconds(first + DropoutTo - 0.5));
                    spoilt.UnionWith(Enumerable.Range(first + DropoutFrom, DropoutTo - DropoutFrom));
                    break;
            }
        }

        Assert.Equal(count, clean.SampleCount);
        return (clean, faulty, spoilt, [.. used]);
    }

    private static double Seconds(double sample) => sample / SyntheticCar.DefaultSampleRateHz;
}