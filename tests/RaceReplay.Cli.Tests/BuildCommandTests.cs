using Microsoft.Extensions.DependencyInjection;

using RaceReplay.Application.Ports;
using RaceReplay.Bundle;
using RaceReplay.Domain;

namespace RaceReplay.Cli.Tests;

public sealed class BuildCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Build_SyntheticSeed_WritesAReadableBundleInItsSeedFolder()
    {
        string output = _host.PathTo("bundles", "demo");

        int exit = await _host.RunAsync("build", "--seed", "3", "--out", output);

        Assert.Equal(ExitCodes.Success, exit);
        string folder = Path.Combine(output, "3");
        Assert.Equal(folder, _host.Out.ToString().Trim());
        ReplaySession replay = ReplayBundleReader.Read(
            File.ReadAllBytes(Path.Combine(folder, BundleFiles.Manifest)), File.ReadAllBytes(Path.Combine(folder, BundleFiles.Positions)));
        Assert.Equal(SessionKey.Synthetic(3), replay.Session.Key);
        Assert.Equal(4, replay.Drivers.Count);
        Assert.Equal(CliTestHost.Now, replay.Provenance.RetrievedUtc);
        Assert.StartsWith("race-replay ", replay.Provenance.Builder, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_ExistingBundle_IsReplacedWithoutLeftovers()
    {
        string output = _host.PathTo("out");
        Directory.CreateDirectory(Path.Combine(output, "5"));
        await File.WriteAllTextAsync(Path.Combine(output, "5", "stale.txt"), "old", TestContext.Current.CancellationToken);

        int exit = await _host.RunAsync("build", "--seed", "5", "--out", output);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([BundleFiles.Manifest, BundleFiles.Positions], Directory.GetFiles(Path.Combine(output, "5")).Select(Path.GetFileName).Order());
        Assert.Equal(["5"], Directory.GetDirectories(output).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("build")]                                     // no seed
    [InlineData("build", "--seed", "-1")]
    [InlineData("build", "--seed", "x")]
    [InlineData("build", "--seed", "1", "--source", "openf1")] // arrives in M3
    [InlineData("build", "--seed", "1", "--layout", "zip")]
    [InlineData("frobnicate")]
    public async Task Build_InvalidCommandLine_ExitsWithUsage(params string[] args)
    {
        int exit = await _host.RunAsync(args);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.NotEmpty(_host.Error.ToString());
    }

    [Fact]
    public async Task Handler_KeyOutsideTheSourcesRange_ExitsWithDataError()
    {
        BuildCommandHandler handler = _host.Services.GetRequiredService<BuildCommandHandler>();

        int exit = await handler.RunAsync("synthetic", new SessionKey(9839), _host.Root, PackageLayout.Directory, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Data, exit);
        Assert.Contains("9839", _host.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handler_UnregisteredSource_ExitsWithUsage()
    {
        BuildCommandHandler handler = _host.Services.GetRequiredService<BuildCommandHandler>();

        int exit = await handler.RunAsync("openf1", new SessionKey(9839), _host.Root, PackageLayout.Directory, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Usage, exit);
    }

    [Fact]
    public async Task Handler_Cancelled_ExitsWith130()
    {
        BuildCommandHandler handler = _host.Services.GetRequiredService<BuildCommandHandler>();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        int exit = await handler.RunAsync("synthetic", SessionKey.Synthetic(1), _host.Root, PackageLayout.Directory, cancelled.Token);

        Assert.Equal(ExitCodes.Cancelled, exit);
    }
}