# ADR-0001: Target .NET 10 LTS and C# 14

- **Status:** Accepted
- **Date:** 2026-10-12
- **Deciders:** Project maintainer

## Context

The project starts from an empty repository and should use current C# and .NET capabilities. As of October 2026:

- **.NET 10** shipped in November 2025 as a **Long-Term Support** release, supported until November 2028. It ships with **C# 14** (extension members, the `field` keyword, implicit span conversions, null-conditional assignment), native Microsoft Testing Platform support in `dotnet test`, `.slnx` by default, and file-based apps.
- **.NET 11** is at Release Candidate (RC1 shipped September 2026 with a go-live licence). General availability is scheduled for .NET Conf in November 2026. It is a **standard-term** release and brings **C# 15** with union types.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **.NET 10 LTS** | Stable, long support window; mature tooling and templates; all chosen packages (xUnit 4, System.CommandLine 2.0, Extensions 10.x) support it | Misses C# 15 unions |
| .NET 11 RC now, GA in November | Newest features (unions fit `RaceEvent`) | RC churn during the build; shorter support; some packages may lag |
| .NET 8 LTS | Widest compatibility | Out of support in November 2026; misses C# 13/14, MTP-native `dotnet test`, slnx default |

## Decision

Target **`net10.0`** with the default **C# 14** language version for every project. Pin the SDK band in `global.json` with `rollForward: latestFeature`.

## Consequences

- **Positive:**
  - Stable foundation for the whole MVP.
  - Three years of support.
  - Every feature in [02 §10](../02-architecture.md#10-net-platform-features-used) is available.
- **Negative:**
  - `RaceEvent` uses an abstract record hierarchy instead of union types.
  - A future upgrade is a deliberate task.
- **Follow-up:** a post-MVP backlog item evaluates .NET 11 once it is GA and the dependencies support it.

## Revisit when

- .NET 11 is GA and the project wants C# 15 unions or a specific .NET 11 runtime improvement (for example Blazor WebAssembly performance).
- Or when .NET 12 LTS ships (November 2027).
