using System.CommandLine;
using System.Globalization;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

using RaceReplay.Analysis;
using RaceReplay.Application;
using RaceReplay.Application.Ports;
using RaceReplay.Bundle.Catalog;
using RaceReplay.Domain;
using RaceReplay.Synthetic;

namespace RaceReplay.Cli;

/// <summary>The command line (docs/04 §6). System.CommandLine stays at this edge; handlers are plain DI classes.</summary>
public static class CliCommands
{
    /// <summary>Tool name and version, recorded in each replay's provenance.</summary>
    public static string BuilderName { get; } =
        $"race-replay {typeof(CliCommands).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev"}";

    public static RootCommand CreateRoot(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new RootCommand("Race Replay: build replay packages from OpenF1 sessions or seeded synthetic races.")
        {
            Build(services),
            Catalog(services),
        };
    }

    /// <summary>Parses and runs a command line, mapping parse and validation errors to <see cref="ExitCodes.Usage"/>.</summary>
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        ParseResult parsed = CreateRoot(services).Parse(args);
        if (parsed.Errors.Count > 0)
        {
            CliConsole console = services.GetRequiredService<CliConsole>();
            foreach (System.CommandLine.Parsing.ParseError error in parsed.Errors)
            {
                await console.Error.WriteLineAsync(error.Message);
            }

            return ExitCodes.Usage;
        }

        return await parsed.InvokeAsync();
    }

    private static Command Build(IServiceProvider services)
    {
        // OpenF1 joins as a source (and becomes the default) in M3; .rrz packages in M4.
        Option<string> source = new("--source") { Description = "Data source", DefaultValueFactory = _ => SyntheticRawSessionProvider.Key };
        source.AcceptOnlyFromAmong(SyntheticRawSessionProvider.Key);
        Option<int?> seed = new("--seed") { Description = "Seed of a synthetic race" };
        Option<PackageLayout> layout = new("--layout") { Description = "How to store the replay", DefaultValueFactory = _ => PackageLayout.Directory };
        Option<string> output = new("--out") { Description = "Output folder", DefaultValueFactory = _ => "replays" };

        // Validators read the raw token, never the converted value: for "--seed x" conversion fails and reading the
        // value would throw, while System.CommandLine reports the conversion error itself.
        seed.Validators.Add(result =>
        {
            if (result.Tokens is [{ Value: string text }]
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                && value is < 0 or > SessionKey.MaxSyntheticSeed)
            {
                result.AddError($"--seed must be between 0 and {SessionKey.MaxSyntheticSeed}.");
            }
        });

        Command build = new("build", "Build a replay") { source, seed, layout, output };
        build.Validators.Add(result =>
        {
            if (result.GetResult(seed) is null)
            {
                result.AddError("--seed is required for a synthetic race.");
            }
        });
        build.SetAction((parse, cancellationToken) => services.GetRequiredService<BuildCommandHandler>().RunAsync(
            parse.GetValue(source)!,
            SessionKey.Synthetic(parse.GetValue(seed)!.Value),
            parse.GetValue(output)!,
            parse.GetValue(layout),
            cancellationToken));
        return build;
    }

    private static Command Catalog(IServiceProvider services)
    {
        Option<string> bundles = new("--bundles") { Description = "Folder of bundles to list", Required = true };
        Option<string> mode = new("--mode") { Description = "demo: synthetic races only; hosted: real races too", DefaultValueFactory = _ => ReplayCatalog.DemoMode };
        mode.AcceptOnlyFromAmong(ReplayCatalog.DemoMode, ReplayCatalog.HostedMode);
        Option<string?> output = new("--out") { Description = $"Catalogue file (default: <bundles>/{ReplayCatalog.FileName})" };

        Command catalog = new("catalog", "Regenerate the catalogue of replays a site serves") { bundles, mode, output };
        catalog.SetAction((parse, cancellationToken) => services.GetRequiredService<CatalogCommandHandler>().RunAsync(
            parse.GetValue(bundles)!, parse.GetValue(mode)!, parse.GetValue(output), cancellationToken));
        return catalog;
    }
}

/// <summary>The build-time composition root's registrations (docs/02 §6, Dependency Inversion).</summary>
public static class CliServices
{
    public static IServiceCollection AddRaceReplayCli(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(CliConsole.System);
        services.AddSingleton(new BuildOptions(CliCommands.BuilderName));

        services.AddSingleton(new ResamplingOptions());
        services.AddSingleton<TimelineResampler>();
        services.AddSingleton(new CentrelineOptions());
        services.AddSingleton<CentrelineBuilder>();
        services.AddSingleton<ReplayBuilder>();

        services.AddSingleton(SyntheticRaceOptions.Default);
        services.AddSingleton(sp => new SyntheticRaceGenerator(sp.GetRequiredService<SyntheticRaceOptions>()));
        services.AddKeyedSingleton<IRawSessionProvider, SyntheticRawSessionProvider>(SyntheticRawSessionProvider.Key);

        services.AddSingleton<IReplayPackageStore, FileSystemReplayPackageStore>();
        services.AddSingleton<BuildCommandHandler>();
        services.AddSingleton<CatalogCommandHandler>();
        return services;
    }
}