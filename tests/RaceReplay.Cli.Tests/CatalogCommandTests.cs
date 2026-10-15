using System.Text;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using RaceReplay.Application;
using RaceReplay.Application.Ports;
using RaceReplay.Bundle;
using RaceReplay.Bundle.Catalog;
using RaceReplay.Domain;
using RaceReplay.Synthetic;

namespace RaceReplay.Cli.Tests;

public sealed class CatalogCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Catalog_DemoBundles_ListsEachInPathOrder()
    {
        string bundles = _host.PathTo("bundles");
        await Build(2, Path.Combine(bundles, "demo"));
        await Build(1, Path.Combine(bundles, "demo"));

        int exit = await _host.RunAsync("catalog", "--bundles", bundles);

        Assert.Equal(ExitCodes.Success, exit);
        BundleCatalogDto catalog = ReadCatalog(Path.Combine(bundles, ReplayCatalog.FileName));
        Assert.Equal(ReplayCatalog.DemoMode, catalog.Mode);
        Assert.Equal(CliTestHost.Now.UtcDateTime, catalog.GeneratedUtc);
        Assert.Equal(["demo/1/", "demo/2/"], catalog.Sessions.Select(s => s.Path));
        Assert.Equal(["demo-1", "demo-2"], catalog.Sessions.Select(s => s.Id));
        Assert.All(catalog.Sessions, s => Assert.StartsWith("Demo GP — ", s.Title, StringComparison.Ordinal));
        Assert.All(catalog.Sessions, s => Assert.Equal("synthetic", s.Source));

        string folder = Path.Combine(bundles, "demo", "1");
        long expectedBytes = new FileInfo(Path.Combine(folder, BundleFiles.Manifest)).Length + new FileInfo(Path.Combine(folder, BundleFiles.Positions)).Length;
        Assert.Equal(expectedBytes, catalog.Sessions[0].Bytes);
    }

    [Fact]
    public async Task Catalog_EmptyFolder_WritesAnEmptyCatalogue()
    {
        string bundles = _host.PathTo("empty");
        Directory.CreateDirectory(bundles);

        int exit = await _host.RunAsync("catalog", "--bundles", bundles, "--mode", "demo");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(ReadCatalog(Path.Combine(bundles, ReplayCatalog.FileName)).Sessions);
    }

    [Fact]
    public async Task Catalog_RealRaceInDemoMode_RefusesAndWritesNothing()
    {
        string bundles = _host.PathTo("bundles");
        await BuildReal(bundles);

        int exit = await _host.RunAsync("catalog", "--bundles", bundles);

        Assert.Equal(ExitCodes.Data, exit);
        Assert.Contains("ADR-0008", _host.Error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(bundles, ReplayCatalog.FileName)));
    }

    [Fact]
    public async Task Catalog_RealRaceInHostedMode_IsListedByYearAndKey()
    {
        string bundles = _host.PathTo("bundles");
        await BuildReal(bundles);

        int exit = await _host.RunAsync("catalog", "--bundles", bundles, "--mode", "hosted");

        Assert.Equal(ExitCodes.Success, exit);
        CatalogSessionDto session = Assert.Single(ReadCatalog(Path.Combine(bundles, ReplayCatalog.FileName)).Sessions);
        Assert.Equal($"{session.Year}-9839", session.Id);
        Assert.Equal($"{session.Year}/9839/", session.Path);
        Assert.Equal("openf1", session.Source);
        Assert.Equal(9839, session.Key);
    }

    [Fact]
    public async Task Catalog_OutOption_WritesThere()
    {
        string bundles = _host.PathTo("bundles");
        await Build(1, Path.Combine(bundles, "demo"));
        string output = _host.PathTo("elsewhere.json");

        int exit = await _host.RunAsync("catalog", "--bundles", bundles, "--out", output);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(ReadCatalog(output).Sessions);
    }

    [Fact]
    public async Task Catalog_CorruptManifest_ExitsWithDataError()
    {
        string bundles = _host.PathTo("bundles");
        string folder = await Build(1, Path.Combine(bundles, "demo"));
        await File.WriteAllTextAsync(Path.Combine(folder, BundleFiles.Manifest), "{ not json", TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Data, await _host.RunAsync("catalog", "--bundles", bundles));
    }

    [Fact]
    public async Task Catalog_MissingPositionsFile_ExitsWithDataError()
    {
        string bundles = _host.PathTo("bundles");
        string folder = await Build(1, Path.Combine(bundles, "demo"));
        File.Delete(Path.Combine(folder, BundleFiles.Positions));

        Assert.Equal(ExitCodes.Data, await _host.RunAsync("catalog", "--bundles", bundles));
    }

    [Fact]
    public async Task Catalog_ManifestNamingAnotherFile_ExitsWithDataError()
    {
        string bundles = _host.PathTo("bundles");
        string folder = await Build(1, Path.Combine(bundles, "demo"));
        string manifestPath = Path.Combine(folder, BundleFiles.Manifest);
        JsonNode manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, TestContext.Current.CancellationToken))!;
        manifest["channels"]!["positions"]!["file"] = "../../secret.bin";
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Data, await _host.RunAsync("catalog", "--bundles", bundles));
    }

    [Fact]
    public async Task Catalog_BundleDirectlyInTheRoot_ExitsWithDataError()
    {
        string bundles = await Build(1, _host.PathTo("flat"));

        Assert.Equal(ExitCodes.Data, await _host.RunAsync("catalog", "--bundles", bundles));
    }

    [Fact]
    public async Task Catalog_MissingFolder_ExitsWithDataError() =>
        Assert.Equal(ExitCodes.Data, await _host.RunAsync("catalog", "--bundles", _host.PathTo("nowhere")));

    [Theory]
    [InlineData("catalog")]
    [InlineData("catalog", "--bundles", "x", "--mode", "everything")]
    public async Task Catalog_InvalidCommandLine_ExitsWithUsage(params string[] args) =>
        Assert.Equal(ExitCodes.Usage, await _host.RunAsync(args));

    private async Task<string> Build(int seed, string output)
    {
        Assert.Equal(ExitCodes.Success, await _host.RunAsync("build", "--seed", seed.ToString(System.Globalization.CultureInfo.InvariantCulture), "--out", output));
        return Path.Combine(output, seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // A bundle with real-data provenance, built from synthetic data re-keyed as an OpenF1 session.
    private async Task BuildReal(string bundles)
    {
        SyntheticRace race = _host.Services.GetRequiredService<SyntheticRaceGenerator>().Generate(1);
        SessionInfo s = race.Raw.Session;
        RawSessionData raw = race.Raw with { Session = new SessionInfo(new SessionKey(9839), s.Year, s.Type, s.Name, s.MeetingName, s.Circuit, s.StartUtc) };
        ReplaySession replay = _host.Services.GetRequiredService<ReplayBuilder>().Build(raw, new BuildOptions("race-replay test")).Replay;
        await _host.Services.GetRequiredService<IReplayPackageStore>()
            .SaveAsync(replay, bundles, PackageLayout.Directory, TestContext.Current.CancellationToken);
    }

    private static BundleCatalogDto ReadCatalog(string path) => ReplayCatalog.Deserialize(File.ReadAllBytes(path));
}