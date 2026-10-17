using System.Globalization;
using System.Text.Json;

using RaceReplay.Domain;

namespace RaceReplay.TestKit;

/// <summary>
/// The trimmed OpenF1 recordings from spike S1 (docs/06 §9), read straight from their JSON so tests below the adapter
/// can use real data before the OpenF1 client exists.
/// </summary>
public static class RecordedFixtures
{
    /// <summary>The fixture folders: one race each.</summary>
    public static IReadOnlyList<string> Races =>
        [.. Directory.EnumerateDirectories(Root).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal)];

    /// <summary>The folder holding one subfolder per race.</summary>
    public static string Root => RepositoryFiles.PathTo("tests", "RaceReplay.TestKit", "Fixtures", "OpenF1");

    /// <summary>Each driver's <c>location</c> samples in <paramref name="race"/>, in the order the API returned them.</summary>
    public static IReadOnlyDictionary<DriverNumber, IReadOnlyList<RawLocationSample>> Locations(string race)
    {
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, race, "location.json")));
        Dictionary<DriverNumber, List<RawLocationSample>> byDriver = [];
        foreach (JsonElement row in json.RootElement.EnumerateArray())
        {
            DriverNumber driver = new(row.GetProperty("driver_number").GetInt32());
            RawLocationSample sample = new(
                DateTimeOffset.Parse(row.GetProperty("date").GetString()!, CultureInfo.InvariantCulture),
                driver,
                row.GetProperty("x").GetInt32(),
                row.GetProperty("y").GetInt32(),
                row.GetProperty("z").GetInt32());
            if (!byDriver.TryGetValue(driver, out List<RawLocationSample>? samples))
            {
                byDriver[driver] = samples = [];
            }

            samples.Add(sample);
        }

        return byDriver.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<RawLocationSample>)pair.Value);
    }
}