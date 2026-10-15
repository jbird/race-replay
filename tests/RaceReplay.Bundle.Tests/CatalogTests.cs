using System.Text;

using RaceReplay.Bundle.Catalog;
using RaceReplay.TestKit;

namespace RaceReplay.Bundle.Tests;

public sealed class CatalogTests
{
    private static readonly BundleCatalogDto s_catalog = new()
    {
        SchemaVersion = ReplayCatalog.SchemaVersion,
        GeneratedUtc = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc),
        Mode = ReplayCatalog.DemoMode,
        Sessions = [ReplayCatalog.Describe(ReplayBundleWriter.Write(TestReplays.Small()).Manifest, "demo/7/", 1234)],
    };

    [Fact]
    public void Describe_SyntheticManifest_IsADemoEntryKeyedBySeed()
    {
        CatalogSessionDto entry = s_catalog.Sessions[0];

        Assert.Equal("demo-7", entry.Id);
        Assert.Equal("Demo GP — Kestrel Park (seed 7)", entry.Title);
        Assert.Equal(("synthetic", 7, (int?)null, (int?)null), (entry.Source, entry.Seed, entry.Year, entry.Key));
        Assert.Equal("1.0", entry.BundleSchema);
        Assert.Equal(1234, entry.Bytes);
    }

    [Fact]
    public void Deserialize_SerializedCatalogue_RoundTrips()
    {
        BundleCatalogDto read = ReplayCatalog.Deserialize(ReplayCatalog.Serialize(s_catalog));

        Assert.Equal(s_catalog.Mode, read.Mode);
        Assert.Equal(s_catalog.GeneratedUtc, read.GeneratedUtc);
        Assert.Equal(s_catalog.Sessions, read.Sessions);
    }

    [Theory]
    [InlineData("demo/1/", true)]
    [InlineData("2025/9839/", true)]
    [InlineData("demo/1", false)]          // must be a folder
    [InlineData("/demo/1/", false)]        // absolute
    [InlineData("../outside/", false)]
    [InlineData("demo/../../x/", false)]
    [InlineData("./demo/", false)]
    [InlineData("https://evil.example/", false)]
    [InlineData("demo\\1/", false)]
    [InlineData("/", false)]
    public void IsSafeRelativeFolder_Path_AcceptsOnlyFoldersInsideTheSite(string path, bool safe) =>
        Assert.Equal(safe, ReplayCatalog.IsSafeRelativeFolder(path));

    [Theory]
    [InlineData("\"path\":\"demo/7/\"", "\"path\":\"../../etc/\"")]
    [InlineData("\"mode\":\"demo\"", "\"mode\":\"everything\"")]
    [InlineData("\"schemaVersion\":\"1.0\"", "\"schemaVersion\":\"2.0\"")]
    [InlineData("\"mode\":\"demo\",", "")]
    public void Deserialize_UnsafeOrUnsupported_ThrowsBundleFormatException(string find, string replace)
    {
        string json = Encoding.UTF8.GetString(ReplayCatalog.Serialize(s_catalog)).Replace(find, replace, StringComparison.Ordinal);

        Assert.Throws<BundleFormatException>(() => ReplayCatalog.Deserialize(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"sessions\":[null]}")]
    public void Deserialize_NotACatalogue_ThrowsBundleFormatException(string json) =>
        Assert.Throws<BundleFormatException>(() => ReplayCatalog.Deserialize(Encoding.UTF8.GetBytes(json)));
}