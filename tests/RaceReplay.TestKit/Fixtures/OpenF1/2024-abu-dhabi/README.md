# OpenF1 fixture: 2024 Abu Dhabi Grand Prix, session 9662

Trimmed recorded responses from the [OpenF1](https://openf1.org) API, used by the adapter tests (docs/06 §9).

- **Licence:** derived from OpenF1 data, which is licensed under
  [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/). So are these files.
  Attribution: **Data: OpenF1 (openf1.org)**. See [DATA-LICENSE](../../../../../DATA-LICENSE.md).
- **Session:** 9662 (Yas Marina Circuit).
- **Kept:** drivers 4 and 20, from 2024-12-08T14:27:50Z to 2024-12-08T14:29:20Z
  (laps from 3 minutes earlier, so the window has a lap anchor). Session-wide records (`sessions`, `meetings`) are whole.
- **Covers:** a pit stop by a lapped car (20), `intervals` with numbers and `"+1 LAP"`, and the race leader (4).
- **Unmodified** apart from that filtering: one JSON array per endpoint, as the API returned it.
- **Cut by** `dotnet run scripts/s1-explore.cs -- fixture 9662 2024-abu-dhabi 2024-12-08T14:27:50Z 2024-12-08T14:29:20Z 4 20`
  (spike S1, RR-020).