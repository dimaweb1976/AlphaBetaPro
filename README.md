# AlphaBetaPro — AMBER integration bootstrap

This directory contains the first C# boundary extracted from the confirmed
AlphaPRO/Amber2Emulator exchange. It intentionally has no WPF or analysis logic
yet: protocol correctness is tested before UI and calculations are added.

## Projects

- `AlphaBetaPro.AmberProtocol` — framing, CRC, TCP exchange and block spectrum read;
- `AlphaBetaPro.AmberProbe` — CLI for a real or emulated AMBER endpoint;
- `AlphaBetaPro.AmberProtocol.Tests` — dependency-free byte-vector and optional
  network tests;
- `docs/AMBER_PROTOCOL_V1.md` — confirmed protocol contract and open questions.

## Prerequisite

.NET 10 SDK. The reference workstation was verified with SDK `10.0.401`.

## Build and offline vectors

```powershell
dotnet build .\AlphaBetaPro.sln -c Release
dotnet run --project .\tests\AlphaBetaPro.AmberProtocol.Tests -c Release
```

## Test against Amber2Emulator

Start the emulator with `config.regression-1024.json`, then run:

```powershell
dotnet run --project .\tests\AlphaBetaPro.AmberProtocol.Tests -c Release -- `
  --network --host 127.0.0.1 --port 50000

dotnet run --project .\src\AlphaBetaPro.AmberProbe -c Release -- `
  smoke --host 127.0.0.1 --port 50000 --tract 1 --channels 1024 `
  --duration 2 --output .\artifacts\tract1-smoke.json
```

Repeat `smoke` with `--tract 2`. The command performs `CLEAR`, `START`, waits,
`STOP`, reads four 256-counter blocks and writes the complete spectrum to JSON.

## v2.1 acceptance criterion

Both network tests must pass against Amber2Emulator, and the JSON arrays read by
the C# probe must match the emulator/reference arrays channel by channel. Only
after this gate should the WPF measurement screen consume the library.

Current status: v2.1 software acceptance is complete. The solution builds with
zero warnings and errors; byte vectors, timeout and reconnect tests pass; both
network tracts pass; and both 1024-channel spectra exactly match the v1 golden
arrays, totals and SHA-256 values. The next product stage is the WPF diagnostic
measurement shell.
