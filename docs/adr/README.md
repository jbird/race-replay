# Architecture decision records

Each record captures one significant decision: its context, the options considered, the decision itself, and its consequences.

- Records are immutable once accepted. A changed decision gets a new ADR that supersedes the old one.
- Spike outcomes that refine an accepted decision are added as a dated **addendum**.

| ADR | Title | Status |
|---|---|---|
| [0001](0001-target-dotnet-10-lts.md) | Target .NET 10 LTS and C# 14 | Accepted |
| [0002](0002-precompute-bundles-static-hosting.md) | Precompute replay bundles offline; serve statically | Accepted |
| [0003](0003-blazor-webassembly-with-thin-js.md) | Blazor WebAssembly client with thin JavaScript render shims | Accepted (validate in S0) |
| [0004](0004-custom-binary-bundle-format.md) | Custom structure-of-arrays binary bundle format | Accepted (compression via S2) |
| [0005](0005-derive-gaps-from-race-progress.md) | Derive gaps from race progress using a virtual leader | Accepted |
| [0006](0006-xunit-v3-on-microsoft-testing-platform.md) | xUnit v3 on Microsoft Testing Platform | Accepted |
| [0007](0007-bundles-on-orphan-data-branch.md) | Store published bundles on an orphan `data` branch, deploy via GitHub Pages | Accepted; amended by 0008 (hosted mode only) |
| [0008](0008-bring-your-own-bundles.md) | Bring your own bundles by default; synthetic demo races on the public site | Accepted |
| [0009](0009-interfaces-at-seams-ports-in-application.md) | Interfaces only at real seams; ports owned by the Application layer | Accepted |
| [0010](0010-github-as-the-delivery-platform.md) | GitHub as the delivery platform: public repo, Pages, Releases, settings as code, shared template | Accepted |

## Template

```markdown
# ADR-NNNN: Title

- **Status:** Proposed | Accepted | Superseded by ADR-XXXX
- **Date:** YYYY-MM-DD
- **Deciders:** …

## Context
What forces are at play? What constraints and requirements (FR/NFR IDs) apply?

## Options considered
| Option | Pros | Cons |
|---|---|---|

## Decision
What we will do, stated plainly.

## Consequences
Positive, negative, and follow-up actions.

## Revisit when
Conditions that should trigger reconsideration.
```
