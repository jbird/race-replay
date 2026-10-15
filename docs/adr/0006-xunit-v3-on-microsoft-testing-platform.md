# ADR-0006: xUnit v3 on Microsoft Testing Platform

- **Status:** Accepted
- **Date:** 2026-10-12
- **Deciders:** Project maintainer

## Context

Unit tests must use xUnit (project requirement). As of October 2026:

- **xUnit.net v3 4.0** shipped in August 2026, with 4.0.2 the latest in October. It adds Native AOT test support and full test parallelisation.
- The `xunit.v3` package now installs **Microsoft Testing Platform (MTP) v2** support by default.
- From the **.NET 10 SDK**, `dotnet test` runs MTP natively when `global.json` declares `"test": { "runner": "Microsoft.Testing.Platform" }`. MTP v2 projects are rejected without it.
- xUnit offers packages to turn MTP off, so VSTest can still be used on .NET 10+.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **xUnit v3 (4.x) + MTP v2, `global.json` runner** | Current default path; test projects are self-contained executables; native `dotnet test`; works in VS 2026 / VS Code C# Dev Kit; built-in TRX and code coverage extensions | Newer; some CLI flags differ from VSTest (`--report-xunit-trx`, `--filter-class`) |
| xUnit v3 with MTP off (VSTest) | Familiar VSTest flags and tooling | Swims against the default; extra package choices; VSTest is the legacy path |
| xUnit v2 | Most familiar | Legacy; misses v3 features; not recommended for new projects |

## Decision

Use **`xunit.v3` 4.x on Microsoft Testing Platform v2**:

- `global.json` sets the runner.
- Shared test settings live in `tests/Directory.Build.props`.
- Coverage comes from `Microsoft.Testing.Extensions.CodeCoverage` (Cobertura).
- TRX comes from xUnit's `--report-xunit-trx`.
- Assertions use xUnit's built-in `Assert`. FluentAssertions 8+ is avoided because it is commercially licensed.
- Property tests use CsCheck, which is framework-agnostic.

## Consequences

- **Positive:**
  - Modern, supported path.
  - Fast, isolated test executables.
  - Optional full parallelism for pure test projects.
- **Negative:**
  - Contributors with older SDKs or IDEs need the .NET 10 SDK and Visual Studio 2026, Rider, or VS Code with a current C# Dev Kit.
  - CI flags follow MTP conventions.
- **Follow-up:** check bUnit compatibility with xUnit 4 when component tests are added in M5.

## Revisit when

- Microsoft Testing Platform v3 introduces breaking changes. xUnit already sets `IsTestingPlatformApplication` in anticipation.
- Or xUnit 5 ships.
