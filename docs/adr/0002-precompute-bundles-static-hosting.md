# ADR-0002: Precompute replay bundles offline; serve statically

- **Status:** Accepted
- **Date:** 2026-10-12
- **Deciders:** Project maintainer

## Context

Raw OpenF1 position data:

- is irregular (about 3.7 Hz per car, unaligned) and noisy;
- is large (about 70–80 MB of JSON per race);
- sits behind a rate limit (3 req/s, 30 req/min on the free tier).

Turning it into something replayable needs a centreline, projection, unwrapping, resampling and gap derivation. The viewer needs instant seeking at 60 fps (NFR-01, NFR-02). The project must cost nothing to run (NFR-10), and is hosted on GitHub.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **Offline precompute → static bundles** | Heavy work done once; tiny, fast downloads; no server; deterministic and testable; no API load from viewers | Bundles must be built per race (automated by workflow); not live |
| Browser fetches OpenF1 directly and processes client-side | No build step | Slow first load (tens of MB, rate-limited); every viewer hits the API; analysis code shipped to the browser; poor reliability |
| Server API (ASP.NET Core) that processes on demand and caches | Flexible; could add live mode later | Needs hosting, running cost, ops; contradicts NFR-10 |

## Decision

Split the system into **build time** and **run time**:

- **Build time:** a .NET CLI (also run by GitHub Actions) fetches, analyses and writes **replay bundles**.
- **Run time:** a static web app loads bundles from GitHub Pages, or from a local package the user opens (ADR-0008). No server code runs at view time.

## Consequences

- **Positive:**
  - Viewer performance depends only on bundle size and client code.
  - Algorithms run in a controlled, testable environment.
  - The OpenF1 API sees roughly 30 requests per race build, never per page view.
  - There is no infrastructure to operate.
- **Negative:**
  - A replay exists only after someone builds it. Since ADR-0008, that is the user running one CLI command locally, or the bundle workflow in hosted mode.
  - Bundle format becomes a versioned contract ([04 §10](../04-data-pipeline-and-bundle-format.md#10-versioning-and-compatibility)).
  - Live timing is out of scope.
- **Follow-up:** the catalogue indirection allows moving bundles to other static storage without client changes.

## Revisit when

- Live replay or near-live replay becomes a goal. That needs a push architecture and a paid OpenF1 tier.
- Or when bundle volume outgrows GitHub Pages limits.
