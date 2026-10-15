# ADR-0008: Bring your own bundles by default; synthetic demo races on the public site

- **Status:** Accepted. Amends ADR-0007.
- **Date:** 2026-10-12
- **Deciders:** Project maintainer

## Context

The original design (ADR-0007) published real-race bundles, derived from OpenF1 position data, on a public GitHub Pages site. Several facts argue against doing that by default:

- OpenF1 data is licensed CC BY-NC-SA 4.0 and intended for non-commercial and personal use. OpenF1 itself states that it does not claim ownership of the underlying Formula 1 data.
- A public site serving derived position data for every race is redistribution at scale. That is the project's largest legal and reputational exposure (risk R7).
- The sibling **racesim** project in the same workspace already treats "redistributing official timing data" as a non-goal and ships only synthetic data. The two projects should follow one policy.
- The analysis pipeline benefits from a synthetic data source anyway: for UI development before ingest exists, for deterministic end-to-end tests, and for regression testing of the algorithms.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| A. Publish real bundles publicly (ADR-0007 as written) | Zero friction for viewers; shareable links to any race | Redistribution of derived data by the project; takedown risk; inconsistent with racesim |
| **B. Bring your own bundles + synthetic demo + opt-in hosted mode** | No real data published by default; demo needs no setup; same app works for real races opened locally; consistent with racesim | Viewers of real races must run the CLI (or receive a package); deep links to real races only work after opening the file |
| C. Private repository with private Pages | Real data visible only to the owner | Pages for private repositories needs a paid plan; the site itself is still public unless the plan supports access control; doesn't serve other fans |
| D. Build bundles in the browser from OpenF1 | Nothing redistributed | Rejected in ADR-0002: slow, rate-limited, and every viewer hits the API |

## Decision

Adopt **option B**.

1. **Public site = app + synthetic demo races.**
   - Demo races are generated in CI by `race-replay build --source synthetic --seed N`, through the real analysis pipeline.
   - They use fictional circuits, drivers and teams. They contain no OpenF1 data, so they are free of licence obligations.
2. **Real races are built locally.**
   - `race-replay build --session <key>` writes a single **replay package** (`.rrz`). This is a ZIP containing `manifest.json`, the `.rrb` channel files and a `DATA-NOTICE.txt` with attribution and licence.
   - The user opens it in the app with a file picker or drag-and-drop. It is read entirely in the browser and never uploaded.
3. **Hosted mode is opt-in.**
   - Setting the repository variable `HOSTED_BUNDLES=true` enables the data-branch flow from ADR-0007, so real bundles are deployed with the site.
   - This is intended for personal or private deployments. The README states that the deployer then takes responsibility for compliance.
   - It is off in the main repository.
4. **One source of truth for packages.**
   - The CLI writes `.rrz` packages by default, or `--layout directory` for the hosted layout.
   - The app's `IReplaySource` abstraction has two implementations:
     - `HttpReplaySource` for demo and hosted bundles;
     - `LocalPackageReplaySource` for opened files.

## Consequences

- **Positive:**
  - The project does not redistribute real data.
  - The public demo works without OpenF1, deterministically and offline.
  - Contributors can develop the whole UI against synthetic races.
  - End-to-end tests have stable inputs.
  - Policy is consistent with racesim.
- **Negative:**
  - Watching a real race needs one CLI command. Mitigations: self-contained CLI builds attached to GitHub Releases, and an in-app guide.
  - Sharing a real race means sharing a file. The UI notes that the file carries CC BY-NC-SA obligations.
  - Deep links to local packages restore time and focus only after the file is opened.
- **Follow-ups:**
  - Spike S2 confirms that `InputFile` and `ZipArchive` work in browser-wasm for 5–10 MB packages.
  - The deploy workflow fails if a non-synthetic manifest (`provenance.source` other than `synthetic`) reaches the public artifact while hosted mode is off.

## Revisit when

- OpenF1 or the rights holders publish clearer guidance permitting (or forbidding) redistribution of derived position data.
- Or user demand makes the local-build step a real barrier. A desktop wrapper or a one-click local build helper could then be considered.
