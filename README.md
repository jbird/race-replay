# Race Replay

Replay historical motorsport sessions in the browser: an animated track map with every car, play, pause and
scrub through the session, and a live gap chart that stays in sync with the playback cursor.

Race Replay is built entirely in C# on .NET 10. An offline CLI turns raw position data into compact replay
packages, and a Blazor WebAssembly app plays them back.

> **Status: early development.** The repository is being bootstrapped. Follow progress in the
> [implementation plan](docs/08-implementation-plan.md) and the project milestones.

**Live site:** <https://jbird.github.io/race-replay/>

## How it works

1. The `race-replay` CLI pulls a session from the [OpenF1](https://openf1.org) API, or generates a seeded
   synthetic race.
2. It builds a track centreline, projects every car onto it, resamples all cars onto one timeline and derives
   continuous gaps.
3. It writes a single `.rrz` replay package.
4. You open the package in the web app. It is read in your browser and never uploaded.

The public site ships synthetic demo races only. Real races are built and opened locally
([data policy](DATA-LICENSE.md)).

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build
dotnet test
dotnet watch --project src/RaceReplay.Web
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full workflow.

## Documentation

| Document | What it answers |
|---|---|
| [Proposal](docs/01-proposal.md) | Why build it, scope, requirements, risks |
| [Architecture](docs/02-architecture.md) | System shape, solution layout, dependency rules |
| [Domain model & algorithms](docs/03-domain-and-algorithms.md) | Race progress, projection, resampling, gaps |
| [Data pipeline & bundle format](docs/04-data-pipeline-and-bundle-format.md) | OpenF1 ingest, CLI, package format |
| [Web client](docs/05-web-client.md) | Blazor WebAssembly app, render loop, playback |
| [Testing strategy](docs/06-testing-strategy.md) | xUnit v3, fixtures, contract and architecture tests |
| [GitHub: repository, CI & delivery](docs/07-repository-ci-and-delivery.md) | Workflows, Pages, Releases |
| [Implementation plan](docs/08-implementation-plan.md) | Milestones and backlog |
| [Architecture decisions](docs/adr/README.md) | The key decisions and their trade-offs |

## Licence and attribution

- Code: [MIT](LICENSE).
- Real-race data: OpenF1, under CC BY-NC-SA 4.0. See [DATA-LICENSE.md](DATA-LICENSE.md).
- Race Replay is an unofficial fan project, not affiliated with the Formula 1 companies. See [NOTICE.md](NOTICE.md).
