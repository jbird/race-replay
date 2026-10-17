# ADR-0010: GitHub as the delivery platform

- **Status:** Accepted
- **Date:** 2026-10-12
- **Deciders:** Project maintainer
- **Related:** ADR-0007, ADR-0008; racesim [ADR-0009](https://github.com/OWNER/racesim/blob/main/docs/adr/0009-shared-foundations-with-race-replay.md) (shared template). Replace `OWNER` with the GitHub account that owns both repositories.

## Context

The project is hosted on GitHub. That is a requirement, not an open question. Several GitHub facts shape the design, so they are recorded here rather than scattered across documents. All were checked in October 2026.

- **Plan and visibility.**
  - GitHub Pages works for public repositories on GitHub Free. Private repositories need GitHub Pro, Team or Enterprise.
  - Artifact attestations for public repositories are available on every plan. Private repositories need Enterprise Cloud.
  - Code scanning, secret scanning and standard GitHub-hosted runner minutes are free for public repositories.
- **Pages is a static host with fixed behaviour.**
  - No custom HTTP headers, and a short, fixed cache lifetime (reported as `max-age=600`).
  - No server-side compression negotiation for pre-compressed files. Microsoft's guidance for Blazor WebAssembly on GitHub Pages is to fetch the `.br` files and decode them in the browser.
  - Published sites are limited to 1 GB, with a soft bandwidth limit of 100 GB a month and a 10-minute deployment timeout.
- **Actions.**
  - The cache is 10 GB per repository at no cost, and entries unused for 7 days are evicted.
  - Standard runners for Linux, Windows and macOS are available to public repositories.
  - Policies can restrict workflows to GitHub-owned actions and require actions to be pinned to full commit SHAs.
- **Releases.**
  - *Immutable releases* (generally available since October 2025) lock a published release's assets and tag, and generate a release attestation.
  - `actions/attest` adds signed build-provenance attestations that users can check with `gh attestation verify`.
- **Packages.** GitHub Packages' NuGet registry needs an access token even to *install* public packages. That makes it unsuitable for anything end users or contributors restore anonymously.
- **Sibling project.** racesim, in the same workspace, adopted a shared GitHub *template repository* for build, test and CI files (racesim ADR-0009). It listed follow-ups for Race Replay: newer action majors, the docs gate, script conventions and Dependabot settings.

## Options considered

| Decision area | Options | Choice |
|---|---|---|
| Repository visibility | Public on GitHub Free · Private on GitHub Pro | **Public, GitHub Free.** Pages, attestations, CodeQL and runner minutes are all free. ADR-0008 means nothing sensitive is committed. |
| Site hosting | GitHub Pages (Actions source) · Pages from a branch · another static host | **Pages, deployed from an Actions workflow.** No `gh-pages` branch, no Jekyll, and no soft limit of 10 builds an hour. |
| CLI distribution | GitHub Releases · GitHub Packages (NuGet) · nuget.org `dotnet tool` | **GitHub Releases**: self-contained binaries, checksums and attestations, as immutable releases. A nuget.org tool package is an optional later addition. GitHub Packages is ruled out by its token requirement. |
| Shared library hosting (OpenF1 client) | nuget.org · GitHub Packages · copy · monorepo | **Deferred to racesim ADR-0009 Part 2** (proposed: nuget.org). Race Replay keeps the client in its own project so either choice is cheap. |
| Repository configuration | Click through settings · settings as code | **Settings as code:** a re-runnable `scripts/setup-github.sh` plus committed ruleset JSON. A short checklist covers what the API can't set. |
| Build conventions | Own files · shared template | **Adopt the shared template** (racesim ADR-0009). Additions Race Replay needs are proposed to the template first. |

## Decision

1. **Public repository on GitHub Free.** Everything the project needs is free there, and the data policy (ADR-0008) keeps real data out of Git.
2. **GitHub Pages via Actions.** The app and synthetic demo races are deployed from `deploy-pages.yml`. The client is designed around Pages' fixed behaviour ([05 §12](../05-web-client.md#12-github-pages-specifics)):
   - Brotli files are decoded in the browser.
   - Assets are fingerprinted.
   - Runtime settings come from the catalogue, so published app files are never edited.
   - The app is single-threaded, because Pages can't send the headers threads need.
3. **GitHub Releases for the CLI.** Each release ships self-contained builds per platform, made on native runners. A `SHA256SUMS.txt` and signed build-provenance attestations accompany them, published as an **immutable release** (create a draft, upload, then publish).
4. **Supply-chain baseline from day one:**
   - only GitHub-owned actions, pinned to full commit SHAs and enforced by repository policy;
   - read-only `GITHUB_TOKEN` by default, elevated per job;
   - CodeQL default setup, secret scanning with push protection, Dependabot (NuGet, .NET SDK, Actions), and private vulnerability reporting.
5. **Settings as code.** `scripts/setup-github.sh` applies merge settings, Actions policy, Pages source, security features, rulesets, labels and milestones. It is safe to re-run.
6. **Shared template.** Race Replay is created from `<owner>/dotnet-lab-template`. If it reaches M0 before racesim, it creates the template. The racesim ADR-0009 follow-ups are resolved in favour of the template.
7. **Budgets for platform limits.** Every relevant limit has a budget and a guard in the workflows ([07 §10](../07-repository-ci-and-delivery.md#10-platform-limits-and-budgets)).

## Consequences

- **Positive:**
  - Zero running cost.
  - One place for code, CI, site, downloads, issues and discussions.
  - Users can verify the binaries they download.
  - Settings are reviewable and reproducible.
  - Conventions match racesim.
- **Negative:**
  - Pages' fixed headers mean no custom caching, no response-header CSP and no WebAssembly multithreading.
  - Brotli decoding adds a small client-side step and one vendored script.
  - SHA-pinned actions are less readable. Version comments and Dependabot mitigate that.
  - Immutable releases can't be patched: a bad release is fixed by a new version.
- **Follow-ups:**
  - Spikes S0 and S2 confirm Brotli decoding with .NET 10 fingerprinted assets, and measure what Pages already compresses. **Done:** decoding works with fingerprinted names (S0). Pages gzips everything it serves, much like zlib at level 5, and never sends Brotli (S0, S2). RR-070 (#37) decides whether Brotli decoding pays off.
  - Proposals to the shared template (Dependabot `dotnet-sdk`, rulesets, dev container, Brotli decoding for Pages) go through its PR process.

## Revisit when

- GitHub Pages adds custom headers, compression negotiation, or changes its limits.
- The project needs features Pages can't provide, such as server-side code or private access.
- racesim ADR-0009 Part 2 is decided, which settles how the OpenF1 client is published.
