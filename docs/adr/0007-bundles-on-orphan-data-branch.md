# ADR-0007: Store published bundles on an orphan `data` branch; deploy via GitHub Pages

- **Status:** Accepted, amended by [ADR-0008](0008-bring-your-own-bundles.md). This mechanism now applies only to the opt-in **hosted mode**. By default the public site serves synthetic demo races only, and real races are opened locally.
- **Date:** 2026-10-12
- **Deciders:** Project maintainer

## Context

Bundles (about 3–6 MB per race) must be:

- served to browsers as static files alongside the app;
- versioned and reproducible;
- kept out of the code history.

Everything is hosted on GitHub at zero cost (NFR-10). Browsers fetch bundles directly, so the storage must be served with correct content and without cross-origin problems.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **Orphan `data` branch, copied into the Pages artifact at deploy** | Versioned; separate from code history; same origin as the app (no CORS); simple workflow; squashable if large | Repository size grows with seasons; Pages site-size limits apply |
| Commit bundles to `main` | Simplest | Bloats code history and clones; mixes data with code reviews |
| Git LFS | Keeps the repo small | GitHub Pages serves LFS pointer files, not content; bandwidth quotas |
| GitHub Release assets | Outside Git history | Download redirects to another origin; browser `fetch` hits cross-origin and redirect issues; awkward catalogue |
| Rebuild all bundles at every deploy from the cached raw data | No stored bundles | Slow deploys; depends on cache retention; heavy OpenF1 usage if the cache is evicted |
| External object storage + CDN | Scales | Running cost and credentials; not GitHub-only |

## Decision

Keep published bundles and `catalog.json` on an **orphan `data` branch**. Only the bundle workflow pushes to it. The deploy workflow checks it out and copies `bundles/` into the Pages artifact next to the app. The client discovers bundles only through `catalog.json`.

## Consequences

- **Positive:**
  - Same-origin static files.
  - Full history of every published bundle.
  - Easy rollback by reverting a commit on `data`.
  - Code history stays clean.
- **Negative:**
  - Repository and site size grow by roughly 100–150 MB per season. GitHub Pages allows 1 GB per published site and recommends repositories under 1 GB. `assemble-site.sh` warns at 500 MB and fails the deploy at 900 MB ([07 §10](../07-repository-ci-and-delivery.md#10-platform-limits-and-budgets)). Old seasons can be pruned or the branch squashed.
  - Contributors who clone with `--no-single-branch` download the data too. A normal clone of `main` does not.
- **Follow-up:** the catalogue indirection keeps a future move to object storage a configuration change.

## Revisit when

- Site size approaches GitHub Pages limits.
- Or the project needs bundle hosting beyond what Pages provides.
