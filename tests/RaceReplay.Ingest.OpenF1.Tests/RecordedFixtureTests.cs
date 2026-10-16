using System.Text.Json;

using RaceReplay.TestKit;

namespace RaceReplay.Ingest.OpenF1.Tests;

/// <summary>
/// Guards the trimmed OpenF1 fixtures (docs/06 §9, ADR-0008): small, from known endpoints, two drivers at most, and
/// carrying the CC BY-NC-SA notice. Real-race data beyond these excerpts is never committed.
/// </summary>
public sealed class RecordedFixtureTests
{
    private const long MaxTotalBytes = 300 * 1024;
    private const int MaxDrivers = 2;

    // docs/04 §2: the endpoints Race Replay reads.
    private static readonly HashSet<string> s_endpoints = new(StringComparer.Ordinal)
    {
        "sessions", "meetings", "drivers", "location", "laps", "pit", "race_control", "session_result", "intervals", "position",
    };

    private static readonly string s_root = RepositoryFiles.PathTo("tests", "RaceReplay.TestKit", "Fixtures", "OpenF1");

    public static TheoryData<string> Folders =>
        [.. Directory.EnumerateDirectories(s_root).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal)];

    [Fact]
    public void Fixtures_Exist()
    {
        Assert.NotEmpty(Folders);
    }

    [Fact]
    public void Fixtures_TogetherStayUnderTheSizeCap()
    {
        long total = Directory.EnumerateFiles(s_root, "*.json", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

        Assert.True(total <= MaxTotalBytes, $"Fixtures are {total:N0} B; the cap is {MaxTotalBytes:N0} B.");
    }

    [Theory, MemberData(nameof(Folders))]
    public void Fixture_FilesAreJsonArraysFromKnownEndpoints(string folder)
    {
        string[] files = [.. Directory.EnumerateFiles(Path.Combine(s_root, folder), "*.json")];

        Assert.NotEmpty(files);
        Assert.All(files, file =>
        {
            Assert.Contains(Path.GetFileNameWithoutExtension(file), s_endpoints);
            using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(file));
            Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        });
    }

    [Theory, MemberData(nameof(Folders))]
    public void Fixture_KeepsAtMostTwoDrivers(string folder)
    {
        foreach (string endpoint in new[] { "location", "laps", "intervals", "position", "drivers" })
        {
            string file = Path.Combine(s_root, folder, $"{endpoint}.json");
            if (!File.Exists(file))
            {
                continue;
            }

            using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(file));
            int drivers = json.RootElement.EnumerateArray().Select(r => r.GetProperty("driver_number").GetInt32()).Distinct().Count();
            Assert.True(drivers <= MaxDrivers, $"{folder}/{endpoint}.json has {drivers} drivers.");
        }
    }

    [Theory, MemberData(nameof(Folders))]
    public void Fixture_CarriesTheOpenF1Licence(string folder)
    {
        string readme = Path.Combine(s_root, folder, "README.md");

        Assert.True(File.Exists(readme), $"{folder} has no README.md.");
        string text = File.ReadAllText(readme);
        Assert.Contains("OpenF1", text, StringComparison.Ordinal);
        Assert.Contains("CC BY-NC-SA 4.0", text, StringComparison.Ordinal);
    }
}