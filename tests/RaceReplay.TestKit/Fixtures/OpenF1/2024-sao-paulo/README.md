# OpenF1 fixture: 2024 São Paulo Grand Prix, session 9636

Trimmed recorded responses from the [OpenF1](https://openf1.org) API, used by the adapter tests (docs/06 §9).

- **Licence:** derived from OpenF1 data, which is licensed under
  [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/). So are these files.
  Attribution: **Data: OpenF1 (openf1.org)**. See [DATA-LICENSE](../../../../../DATA-LICENSE.md).
- **Session:** 9636 (Interlagos).
- **Kept:** drivers 43 and 23, from 2024-11-03T16:36:50Z to 2024-11-03T16:38:05Z
  (laps from 3 minutes earlier, so the window has a lap anchor). Session-wide records (`sessions`, `meetings`) are whole.
- **Covers:** `SESSION ABORTED` and `RED FLAG`; a car stopped after a crash (43); and a car that didn't start, whose positions are all `(0,0,0)` (23).
- **Unmodified** apart from that filtering: one JSON array per endpoint, as the API returned it.
- **Cut by** `dotnet run scripts/s1-explore.cs -- fixture 9636 2024-sao-paulo 2024-11-03T16:36:50Z 2024-11-03T16:38:05Z 43 23`
  (spike S1, RR-020).