using Microsoft.Extensions.DependencyInjection;

using RaceReplay.Analysis;
using RaceReplay.Application;
using RaceReplay.Application.Ports;
using RaceReplay.Domain;

namespace RaceReplay.Cli;

/// <summary>
/// <c>race-replay build</c>: load from the chosen source, build, store (docs/02 §8.1). It coordinates I/O and maps
/// outcomes to exit codes; the builder does the work.
/// </summary>
public sealed class BuildCommandHandler(
    IServiceProvider services,
    ReplayBuilder builder,
    BuildOptions options,
    IReplayPackageStore store,
    CliConsole console)
{
    public async Task<int> RunAsync(
        string source, SessionKey session, string outputDirectory, PackageLayout layout, CancellationToken cancellationToken)
    {
        IRawSessionProvider? provider = services.GetKeyedService<IRawSessionProvider>(source);
        if (provider is null)
        {
            await console.Error.WriteLineAsync($"Unknown source '{source}'.");
            return ExitCodes.Usage;
        }

        try
        {
            RawSessionData raw = await provider.LoadAsync(session, cancellationToken);
            BuildResult result = builder.Build(raw, options);
            string path = await store.SaveAsync(result.Replay, outputDirectory, layout, cancellationToken);
            await console.Out.WriteLineAsync(path);
            return ExitCodes.Success;
        }
        catch (Exception e) when (e is SessionNotFoundException or InsufficientDataException)
        {
            await console.Error.WriteLineAsync(e.Message);
            return ExitCodes.Data;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("Cancelled.");
            return ExitCodes.Cancelled;
        }
    }
}