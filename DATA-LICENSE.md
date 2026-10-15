# Data licence and policy

Race Replay's **source code** is licensed under the [MIT License](LICENSE). This file covers **data**.

## Real-race data (OpenF1)

Real-race replay packages that users build with the CLI, bundles published in hosted mode, and the trimmed
recorded fixtures used by the test suite are derived from data provided by [OpenF1](https://openf1.org).
That data is licensed under
[Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)](https://creativecommons.org/licenses/by-nc-sa/4.0/),
and so is anything derived from it.

Attribution: **Data: OpenF1 (openf1.org)**

Every real-race package carries this attribution in its manifest and in a `DATA-NOTICE.txt` entry, and the
web app shows it whenever a real replay is open.

## What this repository publishes

By default, no real-race data is committed or published ([ADR-0008](docs/adr/0008-bring-your-own-bundles.md)):

- Users build real races **locally** into a `.rrz` package and open it in the app. The package is read in
  the browser and never uploaded.
- The public site shows **synthetic demo races** only. They are generated from a seed by
  `RaceReplay.Synthetic` and contain no third-party data.
- The deploy workflow rejects any bundle whose provenance is not synthetic unless hosted mode is enabled.

Hosted mode, where a deployment publishes real-race bundles from an orphan `data` branch, is off in this
repository. Anyone who enables it in their own deployment is responsible for complying with the licence above.
