# ADR-0009: Interfaces only at real seams; ports owned by the Application layer

- **Status:** Accepted
- **Date:** 2026-10-12
- **Deciders:** Project maintainer

## Context

The first draft of the design gave every analysis step its own interface (`ILocationCleaner`, `ITrackProjector`, `IGapCalculator`, …), each with exactly one implementation. It also had four fine-grained data ports (`ISessionMetadataSource`, `ILocationSource`, `ITimingSource`, `ISessionCatalogSource`), declared in Domain.

On review:

- The analysis steps are pure, deterministic functions. They are tested directly with synthetic data, so the interfaces provide no test seam and no substitution. They add ceremony, extra DI registrations and indirection when reading code.
- With the synthetic data source (ADR-0008), loading a session is a single operation with two implementations. That is a real seam, but at the level of "load a whole session", not four fine-grained calls.
- The sibling **racesim** project already states the rule "no interface where there is one implementation and no test seam", and declares ports in its Application layer. Two projects by the same author should share conventions.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Keep an interface per class; ports in Domain | Maximum substitutability on paper | Ceremony without benefit; ports in Domain blur the layer's purpose; inconsistent with racesim |
| **Interfaces only at seams; ports in Application** | Interfaces mark real boundaries and extension points; less code; Domain stays pure model; consistent with racesim | Adding a substitute for a concrete class later needs a small refactor (extract interface) |
| No interfaces at all | Minimal code | Loses ports, plug-in points and test seams that are genuinely needed |

## Decision

1. **An interface is introduced only when at least one of these holds:**
   - more than one implementation exists or is planned (`IRawSessionProvider`, `IReplaySource`, `IEventExtractor`, `IReplayValidator`, `IPlaybackClock` with its decorator);
   - it is an external boundary (file system, HTTP, JS interop renderers);
   - a test genuinely needs a substitute (`IReplayPackageStore`, `IBuildReportWriter` in CLI handler tests).
2. **Pure algorithm classes are `sealed` and concrete.** This includes the analysis steps, `SessionAnalyzer`, `FrameSampler`, `RunningOrderCalculator` and `ReplayStore`. They are configured through the options pattern and tested directly.
3. **Ports live in `RaceReplay.Application`** (formerly `Pipeline`), which owns the use cases that consume them. Adapters (`Ingest.OpenF1`, `Synthetic`, the CLI's file-system store) reference Application. Domain contains only the model.
4. **Data loading is one port per source:** `IRawSessionProvider.LoadAsync(SessionKey)`, registered as keyed services. `ISessionCatalogSource` stays separate because listing sessions is a different job from loading one.

## Consequences

- **Positive:**
  - Fewer types and registrations.
  - Interfaces now *signal* something: a boundary or an extension point.
  - Domain is a pure model.
  - One shared contract test (`RawSessionProviderContract`) covers both data sources.
  - Conventions match racesim.
- **Negative:**
  - Some ISP examples from the first draft disappear. ISP remains visible where it matters: the clock's reader/control split, catalogue vs loading, and narrow renderer interfaces.
  - Extracting an interface later is a small, mechanical refactor when a second implementation appears.
- **Follow-up:** architecture tests are updated for the new project graph ([02 §4.2](../02-architecture.md#42-allowed-references)).

## Revisit when

- A concrete class gains a second implementation, or a test needs to substitute it. Extract an interface at that point, not before.
