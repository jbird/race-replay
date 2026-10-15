using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using RaceReplay.Synthetic;

namespace RaceReplay.Cli.Tests;

/// <summary>
/// The CLI's real composition, with a short synthetic race, a fixed clock and captured output. Owns a temporary folder.
/// </summary>
internal sealed class CliTestHost : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);

    public CliTestHost()
    {
        Directory.CreateDirectory(Root);
        Services = new ServiceCollection()
            .AddRaceReplayCli()
            .AddSingleton(new SyntheticRaceOptions { Laps = 2, DriverCount = 4 })
            .AddSingleton<TimeProvider>(new FakeTimeProvider(Now))
            .AddSingleton(new CliConsole(Out, Error))
            .BuildServiceProvider();
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "race-replay-tests", Path.GetRandomFileName());

    public ServiceProvider Services { get; }

    public StringWriter Out { get; } = new();

    public StringWriter Error { get; } = new();

    public Task<int> RunAsync(params string[] args) => CliCommands.RunAsync(Services, args);

    public string PathTo(params string[] parts) => Path.Combine([Root, .. parts]);

    public void Dispose()
    {
        Services.Dispose();
        Out.Dispose();
        Error.Dispose();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}