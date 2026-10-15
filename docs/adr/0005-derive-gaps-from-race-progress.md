# ADR-0005: Derive gaps from race progress using a virtual leader

- **Status:** Accepted
- **Date:** 2026-10-12
- **Deciders:** Project maintainer

## Context

The gap chart must be continuous and synchronised with playback (FR-05, FR-07). OpenF1's `intervals` endpoint gives `gap_to_leader` and `interval` about every 4 seconds, in races only. Values are mixed-type: a number, `"+N LAP(S)"`, or `null`. Charting those directly produces a stepped, lagging line. It also gives nothing between updates, which makes scrubbing feel imprecise.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **Derive from race progress: `gap_c(t) = t − T*(d_c(t))` with a virtual-leader envelope D\*** | Continuous at any resolution; handles leader changes seamlessly; same maths serves interval, laps-down and gap-to-focus; works for any session with position data | Needs a centreline and projection; differs from official timing in pit lane / SC edge cases |
| Use OpenF1 intervals, interpolated | Official numbers | Stepped/lagging at 4 s; mixed types; races only; interpolation invents values |
| Gap to the *current* leader car's trajectory | Simple concept | Discontinuities when the leader changes (pit cycles, overtakes) |

## Decision

Compute gaps from monotonic race progress against the **virtual leader** ([03 §8.1](../03-domain-and-algorithms.md#81-gap-to-the-virtual-leader)). Use OpenF1 intervals only to **validate** accuracy (NFR-05: median ≤ 0.5 s, P95 ≤ 1.5 s in green-flag running). These thresholds are provisional until spike S1 measures real races. Running order is validated separately against the OpenF1 `position` endpoint.

## Consequences

- **Positive:**
  - Smooth chart.
  - A single, testable definition with analytic synthetic tests.
  - No dependency on interval data being present.
- **Negative:**
  - Small, explainable differences from official timing in the pit lane and under safety car. These are documented and excluded from validation stats.
  - The envelope is a ratchet: a forward error in any car's progress inflates every other car's gap. This is defended in three layers: a projection re-acquisition rule, a per-car plausibility gate, and a speed limit on the envelope ([03 §6–8](../03-domain-and-algorithms.md#63-unwrapping-and-lap-anchoring)).
- **Follow-up:** the `GapAccuracyValidator` runs in every build. Fixture races are checked in M7.

## Revisit when

- Validation shows systematic errors beyond NFR-05 that projection tuning can't fix.
