# ADR-0003: Blazor WebAssembly client with thin JavaScript render shims

- **Status:** Accepted (performance to be validated in spike S0)
- **Date:** 2026-10-12
- **Deciders:** Project maintainer

## Context

The client must:

- animate 20 cars at 60 fps with instant seeking (NFR-01, NFR-02);
- render a large time-series chart;
- run from static hosting (ADR-0002);
- be written in C# as far as practical, as part of the project's goals.

Domain and bundle-reader code should be shared with the CLI.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **Blazor WebAssembly (standalone) + thin JS for rAF/canvas/chart** | All logic in C#; shares Domain/Bundle/Playback assemblies with the CLI; static hosting; `[JSImport]`/`[JSExport]` gives low-overhead interop with zero-copy `Span` marshalling | Larger first download than a JS SPA; some JS still needed for canvas and charting |
| Blazor WebAssembly, pure C# rendering (SVG via Razor or canvas via IJSRuntime per draw call) | No JS files | Re-rendering SVG for 20 cars at 60 fps through Blazor diffing, or many interop calls per frame, risks missing the frame budget |
| TypeScript SPA (for example React + canvas) | Smallest download; most charting choices | Logic duplicated outside C#; can't share domain/bundle code; against project goals |
| Blazor Server / Interactive Server | Small download | Needs a server; every frame crosses the network; contradicts ADR-0002 |

## Decision

Build `RaceReplay.Web` as a **standalone Blazor WebAssembly** app on .NET 10.

- **C# owns:** all state, timing, sampling and data loading.
- **JavaScript is limited to:**
  - the `requestAnimationFrame` loop;
  - canvas drawing of the frame buffer;
  - a vendored uPlot chart with cursor/mask overlays.
- **Interop per frame:** one `[JSExport]` call in (`OnFrame`) and two `[JSImport]` calls out. The car frame buffer is passed as `Span<double>` → `MemoryView`, without copying.

## Consequences

- **Positive:**
  - One language for logic and tests.
  - Playback engine shared as a plain library, testable without a browser.
  - Static hosting.
- **Negative:**
  - The WebAssembly runtime download is larger than a JS SPA. Mitigations: trimming, invariant globalisation, fingerprinted caching.
  - About 200 lines of JS to maintain.
  - `[JSExport]` methods must be static, so a small static bridge holds the current render loop.
- **Risks:** frame budget. **Mitigation:** spike S0 measures before M4. Fallbacks, in order:
  1. pass `double[]` instead of `Span`;
  2. enable WebAssembly AOT;
  3. move drawing to `OffscreenCanvas` in a worker.

## Revisit when

- S0 shows the frame budget cannot be met.
- Or a future .NET release offers a materially better in-browser rendering path.
