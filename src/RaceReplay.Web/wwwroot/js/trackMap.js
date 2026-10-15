// Track map on two stacked canvases (docs/05 §6). The static layer (track, start/finish line) is drawn on resize;
// the car layer whenever .NET sends a frame, and again after a resize. State is limited to the canvases, the scene,
// the view transform and the last frame.

const carRadius = 6;    // CSS px
const labelFont = 11;   // CSS px
const margin = 28;      // CSS px around the track

let host = null;
let trackCanvas = null;
let carCanvas = null;
let observer = null;
let scene = null;
let view = null;
let frame = new Float64Array(0); // the last frame drawn, kept to repaint after a resize
let stride = 0;
let dprQuery = null;
let measurePaint = false; // ?perf: renderLoop.js separates canvas time from .NET time
let paintMs = 0;
const placed = [];      // label boxes placed this frame: [x, y, w, h] flattened
let labelX = 0, labelY = 0;

export function init(hostId, centreline, rotationDegrees, acronyms, colours, outlines, dashed) {
  clear();
  host = document.getElementById(hostId);
  if (!host) throw new Error(`Track map host '${hostId}' not found.`);

  trackCanvas = addCanvas('track-layer');
  carCanvas = addCanvas('car-layer');
  const radians = rotationDegrees * Math.PI / 180;
  scene = {
    points: Float64Array.from(centreline),
    cos: Math.cos(radians),
    sin: Math.sin(radians),
    acronyms, colours, outlines,
    dashed: dashed.map(d => d === 1),
    labelWidths: new Float64Array(acronyms.length),
  };
  frame = new Float64Array(0);
  stride = 0;
  measurePaint = new URLSearchParams(location.search).has('perf');
  observer = new ResizeObserver(resize);
  observer.observe(host);
  watchDevicePixelRatio();
  resize();
}

// frameView is a MemoryView over .NET memory, valid only during this call: copy it, never keep it.
export function drawCars(frameView, frameStride) {
  if (!scene) return;
  if (frame.length !== frameView.length) frame = new Float64Array(frameView.length);
  frameView.copyTo(frame);
  stride = frameStride;
  if (measurePaint) {
    const begin = performance.now();
    paint();
    paintMs += performance.now() - begin;
  } else {
    paint();
  }
}

// ?perf: canvas time spent drawing cars since the last call.
export function takePaintMs() {
  const ms = paintMs;
  paintMs = 0;
  return ms;
}

export function clear() {
  observer?.disconnect();
  dprQuery?.removeEventListener('change', onDevicePixelRatioChange);
  trackCanvas?.remove();
  carCanvas?.remove();
  host = trackCanvas = carCanvas = observer = dprQuery = scene = view = null;
}

// ResizeObserver misses a devicePixelRatio change without a CSS size change, e.g. moving to another monitor.
function watchDevicePixelRatio() {
  dprQuery?.removeEventListener('change', onDevicePixelRatioChange);
  dprQuery = matchMedia(`(resolution: ${window.devicePixelRatio}dppx)`);
  dprQuery.addEventListener('change', onDevicePixelRatioChange);
}

function onDevicePixelRatioChange() {
  watchDevicePixelRatio();
  resize();
}

// Draws the cars from the last frame received.
function paint() {
  if (!scene || !view || stride === 0) return;
  const ctx = carCanvas.getContext('2d');
  const { a, b, c, d, e, f, dpr, dash, solid, labelHalo, labelColour } = view;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, carCanvas.width, carCanvas.height);
  ctx.font = view.font;
  ctx.textBaseline = 'middle';
  ctx.lineJoin = 'round';
  placed.length = 0;

  const r = carRadius * dpr;
  for (let i = 0, o = 0; i < scene.acronyms.length && o + 2 < frame.length; i++, o += stride) {
    if (frame[o + 2] === 0) continue; // not visible
    const x = frame[o], y = frame[o + 1];
    const sx = a * x + c * y + e;
    const sy = b * x + d * y + f;

    ctx.beginPath();
    ctx.arc(sx, sy, r, 0, 2 * Math.PI);
    ctx.fillStyle = scene.colours[i];
    ctx.fill();
    ctx.setLineDash(scene.dashed[i] ? dash : solid);
    ctx.lineWidth = 2 * dpr;
    ctx.strokeStyle = scene.outlines[i];
    ctx.stroke();
    ctx.setLineDash(solid);

    placeLabel(sx, sy, r, scene.labelWidths[i], labelFont * dpr, dpr);
    ctx.lineWidth = 3 * dpr;
    ctx.strokeStyle = labelHalo;
    ctx.strokeText(scene.acronyms[i], labelX, labelY);
    ctx.fillStyle = labelColour;
    ctx.fillText(scene.acronyms[i], labelX, labelY);
  }
}

function addCanvas(className) {
  const canvas = document.createElement('canvas');
  canvas.className = className;
  canvas.setAttribute('aria-hidden', 'true');
  host.appendChild(canvas);
  return canvas;
}

function resize() {
  if (!scene) return;
  const dpr = window.devicePixelRatio || 1;
  const width = Math.max(1, Math.round(host.clientWidth * dpr));
  const height = Math.max(1, Math.round(host.clientHeight * dpr));
  for (const canvas of [trackCanvas, carCanvas]) {
    canvas.width = width;
    canvas.height = height;
  }

  const style = getComputedStyle(host);
  view = {
    ...fit(width, height, margin * dpr),
    dpr,
    font: `600 ${labelFont * dpr}px system-ui, sans-serif`,
    dash: [3 * dpr, 2 * dpr],
    solid: [],
    labelHalo: style.getPropertyValue('--map-label-halo').trim() || '#000',
    labelColour: style.getPropertyValue('--map-label').trim() || '#fff',
  };

  const ctx = carCanvas.getContext('2d');
  ctx.font = view.font;
  scene.acronyms.forEach((acronym, i) => { scene.labelWidths[i] = ctx.measureText(acronym).width; });
  drawTrack(style);
  paint(); // resizing cleared the car layer, and .NET only sends frames when the position changes
}

// Rotate, flip Y (track is Y-up, canvas Y-down) and scale the rotated bounds into the canvas, centred.
// screen = (a·x + c·y + e, b·x + d·y + f)
function fit(width, height, inset) {
  const { points, cos, sin } = scene;
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (let i = 0; i < points.length; i += 2) {
    const rx = cos * points[i] - sin * points[i + 1];
    const ry = sin * points[i] + cos * points[i + 1];
    minX = Math.min(minX, rx); maxX = Math.max(maxX, rx);
    minY = Math.min(minY, ry); maxY = Math.max(maxY, ry);
  }

  const k = Math.max(0, Math.min(
    (width - 2 * inset) / Math.max(maxX - minX, 1e-9),
    (height - 2 * inset) / Math.max(maxY - minY, 1e-9)));
  const cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
  return {
    a: k * cos, c: -k * sin, e: width / 2 - k * cx,
    b: -k * sin, d: -k * cos, f: height / 2 + k * cy,
  };
}

function drawTrack(style) {
  const ctx = trackCanvas.getContext('2d');
  const { a, b, c, d, e, f, dpr } = view;
  const { points } = scene;
  const at = (i) => [a * points[i] + c * points[i + 1] + e, b * points[i] + d * points[i + 1] + f];

  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, trackCanvas.width, trackCanvas.height);
  ctx.beginPath();
  for (let i = 0; i < points.length; i += 2) {
    const [x, y] = at(i);
    if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
  }
  ctx.closePath();
  ctx.lineJoin = 'round';
  ctx.strokeStyle = style.getPropertyValue('--map-track-edge').trim() || '#888';
  ctx.lineWidth = 12 * dpr;
  ctx.stroke();
  ctx.strokeStyle = style.getPropertyValue('--map-track').trim() || '#444';
  ctx.lineWidth = 9 * dpr;
  ctx.stroke();

  // Start/finish: a line across the track at point 0, perpendicular to the direction of travel.
  const [x0, y0] = at(0);
  const [x1, y1] = at(2);
  const length = Math.hypot(x1 - x0, y1 - y0) || 1;
  const nx = -(y1 - y0) / length, ny = (x1 - x0) / length;
  const half = 9 * dpr;
  ctx.beginPath();
  ctx.moveTo(x0 + nx * half, y0 + ny * half);
  ctx.lineTo(x0 - nx * half, y0 - ny * half);
  ctx.strokeStyle = style.getPropertyValue('--map-start-finish').trim() || '#fff';
  ctx.lineWidth = 3 * dpr;
  ctx.stroke();
}

// Right of the car, else left, above or below: the first spot that doesn't overlap a label already placed.
// Sets labelX/labelY (left, middle) rather than returning an array, to keep the per-frame path allocation-free.
function placeLabel(sx, sy, r, width, height, dpr) {
  const gap = 3 * dpr;
  for (let k = 0; k < 4; k++) {
    labelX = k === 0 ? sx + r + gap : k === 1 ? sx - r - gap - width : sx - width / 2;
    labelY = k < 2 ? sy : k === 2 ? sy - r - gap - height / 2 : sy + r + gap + height / 2;
    if (!overlaps(labelX, labelY - height / 2, width, height)) break;
    if (k === 3) { labelX = sx + r + gap; labelY = sy; } // crowded: fall back to the right
  }
  placed.push(labelX, labelY - height / 2, width, height);
}

function overlaps(x, y, w, h) {
  for (let i = 0; i < placed.length; i += 4) {
    if (x < placed[i] + placed[i + 2] && placed[i] < x + w && y < placed[i + 1] + placed[i + 3] && placed[i + 1] < y + h) {
      return true;
    }
  }
  return false;
}
