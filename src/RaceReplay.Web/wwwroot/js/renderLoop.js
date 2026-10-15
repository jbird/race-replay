// The requestAnimationFrame loop (docs/05 §5). Each frame makes one call into .NET, which advances the clock,
// samples the cars and draws them. No other state.

const assemblyName = 'RaceReplay.Web';
let generation = 0;
let handle = 0;
let onFrame = null;

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
  frameCallback().then(
    (callback) => {
      if (current !== generation) return; // stopped while the exports were resolving
      const tick = (timestamp) => {
        if (current !== generation) return;
        callback(timestamp);
        handle = requestAnimationFrame(tick);
      };
      handle = requestAnimationFrame(tick);
    },
    (error) => console.error('Race Replay: the render loop could not start.', error));
}

export function stop() {
  generation++;
  cancelAnimationFrame(handle);
  handle = 0;
}
