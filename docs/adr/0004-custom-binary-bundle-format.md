# ADR-0004: Custom structure-of-arrays binary bundle format

- **Status:** Accepted. The compression variant is decided in spike S2 and recorded as an addendum.
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
