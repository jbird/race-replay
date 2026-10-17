# 05 — Web client

The client is a **standalone Blazor WebAssembly** app (`RaceReplay.Web`) served as static files from GitHub Pages (ADR-0003). It plays replays from two kinds of source (ADR-0008):

- **bundles served with the site:** synthetic demo races, plus real races in hosted mode;
- **replay packages (`.rrz`) the user opens from their own disk:** read in the browser, never uploaded.

- **C# owns:** state, timing, data loading and all logic.
- **JavaScript owns:** only what the browser does best — the `requestAnimationFrame` loop, canvas drawing, and the uPlot chart.
- **The two meet** through `[JSImport]`/`[JSExport]` interop. A small, fixed number of calls is made per frame.

## 1. Constraints recap

| Constraint | Source | Design response |
|---|---|---|
| 60 fps with 20 cars | NFR-01 | Allocation-free sampling; canvas drawing in JS; few interop calls per frame |
| Instant seek | NFR-02 | Random-access sampler; seek just sets the clock position |
| GitHub Pages: static, sub-path (`/<repo>/`), no custom headers, no compression negotiation | ADR-0010 | `<base href>` rewritten at deploy; relative URLs; `404.html` fallback; Brotli decoded in the browser; fingerprinting; settings from the catalogue (§12) |
| No real data on the public site | ADR-0008, NFR-12 | Demo catalogue is synthetic; real races come from local packages via `InputFile`; nothing is uploaded |
| Small download | NFR-03 | Trimming; invariant globalisation; no Analysis/Application code in the app; vendored uPlot (~50 KB); Brotli framework files on Pages (§12) |
| Spoiler-free viewing | FR-10 | All "future" data is masked at the view layer, not filtered in the data |

## 2. Project setup

```xml
<!-- src/RaceReplay.Web/RaceReplay.Web.csproj -->
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>                     <!-- required by the JSImport/JSExport generator -->
    <InvariantGlobalization>true</InvariantGlobalization>           <!-- no ICU data download -->
    <BlazorEnableTimeZoneSupport>false</BlazorEnableTimeZoneSupport>
    <OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders> <!-- .NET 10 static asset fingerprinting in index.html -->
    <RunAOTCompilation>false</RunAOTCompilation>                    <!-- decided in M7 (open question Q4) -->
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\RaceReplay.Playback\RaceReplay.Playback.csproj" />
    <ProjectReference Include="..\RaceReplay.Bundle\RaceReplay.Bundle.csproj" />
  </ItemGroup>
</Project>
```

```csharp
// src/RaceReplay.Web/Program.cs
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services
    .AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) })
    .AddSingleton<ReplayCatalog>()                 // reads bundles/catalog.json
    .AddSingleton<HttpReplaySource>()              // IReplaySource: demo and hosted bundles
    .AddSingleton<LocalPackageReplaySource>()      // IReplaySource: the .rrz the user opened
    .AddSingleton<ReplayStore>()                   // view state; concrete (ADR-0009)
    .AddSingleton<ReplaySessionHost>()             // owns the per-replay object graph (clock, sampler, renderers)
    .AddSingleton<ITrackMapRenderer, CanvasTrackMapRenderer>()   // JS boundary: interface kept
    .AddSingleton<IGapChartRenderer, UPlotGapChartRenderer>()
    .AddOptions<PlaybackOptions>().Bind(builder.Configuration.GetSection("Playback"));

await JSHost.ImportAsync("renderLoop", "../js/renderLoop.js");
await JSHost.ImportAsync("trackMap", "../js/trackMap.js");
await JSHost.ImportAsync("gapChart", "../js/gapChart.js");

await builder.Build().RunAsync();
```

Module URLs are relative to the `_framework/` folder that loads the runtime. That is why they start with `../`. It also keeps them correct under the GitHub Pages sub-path.

**Cache-busting.** With `OverrideHtmlAssetPlaceholders`, `index.html` uses `_framework/blazor.webassembly#[.{fingerprint}].js` and an empty `<script type="importmap">`, which the build fills in. The app's own JS modules can be fingerprinted too, with a `<StaticWebAssetFingerprintPattern Include="JSModule" Pattern="*.js" Expression="#[.{fingerprint}]!" />` item, and are resolved through the generated import map. Spike S0 confirms that `JSHost.ImportAsync` resolves fingerprinted modules through the import map. If it doesn't, the fallback is a query-string version (`trackMap.js?v={build}`).

**Starting Blazor.** `index.html` starts Blazor manually so that framework files can be fetched as Brotli and decoded in the browser on GitHub Pages (§12). `autostart="false"` is set on the fingerprinted Blazor script, and `js/boot.js` calls `Blazor.start({ loadBootResource })`. Locally, `boot.js` falls back to default loading.

**Site mode** (demo or hosted) comes from `bundles/catalog.json` (`"mode"`), not from `appsettings.json`. The deploy step regenerates the catalogue but never edits published app files ([04 §8](04-data-pipeline-and-bundle-format.md#8-catalogue)).

**Per-replay lifetime.** `ReplaySessionHost` creates a fresh `PlaybackClock`, `FrameSampler` and `RenderLoop` for each loaded replay, and disposes the previous ones. It is a small factory, so components never hold stale replay state.

## 3. UI structure

### 3.1 Routes

| Route | Component | Notes |
|---|---|---|
| `/` | `CatalogPage` | Demo races, an **Open replay…** drop zone, and a short "how to watch a real race" guide. In hosted mode, real races are listed too, grouped by year. |
| `/replay/demo/{Id:int}` | `ReplayPage` | Synthetic demo race. Query: `t` (seconds), `focus` (driver number), `spoilers` (`off`). |
| `/replay/local` | `ReplayPage` | The package opened from disk. Without an open package, it shows the drop zone; `t` and `focus` are applied once a file is opened. |
| `/replay/{Year:int}/{SessionKey:int}` | `ReplayPage` | Hosted mode only. Otherwise redirects to the catalogue with an explanation. |
| `*` | `NotFound` | Link back to the catalogue |

### 3.2 Layout

```text
┌────────────────────────────────────────────────────────────────────┐
│ Race Replay · 2025 · Abu Dhabi Grand Prix · Race [Open…][Catalogue]│
├──────────────────────────────────────────────┬─────────────────────┤
│                                              │ LAP 23 / 58 1:02:14 │
│                                              │  1 NOR   Leader     │
│            TrackMap (two stacked canvases)   │  2 VER   +1.8   1.8 │
│                                              │  3 LEC   +4.1   2.3 │
│                                              │  …   TimingTower    │
├──────────────────────────────────────────────┴─────────────────────┤
│ ▶  1× ▾  ───●─────────▲────■■■■─────────▲──────────────  1:02:14   │
│          Scrubber  (▲ pit stop · ■ SC/VSC · ▮ red flag)            │
├────────────────────────────────────────────────────────────────────┤
│ Gap (s)  [to leader | to focus]   [ ] spoiler-free                 │
│  ~~~~~~~~~~~~~~ GapChart (uPlot) with cursor line ~~~~~~~~~~~~~~   │
└────────────────────────────────────────────────────────────────────┘
```

### 3.3 Components

| Component | Responsibility | Depends on |
|---|---|---|
| `CatalogPage` | Demo list, hosted list (if the catalogue's `mode` is `hosted`), open-package entry point, how-to guide with a link to the latest CLI release | `ReplayCatalog` |
| `OpenPackage` | `InputFile` plus drag-and-drop zone. Reads the chosen `.rrz` into memory and navigates to `/replay/local`. | `LocalPackageReplaySource` |
| `ReplayPage` | Pick the source for the route, load the replay, create the session host, wire children, sync URL | `HttpReplaySource` / `LocalPackageReplaySource` (as `IReplaySource`), `ReplaySessionHost`, `NavigationManager` |
| `TrackMap` | Host canvases. Forward size changes. Driver click → focus. | `ITrackMapRenderer` |
| `TimingTower` | Running order, gaps, intervals, laps-down, status (PIT / OUT) | `ReplayStore` |
| `PlaybackControls` | Play/pause, rate selector, time readout | `IPlaybackControl`, `ReplayStore` |
| `Scrubber` | Range input plus event markers. Begin/seek/end scrub. | `IPlaybackControl`, `ReplayStore` |
| `GapChart` | Host the chart element. Mode toggle (leader/focus). | `IGapChartRenderer`, `ReplayStore` |
| `DataNotice` | Footer banner: "Synthetic demo — fictional data" or the OpenF1 attribution and licence for real packages | `ReplayStore` (provenance) |
| `LoadingOverlay` | Download/read progress, errors, retry | — |

Components receive the narrow interfaces they need where a seam exists. For example, `PlaybackControls` gets `IPlaybackControl`, never the whole clock (ISP). `ReplayStore` is a small concrete class; component tests use the real one (ADR-0009).

## 4. Playback engine (`RaceReplay.Playback`)

The engine is plain .NET with no Blazor or JS references, so it is unit-tested like any library ([06](06-testing-strategy.md)).

### 4.1 Clock

```csharp
public sealed class PlaybackClock(double duration, PlaybackOptions options) : IPlaybackClock
{
    private double? _lastFrameMs;
    private bool _resumeAfterScrub;

    public double Duration { get; } = duration;
    public double Position { get; private set; }
    public bool IsPlaying { get; private set; }

    public double Rate
    {
        get;
        private set => field = value >= options.MinRate && value <= options.MaxRate
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported playback rate.");
    } = 1.0;

    public void SetRate(double rate) => Rate = rate;
    public void Play()   { if (Position >= Duration) Position = 0; IsPlaying = true; _lastFrameMs = null; }
    public void Pause()  => IsPlaying = false;
    public void Seek(double position) => Position = Math.Clamp(position, 0, Duration);
    public void BeginScrub() { _resumeAfterScrub = IsPlaying; IsPlaying = false; }
    public void EndScrub()   { if (_resumeAfterScrub) Play(); }

    public double Advance(double frameTimestampMs)
    {
        if (IsPlaying && _lastFrameMs is double last)
        {
            // Cap dt so returning to a background tab doesn't jump minutes ahead.
            double dt = Math.Min(frameTimestampMs - last, options.MaxFrameDeltaMs) / 1000.0;
            Position = Math.Min(Position + dt * Rate, Duration);
            if (Position >= Duration) IsPlaying = false;
        }
        _lastFrameMs = frameTimestampMs;
        return Position;
    }
}
```

The clock takes **explicit timestamps** from `requestAnimationFrame`, so tests drive it with plain numbers and need no timers or `TimeProvider`.

**Skipping red flags (FR-14)** is a decorator, `SegmentSkippingClock : IPlaybackClock`. It wraps the clock and, after `Advance`, jumps past `Suspended` segments when the user has enabled the option. The core clock stays simple, and the behaviour can be added or removed without editing it (OCP).

### 4.2 Frame sampler

```csharp
public sealed class FrameSampler(ReplaySession replay)   // concrete: one implementation, tested directly
{
    // Frame record per car (driver index order): [x, y, visible (0|1), progress]
    public int Stride => 4;

    public void Sample(double position, Span<double> frame)
    {
        var tl = replay.Timeline;
        double f = Math.Clamp(position * tl.SampleRateHz, 0, tl.SampleCount - 1);
        int i = (int)f;
        int j = Math.Min(i + 1, tl.SampleCount - 1);
        double a = f - i;

        for (int c = 0; c < replay.Cars.Count; c++)
        {
            CarChannels car = replay.Cars[c];
            Span<double> o = frame.Slice(c * Stride, Stride);
            bool vi = car.Valid[i], vj = car.Valid[j];
            if (vi && vj)
            {
                o[0] = car.X.Span[i] + (car.X.Span[j] - car.X.Span[i]) * a;
                o[1] = car.Y.Span[i] + (car.Y.Span[j] - car.Y.Span[i]) * a;
                o[2] = 1;
                o[3] = car.Progress.Span[i] + (car.Progress.Span[j] - car.Progress.Span[i]) * a;
            }
            else if (vi || vj)
            {
                int k = vi ? i : j;                                  // edge of a data gap: hold nearest
                o[0] = car.X.Span[k]; o[1] = car.Y.Span[k]; o[2] = 1; o[3] = car.Progress.Span[k];
            }
            else
            {
                o[2] = 0;                                            // no data: hide the car
            }
        }
    }
}
```

The cost is O(cars) and there are no allocations. A unit test asserts zero allocated bytes with `GC.GetAllocatedBytesForCurrentThread()` (NFR-01).

### 4.3 Running order and tower rows

`RunningOrderCalculator` sorts drivers by interpolated progress into a caller-supplied `Span<int>`, using `MemoryExtensions.Sort` with a struct comparer. `TowerProjector` turns the order plus gap series into `TowerRow` records:

- gap to leader and interval;
- laps down;
- status — `PIT` within a pit window, `OUT` after retirement.

These are produced at the UI refresh rate (§8), not per frame.

### 4.4 Gap chart data

`GapChartProjector` prepares the chart's arrays **once per mode change**:

- **To leader:** the shipped series.
- **To focus:** `gap_c − gap_focus`, computed in O(n).
- Optional lapped-car clamping.

It outputs a flat `double[]` (driver-major) with `NaN` for gaps in the data.

## 5. JavaScript interop

### 5.1 Contract

| Direction | .NET signature | JS function | Frequency |
|---|---|---|---|
| JS → .NET | `[JSExport] static void RenderLoopBridge.OnFrame(double timestampMs)` | — | Every animation frame |
| .NET → JS | `[JSImport("drawCars","trackMap")] static partial void DrawCars([JSMarshalAs<JSType.MemoryView>] Span<double> frame, int stride, int focusIndex)` | `trackMap.drawCars` | Every frame |
| .NET → JS | `[JSImport("setCursor","gapChart")] static partial void SetCursor(double time)` | `gapChart.setCursor` | Every frame |
| .NET → JS | `[JSImport("init","trackMap")]`, `[JSImport("setData","gapChart")]` | `init`, `setData` | On load or mode change |
| JS → .NET | `[JSExport] static void ChartBridge.OnSeek(double time)` | — | On chart click/drag |
| .NET → JS | `[JSImport("start"/"stop","renderLoop")]` | `start`, `stop` | Replay open/close |

Per frame there are **three** interop transitions:

1. `OnFrame` into .NET;
2. `DrawCars` out to JS, carrying the frame buffer;
3. `SetCursor` out to JS, carrying one double.

Measured interop costs are fractions of a microsecond each, so the overhead is negligible next to canvas drawing.

### 5.2 Zero-copy frame buffer

- The frame buffer is a single `double[]` of `cars × stride`, allocated once per replay.
- Passing it as `Span<double>` marshals it as a `MemoryView` over WebAssembly memory **without copying**.
- A `MemoryView` created from a `Span` is **only valid during the call**. The JS side must read it synchronously and must not keep a reference.
- `Span<double>` is used (not `float`) because it is the most widely supported `MemoryView` element type across .NET versions.
- **Spike S0** verifies the exact `[JSImport]` + `Span<double>` pattern on .NET 10 and measures frame time. **Fallback:** pass a `double[]`, which is copied into a JS array. At 80 numbers per frame this is still cheap.

### 5.3 .NET side

```csharp
[SupportedOSPlatform("browser")]
internal static partial class RenderLoopBridge
{
    private static IRenderLoop? s_loop;                       // static only at the interop edge
    public static void Attach(IRenderLoop loop) => s_loop = loop;
    public static void Detach() => s_loop = null;

    [JSExport]
    internal static void OnFrame(double timestampMs) => s_loop?.Tick(timestampMs);
}

[SupportedOSPlatform("browser")]
internal static partial class TrackMapInterop
{
    [JSImport("init", "trackMap")]
    internal static partial void Init(JSObject canvasHost, double[] centreline, string[] acronyms, string[] colours, double rotationDegrees);

    [JSImport("drawCars", "trackMap")]
    internal static partial void DrawCars([JSMarshalAs<JSType.MemoryView>] Span<double> frame, int stride, int focusIndex);
}

public sealed class RenderLoop(
    IPlaybackClock clock, FrameSampler sampler, ITrackMapRenderer map,
    IGapChartRenderer chart, IUiPublisher publisher, int carCount) : IRenderLoop
{
    private readonly double[] _frame = new double[carCount * sampler.Stride];

    public void Tick(double timestampMs)
    {
        double t = clock.Advance(timestampMs);
        sampler.Sample(t, _frame);
        map.DrawFrame(_frame, sampler.Stride);
        chart.SetCursor(t);
        publisher.MaybePublish(t, timestampMs);   // throttled view-state update (§8)
    }
}
```

`[JSExport]` methods must be static, so the bridge holds the current `IRenderLoop` in a static field. This is the **only** static mutable state in the app, and it is confined to the interop edge. Everything behind it is DI-managed and testable.

### 5.4 JS side (illustrative)

```js
// wwwroot/js/renderLoop.js
let exports, handle = 0;
export async function start(assemblyName) {
  const { getAssemblyExports } = await globalThis.getDotnetRuntime(0);
  exports ??= await getAssemblyExports(assemblyName);
  const tick = (ts) => {
    exports.RaceReplay.Web.Interop.RenderLoopBridge.OnFrame(ts);
    handle = requestAnimationFrame(tick);
  };
  handle = requestAnimationFrame(tick);
}
export function stop() { cancelAnimationFrame(handle); }
```

```js
// wwwroot/js/trackMap.js (excerpt)
export function drawCars(frameView, stride, focusIndex) {
  frameView.copyTo(frame);                 // MemoryView: copy now into a reused Float64Array, never store the view
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  for (let c = 0, o = 0; c < drivers.length; c++, o += stride) {
    if (frame[o + 2] === 0) continue;      // not visible
    const x = frame[o], y = frame[o + 1];  // world → screen with the view transform, so circles and labels stay upright
    drawCar(view.a * x + view.c * y + view.e, view.b * x + view.d * y + view.f, drivers[c], c === focusIndex);
  }
}
```

The JS modules are deliberately thin, with no state beyond canvas/chart handles and the view transform. Together they come to roughly 200 lines. Logic stays in C#, where it is tested.

## 6. Track map rendering

- **Two stacked canvases.**
  - The *static* layer (track outline, pit lane hint, start/finish marker, sector ticks) is drawn once per resize from the centreline.
  - The *dynamic* layer (cars, labels, focus ring) is redrawn every frame.
- **View transform.** Fit the track bounds to the canvas with a margin, apply the manifest's `displayRotationDegrees`, flip Y (track coordinates are Y-up; canvas is Y-down), and scale for `devicePixelRatio` so lines stay crisp.
- **Resize.** A `ResizeObserver` in JS recomputes the transform and redraws the static layer.
- **Cars.**
  - Filled circles in team colour, with a contrasting outline. Two cars from the same team share a colour, so the second gets a dashed outline.
  - Driver acronym labels sit on a leader line when cars overlap. Label collision avoidance is simple: offset alternately.
- **Focus.** A larger ring around the focused car, and other cars drawn at reduced opacity (optional).
- **Interaction.** Click hit-testing is done in JS against the last drawn positions, and calls back into .NET to set focus.

## 7. Gap chart

- **Library:** [uPlot](https://github.com/leeoniya/uPlot) (MIT), **vendored** under `wwwroot/lib/uplot/` to avoid runtime CDN dependencies. It handles 20 series × 7,200 points easily.
- **Data:** time in seconds on the x axis (labelled as race time or lap) and gap in seconds on the y axis. The y axis is inverted, so the leader is at the top and cars drop down as they fall back, matching broadcast convention. `NaN` values are converted to `null` in JS, so lines break across missing data.
- **Cursor:** a vertical line in an absolutely positioned overlay element, moved per frame with `transform: translateX(u.valToPos(t, "x"))`. This avoids redrawing the chart every frame.
- **Spoiler-free mask (FR-10):** a second overlay covers `[t, end]` in the page background colour. The timing tower likewise hides final positions and retirements that are still in the future.
- **Seek from chart (FR-06):** pointer down/move/up on the plot area maps the x pixel to time via `u.posToVal`. It calls `ChartBridge.OnSeek`, which uses the same begin/seek/end scrub sequence as the scrubber.
- **Focus:** the focused series is drawn thicker. Other series are dimmed.
- **Segments:** SC/VSC/red-flag bands are drawn as shaded backgrounds via a uPlot `drawClear` hook (static, so drawn once).

## 8. State and UI refresh

```csharp
public sealed record ReplayViewState(
    double Position, double Duration, bool IsPlaying, double Rate,
    int LeaderLap, int TotalLaps, ImmutableArray<TowerRow> Tower,
    DriverNumber? Focus, GapMode GapMode, bool SpoilerFree, TrackStatus TrackStatus);

public sealed class ReplayStore        // concrete: one implementation, used as-is in component tests
{
    public ReplayViewState State { get; private set; } = ReplayViewState.Empty;
    public event Action? Changed;
    public void Publish(ReplayViewState state) { State = state; Changed?.Invoke(); }
    public void Update(Func<ReplayViewState, ReplayViewState> change) => Publish(change(State)); // user intents
}
```

- **One-way data flow.**
  - The render loop produces `ReplayViewState` snapshots.
  - Components render from the snapshot.
  - User actions go to `IPlaybackControl` or `ReplayStore.Update`, never to other components.
- **Throttling.** `UiPublisher` publishes at most `UiRefreshHz` (default 10) times per second. It publishes immediately on discrete changes (play/pause, seek end, focus, mode). The tower updates smoothly while Blazor diffing runs only about 10 times a second.
- **Components** subscribe in `OnInitialized`, unsubscribe in `Dispose`, and re-render via `InvokeAsync(StateHasChanged)`. The tower uses `@key` on driver number, so DOM rows move rather than re-create. Position changes animate with a CSS `transform` transition, disabled under `prefers-reduced-motion`.
- **URL sync (FR-11).** When playback pauses, or a seek settles for 1 s, `NavigationManager.NavigateTo(uri, replace: true)` updates `?t=…&focus=…`. This is never done per frame.

## 9. Loading and errors

Both sources implement one small interface. Two implementations make it a real seam (ADR-0009).

```csharp
public interface IReplaySource
{
    Task<ReplaySession> LoadAsync(string id, IProgress<double> progress, CancellationToken ct);
}
```

### 9.1 From the site (`HttpReplaySource`): demo and hosted races

1. Fetch `catalog.json` (cached for the session) and resolve the path for the id.
2. Fetch `manifest.json`, then the `.rrb` files in parallel. Read response streams in chunks to report progress against the manifest's byte counts. Pages gzips the responses, so `Content-Length` is the compressed size.
3. There is nothing to decompress: the browser inflates the gzipped responses as they arrive (option D, [04 §9](04-data-pipeline-and-bundle-format.md#9-compression-and-hosting-spike-s2)).
4. `ReplayBundleReader.Read(...)` → `ReplaySession`. Decoding happens once here, in one pass: running sums for the delta encodings, then dequantising. Spike S2 measured 49–56 ms warm and 117–157 ms cold for a 2-hour race.

### 9.2 From the user's disk (`LocalPackageReplaySource`): real races

1. `OpenPackage` uses Blazor's `InputFile` (styled as a drop zone, which also accepts drag-and-drop). It calls `IBrowserFile.OpenReadStream(maxAllowedSize: 64 MB)` and copies the stream into a `MemoryStream`, reporting progress.
2. The bytes stay in the browser's memory. **Nothing is uploaded.** The page says so next to the drop zone.
3. `ReplayPackageReader.ReadPackage(...)` validates entries (allow-listed names, size caps) and returns the `ReplaySession`.
4. The source keeps only the most recently opened package. Reopening a file after a page reload requires choosing it again. Remembering recent files is a backlog item.

### 9.3 Then, for both sources

`ReplaySessionHost` builds the clock, sampler, renderers and render loop, initialises the JS modules and starts the loop. If `?t=` or `?focus=` is present, they are applied. `DataNotice` shows "Synthetic demo — fictional data", or the OpenF1 attribution and licence, based on the manifest's `provenance.source`.

| Failure | User sees | Recovery |
|---|---|---|
| Network error or timeout (site bundles) | "Couldn't load this replay" with details | Retry button |
| Wrong file type, or not a valid package | "That file isn't a Race Replay package (.rrz)" | Choose another file |
| Bundle schema major newer than the app | "This replay needs a newer version of the app" | Hard-refresh hint (assets are fingerprinted) |
| Bundle schema major older than the app (local) | "This package was built with an older CLI. Rebuild it with `race-replay build`." | Link to the how-to guide |
| Package larger than the size cap | "That file is too large to be a replay package" | — |
| Replay not in the catalogue, or hosted route in demo mode | Not-found page, or an explanation of demo mode | Link to the catalogue |
| Corrupt bundle (bounds or length check failed) | Error with the replay id | Report link (GitHub issue template) |

## 10. Keyboard and accessibility

| Key | Action |
|---|---|
| `Space` | Play / pause |
| `←` / `→` | Seek −5 s / +5 s (`Shift`: ±30 s) |
| `[` / `]` | Slower / faster |
| `F` | Cycle focus through the top 10 |
| `S` | Toggle spoiler-free mode |

- All controls are native elements (`button`, `input type=range`, `select`) with `aria-label`s.
- The scrubber exposes `aria-valuetext` as the race clock and lap (for example "Lap 23, 1 hour 2 minutes 14 seconds").
- The tower is a semantic `table`. A polite live region announces lead changes when enabled.
- The canvas has a text alternative: the tower conveys the same running order.
- Colour is never the only signal. Driver acronyms are always available on hover/focus, and team-mates get distinct outline styles.

## 11. Performance budget

| Item | Budget | Notes |
|---|---|---|
| `Advance` + `Sample` (20 cars) | < 0.1 ms | Pure arithmetic |
| Interop (3 calls) | < 0.05 ms | Measured in S0 |
| Canvas draw (JS) | < 3 ms | 20 circles + labels; static layer cached |
| Cursor overlay move | < 0.1 ms | CSS transform only |
| Blazor re-render (10 Hz) | < 4 ms | Tower ~20 rows with `@key` |
| Managed allocations per frame | 0 B | Unit-tested |
| App download (over the wire) | ≤ 3 MB | Trimmed. About 2.73 MB as Pages gzips it today (S2); relinking and Brotli decoding (§12) are RR-070's |
| Demo or hosted bundle download (over the wire) | ≤ 3 MB | NFR-04. A 2-hour race's `positions.rrb` is 1.93 MB with the delta encodings (S2) |
| Open a local 6 MB package | < 1 s to first frame | NFR-03. 348–480 ms cold in S2 (Chrome 155 and Firefox 157 on a 2015 desktop) |

If frame time is over budget on target hardware, the levers in order are:

1. reduce label drawing (draw labels only for focus and the top N);
2. enable WebAssembly AOT (`RunAOTCompilation`);
3. move dequantised arrays to `float[]` (already planned);
4. use `OffscreenCanvas` in a worker for the dynamic layer.

## 12. GitHub Pages specifics

GitHub Pages is a fixed static host (ADR-0010). The client is designed around what Pages does and doesn't do.

### 12.1 Paths and routing

- **Base path.** `index.html` ships with `<base href="/" />`. `scripts/assemble-site.sh` rewrites it to `/<repo-name>/`, or to `SITE_BASE_PATH` for a custom domain ([07 §6.3](07-repository-ci-and-delivery.md#63-deploy-pagesyml-and-scriptsassemble-sitesh)). All in-app URLs are relative, so local development (at `/`) and Pages (at `/<repo>/`) behave the same.
- **Deep links.** `404.html` is a copy of `index.html`, so `/race-replay/replay/demo/1?t=3600` loads the app, which routes client-side. Pages returns those deep links with HTTP status 404. Browsers don't care, and the app has no SEO needs.
- **Jekyll.** Deploying from Actions skips Jekyll, so `_framework` is served as-is and no `.nojekyll` file is needed.

### 12.2 Compression: decode Brotli in the browser

`dotnet publish` produces `.br` (and `.gz`) copies of the framework files. Pages can't negotiate pre-compressed files, so without help the browser downloads the uncompressed `.wasm` and assemblies. Microsoft's documented approach for GitHub Pages is to fetch the `.br` copies and decode them in the browser.

```html
<!-- wwwroot/index.html (excerpt) -->
<script src="_framework/blazor.webassembly#[.{fingerprint}].js" autostart="false"></script>
<script type="module" src="js/boot.js"></script>
```

```js
// wwwroot/js/boot.js — Brotli decoding for static hosts (GitHub Pages); default loading locally
import { BrotliDecode } from '../lib/brotli/decode.min.js';   // vendored from google/brotli (MIT)

const decodeBrotli = !['localhost', '127.0.0.1'].includes(location.hostname);

Blazor.start({
  loadBootResource(type, name, defaultUri, integrity) {
    if (!decodeBrotli || type === 'dotnetjs' || type === 'configuration') {
      return;                                      // default loading
    }
    return (async () => {
      const response = await fetch(defaultUri + '.br', { cache: 'no-cache' });
      if (!response.ok) throw new Error(response.statusText);
      const decoded = BrotliDecode(new Int8Array(await response.arrayBuffer()));
      const contentType = type === 'dotnetwasm' ? 'application/wasm' : 'application/octet-stream';
      return new Response(decoded, { headers: { 'content-type': contentType } });
    })();
  }
});
```

- **Keep the `.br` files.** `assemble-site.sh` keeps them and deletes only the unused `.gz` copies. The earlier draft deleted both, which threw away the compression.
- **Integrity.** The decoded bytes are the original file, so Blazor's integrity checks still pass. The site is uploaded as a built artifact, never committed to a branch, so Git line-ending conversion can't alter files after hashing.
- **What spikes S0 and S2 found:**
  - the approach works with .NET 10 fingerprinted file names (S0);
  - Pages already gzips every response, `.wasm` included, much like zlib at level 5, and never sends Brotli (S0, S2);
  - so Brotli saves only about 0.3 MB over Pages' gzip, for about 140 ms of decoding on a 2015 desktop (S0).

  RR-070 (#37) decides whether that pays off, once the decoder's cost on a mid-range laptop is known. Relinking the native runtime in CI saves about 0.6 MB at no runtime cost, and comes first.

### 12.3 Headers and caching

- **No custom headers.** Pages doesn't let sites set response headers. It sends a short, fixed cache lifetime, reported as `max-age=600`.
  - **Caching** relies on fingerprinted file names (§2), and on Blazor caching boot resources in the browser's Cache Storage, so repeat visits are fast regardless of HTTP caching.
  - **No COOP/COEP headers** means no `SharedArrayBuffer`, so no WebAssembly multithreading. The design is single-threaded by choice (§4–§5). The `OffscreenCanvas`-in-a-worker lever (§11) passes data with `postMessage` and doesn't need shared memory.
  - **Content Security Policy** can only be set with `<meta http-equiv="Content-Security-Policy">`. It needs `'wasm-unsafe-eval'` for WebAssembly and a hash for the inline import map, which changes every build, so `assemble-site.sh` would compute and inject it. Planned as RR-095 in M7. All scripts are self-hosted, with no CDNs, which limits the exposure meanwhile.

### 12.4 What's served

| Path | Content | Source |
|---|---|---|
| `/` and `_framework/` | App, with Brotli copies | `dotnet publish` |
| `bundles/catalog.json` | Catalogue, including `mode` | `race-replay catalog` in `assemble-site.sh` |
| `bundles/demo/{seed}/` | Synthetic demo races | `race-replay build --source synthetic` |
| `bundles/{year}/{key}/` | Real races, hosted mode only | `data` branch |
| `schema/` | JSON Schemas referenced by manifests' `$schema` | `docs/schema/` |
| `404.html` | Copy of `index.html` | `assemble-site.sh` |

Local packages opened with `InputFile` never touch Pages. They are read in the browser from the user's disk.
