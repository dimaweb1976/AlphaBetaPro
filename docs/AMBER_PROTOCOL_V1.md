# AMBER / MCA-527 protocol contract v1

Status: confirmed against `AP.exe`, Amber2Emulator v1.0.1 and the two-channel
end-to-end reference run. This document records only the subset required by the
first AlphaBetaPro client. Unknown fields are not assigned speculative meaning.

## Transport

- TCP, default port `50000`.
- One request produces one response; the TCP stream may split or join packets,
  therefore the client reads the exact response length.
- AMBER tunnel route `0x90` selects tract 1; `0x91` selects tract 2.
- All multibyte numeric values confirmed here are little-endian.

## Tunnel request, 16 bytes

| Offset | Size | Meaning |
|---:|---:|---|
| 0 | 1 | `0xC0` tunnel marker |
| 1 | 1 | route: `0x90` or `0x91` |
| 2 | 2 | MCA preamble `A5 5A` |
| 4 | 2 | MCA command, `uint16 LE` |
| 6 | 6 | command parameters |
| 12 | 2 | MCA end marker `B9 9B` |
| 14 | 1 | CRC-8/MAXIM over bytes 1..13 |
| 15 | 1 | `0x0C` tunnel terminator |

CRC-8/MAXIM uses initial value 0 and reflected polynomial `0x8C`.

Captured test vector for `QUERY_STATE`, tract 1:

```text
C0 90 A5 5A 5A 00 00 00 00 00 00 00 B9 9B 6C 0C
```

## Tunnel response

Normal response size is 140 bytes. `QUERY_SPECTRA_EX2` response size is 1044
bytes. Both use the following outer framing:

| Offset | Size | Meaning |
|---:|---:|---|
| 0 | 1 | `0xC0` |
| 1 | 1 | echoed route |
| 2 | variable | MCA response beginning with `A5 5A` |
| N-4 | 2 | MCA end flag |
| N-2 | 1 | CRC-8/MAXIM over bytes 1..N-3 |
| N-1 | 1 | `0x0C` |

Confirmed MCA end flags (`uint16 LE`):

| Bytes | Meaning |
|---|---|
| `B9 9B` | success |
| `A4 AA` | timeout |
| `A6 AA` | invalid frame |
| `AA AA` | invalid parameter |
| `AB AA` | unknown command |
| `AC AA` | measurement already running / forbidden while running |

## MVP command set

| Code | Name | Parameters used by v1 client | Response |
|---:|---|---|---|
| `0x0042` | START | `mode:uint16=1`, remaining 4 bytes zero | 140 bytes |
| `0x0043` | STOP | six zero bytes | 140 bytes |
| `0x0044` | CLEAR | six zero bytes | 140 bytes |
| `0x005A` | QUERY_STATE | six zero bytes | 140 bytes |
| `0x0062` | QUERY_SYSTEM_DATA | six zero bytes | 140 bytes |
| `0x0110` | QUERY_STATE527_EX | six zero bytes | 140 bytes |
| `0x0138` | QUERY_SPECTRA_EX2 | `first:uint16`, `compress:uint16=1`, `buffer:uint16=0` | 1044 bytes |

Additional observed commands are enumerated in the C# `McaCommand` enum, but
their result fields are not yet part of the stable AlphaBetaPro API.

## Measurement time

The following fields are documented by GBS Elektronik and confirmed against the
emulator/AlphaPRO reference timing:

- `QUERY_STATE`, result offset 20: integer real time in seconds (`uint32 LE`);
- `QUERY_STATE`, result offset 28: accumulated dead time in milliseconds
  (`uint32 LE`);
- `QUERY_STATE527_EX`, result offset 82: fractional real-time milliseconds
  (`uint16 LE`).

AlphaBetaPro calculates:

```text
real_time_ms = real_time_seconds * 1000 + fractional_ms
live_time_ms = max(0, real_time_ms - dead_time_ms)
```

Primary specification: [GBS Elektronik — Description of the MCA527 Firmware
Commands](https://www.gbs-elektronik.de/media/download_gallery/MCA527_Firmware_Commands_2025_09_22.pdf).

## `QUERY_SPECTRA_EX2` result

- One response contains exactly 256 counters.
- Counters start at tunnel response offset 4.
- Counter type is `uint32 little-endian`.
- Data size is 1024 bytes.
- `first` is the absolute first channel of the requested block.
- A 1024-channel spectrum is read with offsets 0, 256, 512 and 768.
- AP.exe and the reference `.asw` comparison confirmed exact equality for both
  tracts.

The remaining result area contains buffer state, echoed command fields and an
internal MCA checksum. The current C# boundary validates tunnel framing, route,
end flag and CRC-8. Validation and exposure of the internal checksum is a v2.1
follow-up before connection to physical Amber hardware.

## Known limits and next captures

- Physical Amber must confirm timeout/reconnect behavior and fragmented writes.
- Mapping of fields not exposed by `McaStateSnapshot` remains incomplete.
- Controller commands `0x60`-`0x63`, `0x6F`, PZC and Additional settings remain
  outside the first spectrum-client API.
- Reading a final partial block is implemented by reading 256 and cropping in
  the client; hardware behavior for a request beyond its configured resolution
  still needs a dedicated capture.
