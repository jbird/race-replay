# 01 — Proposal

## 1. Executive summary

Race Replay lets a fan pick a past race and watch it unfold as a top-down animation. Every car moves around a track map, a timeline lets the viewer jump to any moment, and a gap chart shows how the field spread out over time. A cursor on the chart tracks the playback position.

The project has three aims:

1. **A useful tool.** Live timing and TV coverage show a race once. Replaying it with full positional context makes strategy, undercuts and safety-car effects easy to see.
2. **A showcase of modern .NET.** The whole system is C#: an offline data pipeline, a binary file format, and a WebAssembly front end. It uses .NET 10 LTS, C# 14, xUnit v3 on Microsoft Testing Platform, and GitHub Actions.
3. **A clean-code exemplar.** The codebase starts from an empty repository and is shaped by SOLID principles, enforced dependency rules and a deterministic test suite.

The recommended approach is to **precompute replay bundles offline** and play them in a Blazor WebAssembly client hosted on GitHub Pages. There is no runtime server, no database and no running cost.

**Everything lives on GitHub** (ADR-0010). The project is a public repository on GitHub Free:

- source, issues and discussions in the repository;
- CI and deployment in Actions;
- the site on Pages;
- CLI downloads on Releases, with signed build provenance.

Build conventions are shared with the sibling racesim project through a GitHub template repository.

**Real-session data is not redistributed by default** (ADR-0008):

- Users build a real race locally with the CLI, as a single *replay package* file, and open it in the app. The file is read in the browser and never uploaded.
- The public site ships **synthetic demo races**: fictional circuits, drivers and teams generated through the same pipeline.
- Hosting real bundles on the site is an opt-in *hosted mode* for people who accept that responsibility.

## 2. Problem and motivation

| Problem | Effect today | How Race Replay addresses it |
|---|---|---|
| Position data is irregular and unaligned across cars (~3.7 Hz, independent timestamps) | Naive animation stutters, and cars can't be compared at the same instant | Resample every car onto one shared, fixed-rate timeline |
| Official gap data is coarse (~4 s updates) and only available in races | Gap charts look stepped and lag the action | Derive continuous gaps from each car's race progress |
| Raw coordinates carry no notion of "how far round the lap" | Running order, lapped cars and gaps can't be computed directly | Build a track centreline and project positions onto it |
| Full-session data is large and slow to fetch (rate-limited API) | A browser-only app would be slow and would hammer the API | Fetch once offline, cache, and produce a compact package |

### 2.1 Prior art and what's different

| Existing option | What it offers | Where Race Replay differs |
|---|---|---|
| Official live timing (F1 app / F1 TV) | Live track map and timing during sessions | Race Replay is about *after* the session: free scrubbing, a full-race gap chart, and spoiler-free viewing |
| MultiViewer | A desktop companion for watching F1 TV, with live timing and a track map | No subscription or video. A focused, browser-based replay of positions and gaps. |
| FastF1 (Python) and OpenF1 community projects | Data access and analysis libraries; one-off visualisations | An interactive, scrubbable player with continuous derived gaps, built as a reusable .NET application rather than notebooks or scripts |

The combination is the point: scrubbable replay, a synchronised continuous gap chart, spoiler-free mode, and an all-.NET, tested codebase.

## 3. Goals

- **G1** Animate all cars smoothly (60 fps target) on a track map for any supported historical race session.
- **G2** Allow instant play, pause, speed change and scrubbing to any point in the session.
- **G3** Show a gap-to-leader chart that is continuous, synchronised with playback, and usable as a second scrubber.
- **G4** Make turning a new race into a replay a single CLI command that produces one file the app can open.
- **G5** Keep the solution easy to change: SOLID design, clear module boundaries, high unit-test coverage of the core logic.
- **G6** Host code, CI, the public site and CLI downloads on GitHub (public repository, GitHub Free) at zero running cost, within GitHub's published limits, and without redistributing real timing data by default.

## 4. Non-goals

- **Live timing.** Live OpenF1 data needs a paid tier and a different architecture (push, not precompute). The design leaves room for it but does not build it.
- **Telemetry analysis** (throttle, brake, speed traces), team radio, or broadcast video sync.
- **3D rendering**, elevation, or lateral (left/right of track) car placement. OpenF1 location data does not capture lateral position reliably.
- **User accounts**, comments, or any server-side state.
- **Commercial use.** The data licence is non-commercial (see §10).
- **Publishing real-session data by default.** The public site carries only synthetic demo races (ADR-0008). This matches the racesim project's stance on redistributing timing data.
- **Seasons before 2023.** OpenF1 coverage starts with the 2023 season. Other sources could be added later behind the same ports ([02 §6](02-architecture.md#6-solid-in-practice)).

## 5. Users and use cases

| Persona | Use case |
|---|---|
| **Visitor** | Opens the public site and plays a synthetic demo race to see what the app does. No setup. |
| **Fan** | "Show me how the undercut on lap 23 actually played out." Builds the race with the CLI (one command), opens the package in the app, scrubs to lap 20, plays at 4×, and watches the gap chart close. |
| **Spoiler-averse viewer** | Watches a race they missed without seeing the result. Spoiler-free mode hides the future part of the gap chart and the final order. |
| **Developer (project owner)** | Builds packages locally, reviews the build report, and tunes the analysis. Optionally runs hosted mode on a personal deployment. |
| **Contributor** | Clones the repo, runs `dotnet test`, and adds a feature (for example tyre-compound badges) without touching unrelated modules. Works against synthetic races without needing OpenF1 access. |

## 6. Scope

### 6.1 MVP (milestones M0–M7)

- Race sessions from 2023 onwards, via OpenF1.
- **Synthetic race generator.** Fictional circuits, drivers and teams generated from a seed and run through the real analysis pipeline. Used for demo races, UI development and end-to-end tests.
- **Offline CLI** that builds a replay package (`.rrz`) for a real or synthetic session.
- **Web client** with:
  - a demo catalogue;
  - opening local packages (file picker or drag-and-drop);
  - track map with all cars;
  - playback controls;
  - scrubber with event markers;
  - gap-to-leader chart with synced cursor;
  - timing tower;
  - driver focus;
  - deep links.
- **GitHub delivery:**
  - CI on Linux, Windows and macOS;
  - Pages deployment of the app plus demo races;
  - CLI releases with attestations;
  - repository settings as code;
  - an optional hosted-mode workflow for real bundles.

### 6.2 Later candidates (post-MVP backlog)

- Sprint and qualifying sessions. Qualifying needs a different gap model.
- Tyre compound and stint overlay, from the OpenF1 `stints` endpoint.
- Interval-to-car-ahead chart and battle detection.
- Head-to-head comparison mode: gap between two selected drivers.
- Time-chunked bundles for faster first paint.
- "Smooth mode" rendering, which draws cars on the centreline from race progress.
- Remembering recently opened local packages (browser storage).
- A `race-replay export-laps` command producing the racesim calibration CSV locally.
- Additional data sources behind the same ports.

## 7. Functional requirements

| ID | Requirement | Priority |
|---|---|---|
| FR-01 | The user can choose a replay from the catalogue (demo races, plus real races in hosted mode) or open a local replay package (FR-16). | Must |
| FR-02 | All cars are drawn on a track map in team colours, with driver acronyms, at the playback time. | Must |
| FR-03 | The user can play, pause, and choose a playback rate (0.25× to 64×). | Must |
| FR-04 | The user can scrub to any time using a timeline. Cars and charts update immediately. | Must |
| FR-05 | A gap-to-leader chart shows all drivers over the whole session, with a cursor at the playback time. | Must |
| FR-06 | Clicking or dragging on the gap chart seeks playback to that time. | Should |
| FR-07 | A timing tower shows running order, gap to leader and interval at the playback time. | Must |
| FR-08 | Pit stops, safety car / virtual safety car periods, red flags and the chequered flag are marked on the timeline. | Should |
| FR-09 | The user can focus a driver. The car and chart line are highlighted, and gap mode can switch to "gap to focused driver". | Should |
| FR-10 | Spoiler-free mode hides chart data and order information beyond the playback time. | Should |
| FR-11 | The URL encodes the replay, time and focused driver, for example `/replay/demo/1?t=3600&focus=7`. For local packages, time and focus apply after the file is opened. | Should |
| FR-12 | A CLI builds a replay package for a given session key and writes a build report. | Must |
| FR-13 | A CLI command validates a package against official interval and position data. | Should |
| FR-14 | Red-flag suspensions can be skipped automatically during playback. | Could |
| FR-15 | Keyboard shortcuts for play/pause, seek ±5 s / ±30 s, and rate up/down. | Could |
| FR-16 | The user can open a `.rrz` replay package from local disk via a file picker or drag-and-drop. The file is read in the browser and never uploaded. | Must |
| FR-17 | The CLI generates a synthetic race from a seed (fictional circuit, drivers, teams, incidents) and runs it through the same analysis pipeline as real data. | Must |

## 8. Non-functional requirements

| ID | Category | Requirement | How it is verified |
|---|---|---|---|
| NFR-01 | Performance | Steady 60 fps on a mid-range laptop at 20 cars. Per-frame .NET work < 2 ms. Zero managed allocations per frame. | Allocation unit test on the sampler; browser profiling in M1 (spike S0) and M7 |
| NFR-02 | Performance | Seeking to any time renders the new frame within one animation frame. | Sampler is O(cars). Manual check plus a test asserting no data-dependent loops |
| NFR-03 | Load time | First paint of a demo replay in < 5 s on a 50 Mbit/s connection, cold cache. Opening a local package renders in < 1 s after file selection. | DevTools measurement in M7 |
| NFR-04 | Size | Replay package for a ~2 h race ≤ 6 MB, with a target of ≤ 3 MB over the wire when hosted. | Build report emits sizes; CI warns over budget |
| NFR-05 | Accuracy | **Provisional:** derived gap-to-leader within 0.5 s (median) and 1.5 s (95th percentile) of OpenF1 intervals during green-flag running, and derived running order agreeing with OpenF1 positions for ≥ 95 % of compared samples. Confirmed or revised after spike S1. | `race-replay validate` for fixture sessions |
| NFR-06 | Maintainability | Project references follow the dependency rules in [02](02-architecture.md#4-dependency-rules). | Architecture tests fail the build on violation |
| NFR-07 | Testability | ≥ 85 % line coverage on Domain, Analysis, Bundle and Playback. All core tests deterministic (no network, no wall clock). | Coverage gate in CI |
| NFR-08 | Portability | Runs on current evergreen browsers (Chromium, Firefox, Safari) on desktop. Mobile layout is usable but not optimised for MVP. | Manual matrix in M7 |
| NFR-09 | Accessibility | All controls keyboard-operable, labelled for screen readers, with sufficient contrast. Respects `prefers-reduced-motion` for non-essential animation. | Checklist in M7 |
| NFR-10 | Operability | Zero running cost on GitHub Free for a public repository. No secrets required for build, deploy or release. Usage stays within GitHub's published limits (site size, bandwidth, cache, artifacts). | Budgets and guards in [07 §10](07-repository-ci-and-delivery.md#10-platform-limits-and-budgets); the site-size check fails the deploy |
| NFR-11 | Reproducibility | Building a package twice from the same cached raw data, with the same builder version, SDK, OS and CPU architecture, produces byte-identical output. CI (`ubuntu-latest`, x64) is the reference build. Builds elsewhere are valid but may differ in the last bits of floating-point values. | Determinism test in Bundle tests |
| NFR-12 | Compliance | The public site carries only synthetic data. Opening a real package shows the OpenF1 attribution and licence. The repository carries licence and trademark notices. | Review checklist; demo-only check in the deploy workflow |
| NFR-13 | Supply chain | Workflows use only GitHub-owned actions pinned to full commit SHAs, with read-only tokens by default. Release binaries carry signed build-provenance attestations and are published as immutable releases. | Repository policy and rulesets applied by `setup-github.sh`; `gh attestation verify` in the release checklist ([07 §7](07-repository-ci-and-delivery.md#7-supply-chain-and-security)) |

## 9. Success measures

- A full race replays at 60 fps with scrubbing that feels instant.
- Accuracy meets NFR-05, as finalised after S1, for at least three fixture races on different circuit types: permanent, street, and one with a red flag.
- Turning a new race into a package takes one CLI command and under 5 minutes of human time.
- The public demo plays with nothing to install.
- The CI pipeline is green on `main` on Linux, Windows and macOS, with coverage, docs and architecture gates enforced.
- A downloaded CLI passes `gh attestation verify` against the repository.
- A newcomer can clone, build and run tests with only the .NET 10 SDK installed, and see a synthetic race playing locally without OpenF1 access.

## 10. Constraints

| Constraint | Detail | Consequence |
|---|---|---|
| Data source | OpenF1 provides free historical data from the 2023 season onward, with no authentication. Location data is approximate and sampled at about 3.7 Hz. | MVP covers 2023+. The pipeline must cope with noise and gaps. |
| Rate limits | The free tier allows **3 requests/second and 30 requests/minute**. | The ingest client chains two rate limiters and caches every response on disk. |
| Licence | OpenF1 is licensed **CC BY-NC-SA 4.0** and intended for non-commercial use. OpenF1 does not claim ownership of the underlying F1 data. F1 marks are trademarks of Formula One Licensing B.V. | Real data stays on the user's machine by default (ADR-0008). Packages carry the licence and attribution. The project avoids F1 branding in its name and UI. |
| Platform | GitHub, public repository on GitHub Free (ADR-0010). Pages is a static host with no custom headers, no compression negotiation and a 1 GB site limit. | No server-side code at runtime. The client decodes Brotli in the browser, relies on fingerprinting for caching, and is single-threaded. CLI binaries ship through Releases, not GitHub Packages, whose NuGet registry needs a token even for public packages. |
| Platform | .NET 10 LTS (supported until November 2028). .NET 11 is due in November 2026 as a standard-term release. | Target `net10.0` and re-evaluate after .NET 11 GA (ADR-0001). |

## 11. Assumptions

- A1: The OpenF1 historical API remains available and its response shapes stay backward-compatible. Deprecations are announced per field (for example `pit_duration` → `lane_duration` at the end of 2026).
- A2: Location coordinates are in a consistent per-circuit frame across sessions at the same event. Units are believed to be decimetres; this is verified in spike S1.
- A3: A single maintainer works part-time. Milestones are sized in "focused days", not calendar dates, and include an explicit contingency (§13).
- A4: Viewers use desktop browsers with WebAssembly enabled.
- A5: Users who want real races can install the .NET 10 SDK, or a self-contained CLI build from a GitHub Release, and run one command.
- A6: The repository is public, under a personal GitHub account on GitHub Free. `OWNER` in these documents stands for that account. The shared template repository `OWNER/dotnet-lab-template` exists, or Race Replay creates it at M0 (racesim ADR-0009).

## 12. Risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Location data has gaps, jitter or sentinel values that break projection | High | Medium | Cleaning with outlier rejection. Validity bitmap instead of interpolating across gaps. Synthetic-fault unit tests. |
| R2 | Pit lane and off-track excursions confuse the centreline projection | Medium | Medium | Windowed projection with continuity hint and re-acquisition rule. Lateral-error threshold. Pit windows from the `pit` endpoint. Lap anchors from the `laps` endpoint. |
| R3 | A single wrong projection inflates gaps for the whole field (the envelope is a ratchet) | Medium | High | Plausibility gate on each car's progress, envelope speed limit, forward-snap regression test ([03 §8.1](03-domain-and-algorithms.md#81-gap-to-the-virtual-leader)) |
| R4 | Derived gaps or order disagree with official data | Medium | High | Gap and running-order validators. Provisional thresholds confirmed in S1. Fixture races. Documented known differences (pit lane, SC restarts). |
| R5 | JS interop or WebAssembly performance falls short of 60 fps | Low | High | Measured in M1 (spike S0) on a synthetic race before any real-data work. Three small interop calls per frame. Zero-copy `Span<double>` marshalling. AOT compilation as a fallback lever. |
| R6 | OpenF1 changes fields or rate limits | Medium | Medium | DTOs isolated in the adapter. Contract tests on recorded fixtures. Configurable limiter. |
| R7 | Licence, data-rights or trademark concerns | Low | High | Real data not redistributed by default (ADR-0008). Synthetic demo races with fictional names. Attribution in packages and UI. Hosted mode is opt-in and documented as the deployer's responsibility. No official logos. |
| R8 | Users find the local-build step a barrier | Medium | Medium | One command. Self-contained CLI builds attached to releases. Clear in-app "how to open a real race" guide. Demo races for first contact. |
| R9 | Scope creep (telemetry, live mode) | Medium | Medium | Explicit non-goals. Post-MVP backlog. ADRs for any scope change. Written cut line (§13). |
| R10 | Estimates prove optimistic (real-data tuning is unpredictable) | High | Medium | 30 % contingency. Cut line. Vertical slice first, so a working player exists early. |
| R11 | Tooling churn (xUnit 4.x, MTP, .NET 11) | Medium | Low | Central Package Management plus Dependabot (NuGet, .NET SDK, Actions). `global.json` pins the SDK band. Shared template keeps racesim and Race Replay aligned. |
| R12 | GitHub platform limits or behaviour change (Pages size or bandwidth, cache eviction, header handling) | Low | Medium | Budgets and guards per limit ([07 §10](07-repository-ci-and-delivery.md#10-platform-limits-and-budgets)). The catalogue indirection allows moving bundles to other static storage. The client never depends on server behaviour. |
| R13 | Supply-chain compromise via a workflow action or dependency | Low | High | GitHub-owned actions only, pinned by SHA and enforced by policy. Least-privilege jobs. No secrets. Lock files. CodeQL, Dependabot and secret scanning. Attested, immutable releases. |

## 13. Delivery outline

| Milestone | Outcome | Size (focused days) |
|---|---|---|
| M0 | Repository created from the shared template, settings applied as code, CI green on 3 OS, empty app on Pages | 1 |
| M1 | **Walking skeleton:** a synthetic race playing in the browser on Pages, with clock, scrubber and track map (includes spike S0) | 5 |
| S1, S2 | Real-data reality check; packaging and hosting check | 1.5 |
| M2 | Analysis core: centreline, projection, progress, gaps, order, proven on synthetic data | 4 |
| M3 | OpenF1 ingest, Application layer and CLI produce the first real package | 4 |
| M4 | Bundle format and packages: schema, golden files, `.rrz` packages, local file open, `validate` | 2 |
| M5 | Gap chart, timing tower, events, focus, spoiler-free mode, deep links | 4 |
| M6 | Delivery: Pages with demo races, optional hosted mode, docs and notices | 2 |
| M7 | Hardening: validation, performance, accessibility, v1.0 | 3 |
| **Subtotal** | | **26.5** |
| Contingency (30 %) | Real-data tuning, browser quirks, tooling surprises | **≈ 8** |
| **Plan for** | | **≈ 35** |

**Cut line.** If the plan runs late, scope drops in this order. Must requirements are never cut.

1. FR-15 keyboard shortcuts
2. FR-14 red-flag skip
3. FR-10 spoiler-free mode
4. FR-06 chart seek
5. Hosted mode (M6)
6. FR-08 markers other than pit stops

Details, exit criteria and the issue backlog are in [08 — Implementation plan](08-implementation-plan.md).

## 14. Open questions

| # | Question | Resolved by |
|---|---|---|
| Q1 | Which instant does `pit.date` represent (pit entry, box, or exit)? | Spike S1 |
| Q2 | How should race start be detected reliably? Options: race control messages, lap 1 `date_start`, or a motion onset heuristic. | Spike S1 |
| Q3 | Do `InputFile` and `ZipArchive` behave well in browser-wasm for 5–10 MB packages? Is compressing hosted bundles worth it? Does in-browser Brotli decoding of the framework work with .NET 10 fingerprinted assets on Pages? | Spike S2 |
| Q4 | Should AOT compilation be enabled for the WebAssembly client (bigger download vs. faster frames)? | M7 measurement |
| Q5 | Should the client compute gaps itself (smaller package) instead of shipping precomputed gaps? | Revisit after M5 with real sizes |
| Q6 | Are the provisional NFR-05 thresholds realistic? | Spike S1 |

## 15. Glossary

| Term | Meaning |
|---|---|
| **Session** | A distinct on-track period (practice, qualifying, sprint, race), identified by an OpenF1 `session_key`. |
| **Centreline** | A closed polyline approximating the middle of the track, parameterised by arc length *s* ∈ [0, L). |
| **Lap length L** | Total arc length of the centreline. |
| **Race progress *d*** | Laps completed × L + *s*. Monotonic for a car that is racing. Used for ordering and gaps. |
| **Virtual leader** | The envelope D\*(t) = max over cars of d(t), speed-limited. It handles leader changes seamlessly. |
| **Replay timeline** | The fixed-rate time grid (for example 4 Hz) that all channels are resampled onto. |
| **Replay bundle** | The manifest plus binary channel files for one session. |
| **Replay package (`.rrz`)** | A bundle in a single ZIP file, which is what users open in the app. |
| **Synthetic race** | A generated race with a fictional circuit, drivers and teams, run through the real pipeline. |
| **Hosted mode** | Opt-in deployment that publishes real-race bundles on the site from the `data` branch (ADR-0007, ADR-0008). |
| **Shared template** | The GitHub template repository `OWNER/dotnet-lab-template` that racesim and Race Replay are created from, holding their common build, test and CI files. |
| **Immutable release** | A GitHub release whose assets and tag can't change after publication. GitHub attaches a release attestation. |
| **Frame** | The interpolated state of all cars at one playback time, written into a reusable buffer. |
| **Spike** | A time-boxed experiment that de-risks a design question. |
