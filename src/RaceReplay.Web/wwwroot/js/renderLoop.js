// The requestAnimationFrame loop (docs/05 §5). Each frame makes one call into .NET, which advances the clock,
// samples the cars and draws them. No other state, except frame statistics when the page URL has ?perf.

import { takePaintMs } from './trackMap.js';

const assemblyName = 'RaceReplay.Web';
const perfWindowMs = 10_000;
let generation = 0;
let handle = 0;
let onFrame = null;
let perfPanel = null;

async function frameCallback() {
  if (!onFrame) {
    const runtime = await globalThis.getDotnetRuntime(0);
    const exports = await runtime.getAssemblyExports(assemblyName);
    onFrame = exports.RaceReplay.Web.Interop.RenderLoopBridge.OnFrame;
  }
  return onFrame;
}

export function start() {
  stop();
  const current = generation;
  const stats = new URLSearchParams(location.search).has('perf') ? new FrameStats() : null;
  let failed = false;
  frameCallback().then(
    (callback) => {
      if (current !== generation) return; // stopped while the exports were resolving
      const run = (timestamp) => {
        try {
          callback(timestamp);
        } catch (error) {
          if (!failed) { // report once, not 60 times a second
            failed = true;
            console.error('Race Replay: a frame failed.', error);
            document.getElementById('blazor-error-ui')?.style.setProperty('display', 'block');
          }
        }
      };
      // Without ?perf the loop makes no timing calls at all.
      const frame = stats
        ? (timestamp) => {
          const begin = performance.now();
          run(timestamp);
          stats.record(timestamp, performance.now() - begin, takePaintMs());
        }
        : run;
      const tick = (timestamp) => {
        if (current !== generation) return;
        handle = requestAnimationFrame(tick); // first, so one failing frame doesn't stop the loop
        frame(timestamp);
      };
      handle = requestAnimationFrame(tick);
    },
    (error) => console.error('Race Replay: the render loop could not start.', error));
}

export function stop() {
  generation++;
  cancelAnimationFrame(handle);
  handle = 0;
  perfPanel?.remove();
  perfPanel = null;
}

// ?perf: frame intervals, and time inside OnFrame split into canvas drawing and the rest (.NET: clock, sampling,
// interop, view-state publishes and the Blazor re-renders they trigger). Summarised every 10 s (spike S0, M7).
class FrameStats {
  constructor() {
    const capacity = 4096; // preallocated, so recording doesn't allocate per frame
    this.intervals = new Float64Array(capacity);
    this.total = new Float64Array(capacity);
    this.canvas = new Float64Array(capacity);
    this.dotnet = new Float64Array(capacity);
    this.count = 0;
    this.start = -1;
    this.last = -1;
  }

  record(timestamp, totalMs, canvasMs) {
    if (this.start < 0) {
      this.start = this.last = timestamp;
      return;
    }
    if (this.count < this.intervals.length) {
      this.intervals[this.count] = timestamp - this.last;
      this.total[this.count] = totalMs;
      this.canvas[this.count] = canvasMs;
      this.dotnet[this.count] = Math.max(0, totalMs - canvasMs);
      this.count++;
    }
    this.last = timestamp;
    if (timestamp - this.start >= perfWindowMs) {
      this.report(timestamp - this.start);
      this.count = 0;
      this.start = timestamp;
    }
  }

  report(elapsedMs) {
    const n = this.count;
    if (n === 0) return;
    const sorted = (values) => values.slice(0, n).sort();
    const at = (values, q) => values[Math.min(n - 1, Math.floor(q * n))];
    const mean = (values) => values.reduce((sum, v) => sum + v, 0) / n;
    const row = (label, values) => {
      const s = sorted(values);
      return `${label.padEnd(12)}mean ${mean(s).toFixed(3)}  p50 ${at(s, 0.5).toFixed(3)}  p95 ${at(s, 0.95).toFixed(3)}  max ${s[n - 1].toFixed(3)}`;
    };
    const dropped = this.intervals.slice(0, n).filter((ms) => ms > 20).length;
    const text = [
      `Race Replay ?perf: ${n} frames in ${(elapsedMs / 1000).toFixed(1)} s = ${(n * 1000 / elapsedMs).toFixed(1)} fps, ${dropped} over 20 ms`,
      row('frame ms', this.intervals),
      row('OnFrame ms', this.total),
      row('  canvas ms', this.canvas),
      row('  .NET ms', this.dotnet),
      `${navigator.userAgent}, devicePixelRatio ${window.devicePixelRatio}`,
    ].join('\n');
    console.info(text);
    if (!perfPanel) {
      perfPanel = document.createElement('pre');
      perfPanel.className = 'perf-panel';
      document.body.appendChild(perfPanel);
    }
    perfPanel.textContent = text;
  }
}
