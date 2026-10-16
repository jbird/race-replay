# OpenF1 fixture: 2023 Singapore Grand Prix, session 9165

Trimmed recorded responses from the [OpenF1](https://openf1.org) API, used by the adapter tests (docs/06 §9).

- **Licence:** derived from OpenF1 data, which is licensed under
  [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/). So are these files.
  Attribution: **Data: OpenF1 (openf1.org)**. See [DATA-LICENSE](../../../../../DATA-LICENSE.md).
- **Session:** 9165 (Singapore).
- **Kept:** drivers 55 and 2, from 2023-09-17T12:36:30Z to 2023-09-17T12:37:55Z
  (laps from 3 minutes earlier, so the window has a lap anchor). Session-wide records (`sessions`, `meetings`) are whole.
- **Covers:** `SAFETY CAR DEPLOYED` and pit stops under it (55 and 2); `intervals` are sparse under the safety car.
- **Unmodified** apart from that filtering: one JSON array per endpoint, as the API returned it.
- **Cut by** `dotnet run scripts/s1-explore.cs -- fixture 9165 2023-singapore 2023-09-17T12:36:30Z 2023-09-17T12:37:55Z 55 2`
  (spike S1, RR-020).