# Contributing to Race Replay

Thanks for your interest. Start with the [design documents](docs/README.md): they explain what is being built
and why, and [08 — Implementation plan](docs/08-implementation-plan.md) shows where the project is.

## Setup

**Codespaces.** Open the repository in a Codespace. The dev container installs the .NET 10 SDK, the GitHub CLI
and C# Dev Kit, then restores packages and tools.

**Locally.** Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Visual Studio 2026,
Rider, or VS Code with C# Dev Kit all work with `RaceReplay.slnx` and Microsoft Testing Platform.

```bash
dotnet restore
dotnet tool restore
dotnet build
dotnet test
```

## Run the web app with demo races

The app plays bundles from `src/RaceReplay.Web/wwwroot/bundles/`, which is gitignored. Build a demo race or two
with the CLI, then a catalogue listing them:

```bash
dotnet run --project src/RaceReplay.Cli -- build --source synthetic --seed 1 --out src/RaceReplay.Web/wwwroot/bundles/demo
```

```bash
dotnet run --project src/RaceReplay.Cli -- catalog --bundles src/RaceReplay.Web/wwwroot/bundles
```

Then run the app with hot reload and open the address it prints:

```bash
dotnet watch --project src/RaceReplay.Web
```

## Measuring frame rate

Add `?perf` to a replay's address, for example https://jbird.github.io/race-replay/replay/demo/1?perf. Press
Play, choose 64× to load it fully, and wait 10 seconds. Every 10 seconds the console and a panel at the bottom
right show:

- frames per second, and frames over 20 ms;
- the frame interval;
- the time inside each frame, split into canvas drawing and .NET (the clock, sampling, interop and the 10 Hz
  view-state renders).

Measure a published Release build: the public site, or `dotnet publish src/RaceReplay.Web -c Release` with demo
bundles copied into `wwwroot/bundles/`, served by any static server. Debug builds are slower. Without `?perf`,
the loop makes no timing calls. [Spike S0](docs/spikes/S0.md) records the results.

## Before opening a pull request

CI runs these on Linux, Windows and macOS; the gates run once, on Linux. Run them locally first:

```bash
dotnet format --verify-no-changes
dotnet test --coverage --coverage-output-format cobertura --results-directory TestResults
dotnet run scripts/check-docs.cs -- .
```

- **Warnings are errors**, including analyzer and code-style warnings.
- **Packages** are versioned centrally in `Directory.Packages.props`, and restores are locked. After changing a
  package, run `dotnet restore` and commit the updated `packages.lock.json` files.
- **Tests** follow [06 — Testing strategy](docs/06-testing-strategy.md): `Subject_Scenario_Expectation` names,
  Arrange–Act–Assert, no network, no wall clock, seeded randomness.
- **Interfaces only at real seams** ([ADR-0009](docs/adr/0009-interfaces-at-seams-ports-in-application.md)).
  The architecture tests enforce the project dependency rules.

## Pull requests

- Branch from `main` (`feat/…`, `fix/…`, `spike/…`, `docs/…`). Pull requests are squash-merged.
- Use a **Conventional Commit** title, for example `feat(analysis): virtual leader gaps`. It becomes the commit
  message on `main`.
- Add a `type:` and an `area:` label. Add `data-policy` if the change affects what is built, packaged or published.
- Fill in the pull request checklist.

## Data policy

**Never commit real-race data** ([ADR-0008](docs/adr/0008-bring-your-own-bundles.md)): no real-race bundles or
`.rrz` packages, and no raw OpenF1 responses beyond the small, trimmed test fixtures. The `.gitignore` keeps the
usual output folders out, and the deploy workflow rejects non-synthetic bundles. Bug reports about data should
attach the build report, not the package.

## Adding a synthetic demo race

Demo races are generated from seeds listed in `config/demo-races.json`. Add an entry with a seed and a title,
then check the race locally before opening a pull request. The title is a note for people: the catalogue takes
its title from the bundle. A test checks that the seeds are unique and that their circuits have different names.

## Adding an event extractor

Event markers come from `IEventExtractor` implementations in `RaceReplay.Application`. Add a class, register it
in the CLI's composition root, and cover it with tests on a synthetic race. `ReplayBuilder` doesn't change.

## Code of conduct

This project follows the [Code of conduct](CODE_OF_CONDUCT.md).
