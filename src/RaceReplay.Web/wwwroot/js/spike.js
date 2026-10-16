// Spike S2 (throwaway, branch spike/s2-browser): timing and memory helpers for /spike/package and /spike/decode.

let changedAt = 0;
let held = null;

// The moment the user picks a file: a capturing listener sees the input's change event before Blazor does.
export function watchFileInput(inputId) {
  const input = document.getElementById(inputId);
  input?.addEventListener('change', () => { changedAt = performance.now(); }, { capture: true });
}

export function changedAtMs() {
  return changedAt;
}

export function nowMs() {
  return performance.now();
}

// WebAssembly linear memory in bytes. It only ever grows, so after a load it is the peak.
export function wasmMemoryBytes() {
  const runtime = globalThis.getDotnetRuntime?.(0);
  const heap = runtime?.localHeapViewU8?.() ?? runtime?.Module?.HEAPU8;
  return heap ? heap.buffer.byteLength : -1;
}

// A hidden pane pauses requestAnimationFrame, so drive the first frame through the exported OnFrame, as the loop's
// first tick would (spike S0 did the same). Returns when it was drawn; a real loop adds up to one vsync on top.
export async function driveFirstFrame() {
  if (!(globalThis.__s2FirstDraw > 0)) {
    const exports = await globalThis.getDotnetRuntime(0).getAssemblyExports('RaceReplay.Web');
    if (!(globalThis.__s2FirstDraw > 0)) exports.RaceReplay.Web.Interop.RenderLoopBridge.OnFrame(performance.now());
  }
  return globalThis.__s2FirstDraw || -1;
}

// The fallback hand-over: read the chosen file in JS, then .NET takes the bytes in one copy.
export async function readChosenFile(inputId) {
  const file = document.getElementById(inputId).files[0];
  held = new Uint8Array(await file.arrayBuffer());
  return held.length;
}

export function takeHeld() {
  const bytes = held;
  held = null;
  return bytes;
}

// Fetch a file into JS memory and keep it, so decompression can be timed without the network.
export async function fetchHeld(url) {
  held = new Uint8Array(await (await fetch(url, { cache: 'no-store' })).arrayBuffer());
  return held.length;
}

// Decompresses the held gzip bytes with the browser's native DecompressionStream; the result replaces them.
export async function gunzipHeld() {
  const begin = performance.now();
  const stream = new Blob([held]).stream().pipeThrough(new DecompressionStream('gzip'));
  held = new Uint8Array(await new Response(stream).arrayBuffer());
  return performance.now() - begin;
}

export function log(text) {
  console.info(`${text} | ${navigator.userAgent}`);
}
