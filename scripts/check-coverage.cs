#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

// Coverage gate (shared template). Reads a merged Cobertura report (ReportGenerator output) and enforces
// per-assembly line coverage from a JSON config.
//
// Usage: dotnet run scripts/check-coverage.cs -- <cobertura.xml> [config.json]   (default config: config/coverage.json)
//
// Config:
//   minimumLineCoverage  percentage every gated assembly must reach
//   assemblies           gated assemblies; one missing from the report fails, so a test project that
//                        silently stops running can't pass
//   pending              assemblies with no code yet (they don't appear in reports); one that shows up
//                        with coverable lines fails until it is moved to "assemblies"
//
// Exit codes: 0 pass, 1 gate failed, 2 usage or input error.

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("usage: check-coverage.cs <cobertura.xml> [config.json]");
    return 2;
}

string reportPath = args[0];
string configPath = args.Length > 1 ? args[1] : Path.Combine("config", "coverage.json");
if (!File.Exists(reportPath) || !File.Exists(configPath))
{
    Console.Error.WriteLine($"not found: {(File.Exists(reportPath) ? configPath : reportPath)}");
    return 2;
}

double minimum;
string[] gated;
string[] pending;
try
{
    using JsonDocument config = JsonDocument.Parse(
        File.ReadAllText(configPath),
        new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
    if (!config.RootElement.TryGetProperty("minimumLineCoverage", out JsonElement minimumElement)
        || !minimumElement.TryGetDouble(out minimum) || minimum is < 0 or > 100)
    {
        Console.Error.WriteLine($"{configPath}: \"minimumLineCoverage\" must be a number from 0 to 100");
        return 2;
    }

    gated = ReadNames(config.RootElement, "assemblies");
    pending = ReadNames(config.RootElement, "pending");
}
catch (Exception e) when (e is JsonException or InvalidOperationException)
{
    Console.Error.WriteLine($"{configPath}: invalid config: {e.Message}");
    return 2;
}

string[] both = [.. gated.Intersect(pending, StringComparer.Ordinal)];
if (both.Length > 0)
{
    Console.Error.WriteLine($"{configPath}: listed as both gated and pending: {string.Join(", ", both)}");
    return 2;
}

// Distinct (file, line) pairs per assembly; a line counts as covered if any class entry hit it.
Dictionary<string, Dictionary<(string File, int Line), bool>> lines = new(StringComparer.Ordinal);
try
{
    foreach (XElement package in XDocument.Load(reportPath).Descendants("package"))
    {
        string assembly = (string?)package.Attribute("name") ?? "";
        Dictionary<(string, int), bool> byLine = lines.TryGetValue(assembly, out var existing) ? existing : lines[assembly] = [];
        foreach (XElement cls in package.Descendants("class"))
        {
            string file = (string?)cls.Attribute("filename") ?? "";
            foreach (XElement line in cls.Elements("lines").Elements("line"))
            {
                int number = int.Parse((string?)line.Attribute("number") ?? "", CultureInfo.InvariantCulture);
                bool hit = long.Parse((string?)line.Attribute("hits") ?? "", CultureInfo.InvariantCulture) > 0;
                byLine[(file, number)] = byLine.GetValueOrDefault((file, number)) || hit;
            }
        }
    }
}
catch (Exception e) when (e is System.Xml.XmlException or FormatException or OverflowException)
{
    Console.Error.WriteLine($"{reportPath}: not a readable Cobertura report: {e.Message}");
    return 2;
}

bool githubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
int failures = 0;
Console.WriteLine($"Line coverage gate: {minimum.ToString("0.#", CultureInfo.InvariantCulture)} %");

foreach (string assembly in gated)
{
    // ReportGenerator omits assemblies without coverable lines, so "missing" has two causes.
    if (!lines.TryGetValue(assembly, out var byLine) || byLine.Count == 0)
    {
        Fail($"{assembly}: no coverable lines in the report. Either its tests didn't run, or it has no code yet " +
             $"and belongs in \"pending\" in {configPath}");
        continue;
    }

    int covered = byLine.Values.Count(hit => hit);
    double percent = 100.0 * covered / byLine.Count;
    string summary = $"{assembly}: {percent.ToString("0.0", CultureInfo.InvariantCulture)} % ({covered}/{byLine.Count} lines)";
    if (percent + 1e-9 < minimum)
    {
        Fail($"{summary} is below the gate");
    }
    else
    {
        Console.WriteLine($"  ok    {summary}");
    }
}

foreach (string assembly in pending)
{
    if (lines.TryGetValue(assembly, out var byLine) && byLine.Count > 0)
    {
        Fail($"{assembly}: has coverable code now; move it from \"pending\" to \"assemblies\" in {configPath}");
    }
    else
    {
        Console.WriteLine($"  -     {assembly}: pending (no coverable code yet)");
    }
}

Console.WriteLine(failures == 0 ? "Coverage gate passed." : $"Coverage gate failed: {failures} problem(s).");
return failures == 0 ? 0 : 1;

void Fail(string message)
{
    failures++;
    Console.WriteLine($"  FAIL  {message}");
    if (githubActions)
    {
        Console.WriteLine($"::error title=Coverage gate::{message}");
    }
}

static string[] ReadNames(JsonElement root, string property) =>
    root.TryGetProperty(property, out JsonElement array)
        ? [.. array.EnumerateArray().Select(e => e.GetString()!)]
        : [];