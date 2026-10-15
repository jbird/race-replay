using System.Text.Json;

using RaceReplay.Synthetic;
using RaceReplay.TestKit;

namespace RaceReplay.Cli.Tests;

/// <summary>Guards <c>config/demo-races.json</c>, which <c>assemble-site.sh</c> builds and deploys (docs/07 §6.3).</summary>
public sealed class DemoRacesConfigTests
{
    private static readonly int[] s_seeds = ReadSeeds();

    [Fact]
    public void DemoRaces_Seeds_AreUniqueAndPositive()
    {
        Assert.NotEmpty(s_seeds);
        Assert.All(s_seeds, seed => Assert.True(seed > 0, $"Seed {seed} is not positive."));
        Assert.Equal(s_seeds.Length, s_seeds.Distinct().Count());
    }

    [Fact]
    public void DemoRaces_Seeds_GenerateFullFieldsAtDistinctCircuits()
    {
        SyntheticRace[] races = [.. s_seeds.Select(seed => new SyntheticRaceGenerator().Generate(seed))];

        Assert.All(races, race => Assert.Equal(SyntheticRaceOptions.MaxDrivers, race.Raw.Drivers.Count)); // NFR-01 is set at 20 cars
        Assert.Equal(races.Length, races.Select(r => r.Circuit.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(races.Length, races.Select(r => r.Circuit.MeetingName).Distinct(StringComparer.Ordinal).Count());
    }

    private static int[] ReadSeeds()
    {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllBytes(RepositoryFiles.PathTo("config", "demo-races.json")));
        return [.. config.RootElement.GetProperty("races").EnumerateArray().Select(race => race.GetProperty("seed").GetInt32())];
    }
}