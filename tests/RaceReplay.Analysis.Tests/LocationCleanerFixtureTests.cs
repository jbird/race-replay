using RaceReplay.Domain;
using RaceReplay.TestKit;

namespace RaceReplay.Analysis.Tests;

/// <summary>
/// The cleaner on real OpenF1 data: the three races recorded in spike S1, two drivers each, 75–90 s of running.
/// S1 found real glitches at 0.1–2.4 per car-hour, and these excerpts total about 0.13 car-hours.
/// </summary>
public sealed class LocationCleanerFixtureTests
{
    // São Paulo 2024's car 23 didn't start: OpenF1 reports (0, 0, 0) for it all session (S1, the fixture's README).
    private static readonly HashSet<(string Race, int Driver)> s_nonStarters = [("2024-sao-paulo", 23)];

    private static readonly LocationCleaner s_cleaner = new(new CleaningOptions());

    public static TheoryData<string> Races => [.. RecordedFixtures.Races];

    public static TheoryData<string, int> NonStarters => [.. s_nonStarters.Select(car => (car.Race, car.Driver))];

    [Theory, MemberData(nameof(Races))]
    public void Clean_RecordedRace_KeepsEveryRunningCarsSamplesInOneRun(string race)
    {
        IEnumerable<KeyValuePair<DriverNumber, IReadOnlyList<RawLocationSample>>> running =
            RecordedFixtures.Locations(race).Where(car => !s_nonStarters.Contains((race, car.Key.Value)));

        Assert.NotEmpty(running);
        foreach ((DriverNumber driver, IReadOnlyList<RawLocationSample> samples) in running)
        {
            CleanedTrace trace = s_cleaner.Clean(driver, samples);

            // S1: no duplicates, no reordering, no gap over 2 s and no placeholder while a car runs; jitter isn't a spike.
            Assert.Equal(new CleaningCounts(samples.Count, 0, 0, 0, 0, 1), trace.Counts);
            Assert.Equal(samples, Assert.Single(trace.Runs));
        }
    }

    [Theory, MemberData(nameof(NonStarters))]
    public void Clean_RecordedNonStarter_DropsEveryPlaceholderAndLeavesNoRun(string race, int number)
    {
        DriverNumber driver = new(number);
        IReadOnlyList<RawLocationSample> samples = RecordedFixtures.Locations(race)[driver];

        CleanedTrace trace = s_cleaner.Clean(driver, samples);

        Assert.NotEmpty(samples);
        Assert.Empty(trace.Runs);
        Assert.Equal(new CleaningCounts(samples.Count, 0, 0, samples.Count, 0, 0), trace.Counts);
    }
}