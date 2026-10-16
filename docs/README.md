# Race Replay — Proposal & Design

Race Replay is a browser-based visualiser that replays historical motorsport sessions. It animates every car's position on a track map, lets the viewer play, pause and scrub through the session, and shows a live gap chart that stays in sync with the playback cursor.

It is built entirely on C# and .NET 10. An offline .NET pipeline turns raw position data into compact replay bundles, and a Blazor WebAssembly client plays them back. The solution follows SOLID principles and is unit-tested with xUnit v3. It is hosted on GitHub as a public repository: CI in Actions, the site on Pages, and CLI downloads on Releases (ADR-0010).

Real-race data is not redistributed by default. Users build a real race locally into a single `.rrz` package and open it in the app, and the public site shows synthetic demo races (ADR-0008).

> **Status:** Proposal / design. The repository starts empty; [08 — Implementation plan](08-implementation-plan.md) walks from `git init` to a deployed site.

## Reading order

| # | Document | What it answers |
|---|----------|-----------------|
| 01 | [Proposal](01-proposal.md) | Why build it, what's in and out of scope, requirements, risks |
| 02 | [Architecture](02-architecture.md) | System shape, solution layout, dependency rules, SOLID mapping, .NET features used |
| 03 | [Domain model & algorithms](03-domain-and-algorithms.md) | Race progress, centreline, projection, resampling, gaps, events, edge cases |
| 04 | [Data pipeline & bundle format](04-data-pipeline-and-bundle-format.md) | OpenF1 ingest, rate limiting, caching, CLI, binary bundle and package spec, JSON Schemas, synthetic races |
| 05 | [Web client](05-web-client.md) | Blazor WebAssembly app, opening local packages, render loop, JS interop, playback, gap chart |
| 06 | [Testing strategy](06-testing-strategy.md) | xUnit v3 setup, test pyramid, fixtures, contract and architecture tests |
| 07 | [GitHub: repository, CI & delivery](07-repository-ci-and-delivery.md) | Shared template, settings as code, Actions workflows, Pages, Releases, supply chain, platform limits |
| 08 | [Implementation plan](08-implementation-plan.md) | Bootstrap commands, spikes, milestones, contingency and cut line, backlog, definition of done |
| — | [Architecture decision records](adr/README.md) | The key decisions, with context and consequences |
| — | [Spike S0: interop and render loop](spikes/S0.md) | Frame times, download size and GitHub Pages behaviour, measured in M1 |
| — | [Spike S1: OpenF1 data reality check](spikes/S1.md) | Units, location quality, race start, pit timing, race-control wording and the NFR-05 reference, from three real races |
| — | [Spike S2: packaging and hosting](spikes/S2.md) | Bundle transfer sizes on GitHub Pages, delta encoding and quantised progress, local packages in the browser |

## One-paragraph summary

Raw car positions arrive irregularly (about 3.7 samples per second per car), and official gaps only update about every four seconds. The design therefore does all heavy computation **once, offline**. A .NET CLI:

1. pulls a session from the OpenF1 API, or generates a seeded synthetic race;
2. builds a track centreline;
3. projects every sample onto it to get each car's *race progress*, with guards against bad projections;
4. resamples all cars onto one fixed timeline;
5. derives continuous gaps from progress;
6. writes a compact binary *replay package*.

The user opens the package in a Blazor WebAssembly client on GitHub Pages. The site itself ships only synthetic demo races. In the browser, a single playback clock drives everything. Drawing a frame is a constant-time array lookup per car, so seeking and scrubbing are free.

## Conventions used in these documents

- **Requirement IDs** (`FR-xx`, `NFR-xx`) are defined in the proposal and referenced elsewhere for traceability.
- **ADR links** (`ADR-0003`) point to the decision log in [`adr/`](adr/README.md).
- **Diagrams** use Mermaid, which GitHub renders natively.
- **Code snippets** target .NET 10 and C# 14. They are illustrative and show intent and shape. They are not copy-paste final code.
- **"Spike"** means a short, time-boxed experiment that settles an open technical question before the related milestone starts (see [08](08-implementation-plan.md#3-spikes)).
- Facts about third-party services and package versions were checked in **October 2026** and are flagged where they may drift.
