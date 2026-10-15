// S0 spike (throwaway, spike/s0-chart, not merged): the M5 gap-chart path early (docs/08 §3, docs/05 §7).
// Builds a uPlot chart of 20 × 7,200 points, times a full redraw, then moves a cursor overlay every frame with a
// CSS transform for 10 s, as gapChart.setCursor will.
import uPlot from '../lib/uplot/uPlot.esm.js';

export async function run(hostId, outId) {
  const host = document.getElementById(hostId);
  const out = document.getElementById(outId);
  const lines = [];
  const log = (line) => { lines.push(line); out.textContent = lines.join('\n'); };

  const series = 20, points = 7200; // 2 h at 1 Hz
  let seed = 1;
  const random = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
  const data = [Float64Array.from({ length: points }, (_, i) => i)];
  for (let s = 0; s < series; s++) {
    const y = new Array(points);
    let gap = s * 1.5;
    for (let i = 0; i < points; i++) {
      gap = Math.max(0, gap + (random() - 0.48) * 0.3);
      y[i] = (i > 2000 + s * 50 && i < 2060 + s * 50) ? null : gap; // a hole per driver: lines break
    }
    data.push(y);
  }

  // uPlot may draw after the call returns, so time until its draw hook fires.
  let notifyDrawn = null;
  const drawn = () => new Promise((resolve) => { notifyDrawn = resolve; });
  const width = () => Math.min(host.parentElement.clientWidth, 1400);
  const opts = {
    width: width(), height: 360,
    scales: { x: { time: false }, y: { dir: -1 } }, // leader at the top
    legend: { show: false },
    cursor: { show: false },
    series: [{}, ...Array.from({ length: series }, (_, s) => ({ stroke: `hsl(${s * 18} 70% 60%)`, width: 1 }))],
    hooks: { draw: [() => notifyDrawn?.()] },
  };

  let begin = performance.now();
  let done = drawn();
  const chart = new uPlot(opts, data, host);
  await done;
  log(`build and first draw (20 × 7,200 points): ${(performance.now() - begin).toFixed(1)} ms`);
  for (const w of [width() - 40, width()]) {
    begin = performance.now();
    done = drawn();
    chart.setSize({ width: w, height: 360 });
    await done;
    log(`full redraw on resize to ${w} px: ${(performance.now() - begin).toFixed(1)} ms`);
  }

  const cursor = document.createElement('div');
  cursor.style.cssText = 'position:absolute;top:0;bottom:0;left:0;width:2px;background:#ff5c5c;pointer-events:none;will-change:transform';
  chart.over.appendChild(cursor);

  const intervals = [], work = [];
  let last = -1, start = -1, position = 0;
  const frame = (timestamp) => {
    const t0 = performance.now();
    position = (position + 2) % points;
    cursor.style.transform = `translateX(${chart.valToPos(position, 'x')}px)`;
    work.push(performance.now() - t0);
    if (last >= 0) intervals.push(timestamp - last); else start = timestamp;
    last = timestamp;
    if (timestamp - start < 10000) { requestAnimationFrame(frame); return; }
    const sorted = (a) => [...a].sort((p, q) => p - q);
    const at = (a, q) => a[Math.min(a.length - 1, Math.floor(q * a.length))];
    const i = sorted(intervals), w = sorted(work);
    log(`cursor: ${i.length} frames in 10 s = ${(i.length / 10).toFixed(1)} fps, ${i.filter((v) => v > 20).length} over 20 ms`);
    log(`frame ms   p50 ${at(i, 0.5).toFixed(2)}  p95 ${at(i, 0.95).toFixed(2)}  max ${i[i.length - 1].toFixed(2)}`);
    log(`cursor ms  p50 ${at(w, 0.5).toFixed(3)}  p95 ${at(w, 0.95).toFixed(3)}  max ${w[w.length - 1].toFixed(3)}`);
    log(`${navigator.userAgent}, devicePixelRatio ${devicePixelRatio}`);
    console.info(out.textContent);
  };
  requestAnimationFrame(frame);
}
