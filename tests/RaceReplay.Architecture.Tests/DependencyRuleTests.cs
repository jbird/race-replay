using System.Reflection;

namespace RaceReplay.Architecture.Tests;

/// <summary>
/// Enforces the dependency rules in docs/02-architecture.md §4.2. <see cref="Assembly.GetReferencedAssemblies"/>
/// lists only references the compiler actually emitted, which is exactly what the rules care about.
/// </summary>
public sealed class DependencyRuleTests
{
    // One marker per src project. Its namespace is the project's root namespace and file name.
    private static readonly Type[] s_markers =
    [
        typeof(Domain.AssemblyMarker),
        typeof(Analysis.AssemblyMarker),
        typeof(Application.AssemblyMarker),
        typeof(Bundle.AssemblyMarker),
        typeof(OpenF1.Client.AssemblyMarker),
        typeof(Ingest.OpenF1.AssemblyMarker),
        typeof(Synthetic.AssemblyMarker),
        typeof(Playback.AssemblyMarker),
        typeof(Cli.AssemblyMarker),
        typeof(Web.AssemblyMarker),
    ];

    private static readonly Assembly[] s_productionAssemblies = [.. s_markers.Select(m => m.Assembly)];

    private static readonly HashSet<string> s_productionNames =
        s_productionAssemblies.Select(a => a.GetName().Name!).ToHashSet(StringComparer.Ordinal);

    public static TheoryData<string, string[]> Rules => new()
    {
        { "RaceReplay.Domain",        [] },
        { "RaceReplay.Analysis",      ["RaceReplay.Domain"] },
        { "RaceReplay.Application",   ["RaceReplay.Domain", "RaceReplay.Analysis"] },
        { "RaceReplay.Bundle",        ["RaceReplay.Domain"] },
        { "OpenF1.Client",            [] }, // shareable: no Race Replay references
        { "RaceReplay.Ingest.OpenF1", ["RaceReplay.Domain", "RaceReplay.Application", "OpenF1.Client"] },
        { "RaceReplay.Synthetic",     ["RaceReplay.Domain", "RaceReplay.Application"] },
        { "RaceReplay.Playback",      ["RaceReplay.Domain"] },
        { "race-replay",              ["RaceReplay.Domain", "RaceReplay.Analysis", "RaceReplay.Application", // Cli
                                       "RaceReplay.Bundle", "RaceReplay.Ingest.OpenF1", "RaceReplay.Synthetic"] },
        { "RaceReplay.Web",           ["RaceReplay.Domain", "RaceReplay.Playback", "RaceReplay.Bundle"] },
    };

    [Theory, MemberData(nameof(Rules))]
    public void Assembly_ReferencesOnlyAllowedProductionAssemblies(string assemblyName, string[] allowed)
    {
        IEnumerable<string> referenced = Load(assemblyName).GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(IsOwnAssembly);

        Assert.All(referenced, r => Assert.Contains(r, allowed));
    }

    [Fact]
    public void Rules_CoverEveryProductionAssembly()
    {
        IEnumerable<string> ruled = Rules.Select(row => row.Data.Item1);

        Assert.Equal(s_productionNames.Order(StringComparer.Ordinal), ruled.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Markers_CoverEverySourceProject()
    {
        string src = Path.Combine(RepositoryRoot(), "src");
        IEnumerable<string> projects = Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Select(p => Path.GetFileNameWithoutExtension(p));

        Assert.Equal(
            projects.Order(StringComparer.Ordinal),
            s_markers.Select(m => m.Namespace!).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("RaceReplay.Domain", "System.Net.Http")]
    [InlineData("RaceReplay.Domain", "System.Text.Json")]
    [InlineData("RaceReplay.Domain", "Microsoft.AspNetCore")]
    [InlineData("RaceReplay.Domain", "Microsoft.JSInterop")]
    [InlineData("RaceReplay.Analysis", "System.Net.Http")]
    [InlineData("RaceReplay.Playback", "Microsoft.AspNetCore.Components")]
    [InlineData("RaceReplay.Playback", "Microsoft.JSInterop")]
    [InlineData("RaceReplay.Playback", "System.Runtime.InteropServices.JavaScript")]
    public void Assembly_DoesNotReferenceBannedFramework(string assemblyName, string banned) =>
        Assert.DoesNotContain(Load(assemblyName).GetReferencedAssemblies(), a => IsInFamily(a.Name!, banned));

    [Theory]
    [InlineData("RaceReplay.Domain", true)]
    [InlineData("RaceReplay.NotYetListed", true)]
    [InlineData("OpenF1.Client", true)]
    [InlineData("race-replay", true)]
    [InlineData("System.Text.Json", false)]
    [InlineData("RaceReplayer", false)]
    public void IsOwnAssembly_RecognisesListedAndUnlistedProjectAssemblies(string name, bool expected) =>
        Assert.Equal(expected, IsOwnAssembly(name));

    [Theory]
    [InlineData("Microsoft.AspNetCore.Components", "Microsoft.AspNetCore.Components", true)]
    [InlineData("Microsoft.AspNetCore.Components.Web", "Microsoft.AspNetCore.Components", true)]
    [InlineData("Microsoft.AspNetCore.Components.Web", "Microsoft.AspNetCore", true)]
    [InlineData("System.Net.Http.Json", "System.Net.Http", true)]
    [InlineData("System.Net.HttpListener", "System.Net.Http", false)]
    public void IsInFamily_MatchesTheAssemblyAndItsChildren(string name, string family, bool expected) =>
        Assert.Equal(expected, IsInFamily(name, family));

    // Name prefixes catch a Race Replay assembly that was added without a marker or a rule.
    private static bool IsOwnAssembly(string name) =>
        s_productionNames.Contains(name)
        || name.StartsWith("RaceReplay.", StringComparison.Ordinal)
        || name.StartsWith("OpenF1.", StringComparison.Ordinal);

    private static bool IsInFamily(string name, string family) =>
        name == family || name.StartsWith(family + ".", StringComparison.Ordinal);

    private static Assembly Load(string name) =>
        s_productionAssemblies.Single(a => a.GetName().Name == name);

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RaceReplay.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"RaceReplay.slnx not found above {AppContext.BaseDirectory}.");
    }
}