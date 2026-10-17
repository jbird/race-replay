# ADR-0004: Custom structure-of-arrays binary bundle format

- **Status:** Accepted. Amended by two addenda: packages (2026-10-12, with ADR-0008), and compression and encodings (2026-10-17, spike S2).
- **Date:** 2026-10-12
- **Deciders:** Project maintainer

## Context

A replay holds about 28,800 time steps × 20 cars × (x, y, progress, validity), plus gap series. It must:

- download fast (NFR-03, NFR-04: ≤ 6 MB raw, ≤ 3 MB over the wire);
- load into typed arrays without heavy parsing in WebAssembly;
- be deterministic (NFR-11) and versioned;
- be readable with no third-party dependency in the browser.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **Custom SoA binary (`.rrb`) + JSON manifest** | Smallest raw size with int16 quantisation; zero-copy `MemoryMarshal.Cast` into spans; trivial reader; no dependencies; manifest stays human-readable | Own spec to maintain; needs golden-file tests |
| JSON arrays | Human-readable; no custom reader | 5–10× larger; slow to parse in WebAssembly; float formatting non-trivial for determinism |
| Protocol Buffers / MessagePack | Schema tooling; compact | Row-oriented by default; extra dependency in the browser; still needs a custom layout for columnar efficiency |
| Apache Parquet / Arrow IPC | Columnar, standard, excellent compression | Heavy libraries; overkill for a fixed, small schema; trimming and WASM friendliness uncertain |

## Decision

Use a **JSON manifest** (metadata, track, drivers, events, channel index) plus **binary `.rrb` channel files**:

- 64-byte header plus a directory of channel blocks;
- little-endian;
- 8-byte aligned blocks;
- X/Y as int16 quantised; progress and gaps as float32; validity as a bitmap.

The full specification is in [04 §7](../04-data-pipeline-and-bundle-format.md#7-bundle-format-schema-10).

**Addendum (2026-10-12, with ADR-0008).**

- The same files are also distributed as a single **`.rrz` package**: a ZIP with stored entries, allow-listed names and size caps. This is what users open from disk.
- The manifest and catalogue are described by **JSON Schemas generated from the DTOs** with `JsonSchemaExporter`. They are committed and golden-tested, so every format change is a reviewed diff ([04 §10](../04-data-pipeline-and-bundle-format.md#10-versioning-and-compatibility)).

**Addendum (2026-10-17, spike S2).** Measured in [spike S2](../spikes/S2.md) on a 2-hour synthetic race of 20 cars.

- **Hosted bundles are plain `.rrb` files with delta encodings, which GitHub Pages gzips** (option D, [04 §9](../04-data-pipeline-and-bundle-format.md#9-compression-and-hosting-spike-s2)).
  - Pages gzips every response, much like zlib at level 5, and never sends Brotli. Today's encodings hardly compress: a 2-hour `positions.rrb` crosses the wire as 3.97 MB of 4.61 MB, which misses NFR-04's 3 MB.
  - With the delta encodings below, it crosses as 1.93 MB. Progress does most of the work, because Float32 mantissas hardly compress.
  - The client needs no decompression code. The browser inflates the response, and the reader adds running sums (about 10 ms) to the pass that already dequantises.
  - Pre-gzipped files (options B and C) would be 1–8 % smaller and need an inflate step in the client, short of the 15 % gain set as the bar in #15.
- **The delta encodings are part of schema 1.0**, fixed in M4 before the golden bytes ([04 §7.3](../04-data-pipeline-and-bundle-format.md#73-binary-container-rrb)):
  - **encoding 5, Int16 quantised delta,** for X and Y: each value is the difference from the previous sample, wrapping in 16 bits;
  - **encoding 4, Int32 units delta,** for progress: whole track units (0.1 m), each the difference from the previous sample;
  - invalid samples hold the last valid value, so they encode as zero.

  They change what channel bytes mean, so they couldn't be added later as a minor version ([04 §10](../04-data-pipeline-and-bundle-format.md#10-versioning-and-compatibility)). They are encoding IDs rather than header flag bit 0, so each block says how to read it, and the flag stays reserved. This replaces the follow-up below that planned delta encoding as flag bit 0 in schema 1.1.
- **Package entries are deflated.** With the delta encodings, deflate shrinks a package by about 61 %, against under 15 % for today's encodings. Inflating costs 33–72 ms cold in the browser, well inside NFR-03's 1 s. The reader accepts stored entries too, since `ZipArchive` reads both with the same code. This replaces the stored entries of the addendum above.

## Consequences

- **Positive:**
  - About 5.3 MB raw for a two-hour race.
  - Loading is a few `Cast` calls plus one dequantise pass.
  - Adding a channel (for example tyre compound) does not disturb existing ones.
- **Negative:**
  - The format must be specified, versioned and guarded by golden-file and malformed-input tests.
- **Follow-up:**
  - S2 decides between raw, gzip, and delta + gzip storage.
  - Delta encoding is reserved as header flag bit 0 (schema 1.1).

## Revisit when

- Bundles need random access to time ranges (chunking).
- Or a standard columnar format becomes lightweight enough for WebAssembly.
