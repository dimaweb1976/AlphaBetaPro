# AlphaBetaPro v2.1 status

Date: 2026-10-05

## Completed

- protocol contract limited to observed and tested facts;
- AMBER tunnel routes `0x90` and `0x91`;
- MCA-527 request framing and CRC-8/MAXIM;
- response framing and end flags;
- asynchronous TCP client with serialized request/response exchanges;
- `CLEAR`, `START`, `STOP`, `QUERY_STATE`;
- `QUERY_SPECTRA_EX2` as 256 × `uint32 little-endian`;
- full configurable spectrum read by consecutive blocks;
- diagnostic `read` and `smoke` commands;
- dependency-free offline vector runner;
- network test against both Amber2Emulator tracts.
- typed state: run state, resolution, LLD/ULD, serial, count rate and bias;
- real time from `QUERY_STATE` plus fractional milliseconds from
  `QUERY_STATE527_EX`;
- live time calculated from documented accumulated dead time;
- operation timeout resets a failed connection;
- explicit disconnect and reconnect on the same client instance;
- `AlphaProGoldenSpectrum-1` validation and channel-by-channel comparison.

## Verification result

Environment: .NET SDK 10.0.401.

```text
Build succeeded: 0 warnings, 0 errors
PASS captured CRC tract 1
PASS captured CRC tract 2
PASS QUERY_STATE request vector
PASS QUERY_SPECTRA_EX2 decoding
PASS network tract 1
PASS network tract 2
PASS network reconnect
```

Exact reference measurements completed with `PASS` for both tracts: 1024
channels, zero differences, matching totals and matching SHA-256. Machine-readable
results are stored under `artifacts/v2.1` (local test output, excluded from Git).

## v2.1 acceptance result

All planned software-only gates are complete. Validation against physical Amber
hardware is intentionally separate because it requires the instrument.

The next product stage is the WPF diagnostic measurement shell. Physical Amber
timeout/reconnect behavior remains a hardware validation task.
