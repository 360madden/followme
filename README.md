# FollowMe

[![.NET 9](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Lua 5.1](https://img.shields.io/badge/Lua-5.1-blue?logo=lua)](https://www.lua.org/)
[![RIFT MMO](https://img.shields.io/badge/RIFT-MMO-FF6B35)](https://www.riftgame.com/)
[![MIT License](https://img.shields.io/badge/License-MIT-green)](LICENSE)

FollowMe is a **reliability-first optical telemetry system** for RIFT multiboxing. It consists of a Lua addon running inside RIFT that encodes game state into a colored pixel strip at the top of the game window, and a C# desktop app that reads that strip optically, decodes it, and relays movement commands to follower clients.

## How It Works

```
[Leader RIFT Client]                    [Follower RIFT Client]
   ↓ Lua addon                             ↓ Lua addon
   ├─ Renders 640×24px color strip       ├─ Renders position frame
   └─ Encodes: position, health, target  └─ Reads own coordinates
         ↓ (optical pixels)                   ↓ (optical pixels)
   [FollowMe.Reader] ←── DesktopDuplication ──→ [FollowMe.Reader]
         ├─ Decodes color strip              ├─ Decodes position
         └─ Builds LeaderState               └─ Builds FollowerState
              ↓ TCP (port 7742)
         [TcpLeaderBroadcast] ────→ [TcpFollowerReceive]
                                         ├─ Win32MovementController (WASD + mouse)
                                         └─ Win32TargetController (/assist)
```

**Key insight:** The color strip is the only channel between leader and follower. No TCP without first proving the optical decode works locally. This ensures the system is predictable and observable.

## Features

- **Optical telemetry bridge**: Position, health, resource, target state, and stats encoded as 8-color pixels
- **Multibox support**: Leader broadcasts position via TCP relay; follower drives movement + target assist
- **External control only**: Follower movement is driven by Win32 input from the C# app — no ingame macro or chat commands needed
- **Reliability-first design**: CRC-16 header + CRC-32C payload; explicit reject reasons for every failed decode
- **Live diagnostics**: Frame inspector, capture replay, synthetic smoke tests, and performance bench

## Requirements

- **RIFT game client** (640×360 or higher window)
- **Windows 10 or 11**
- **.NET 9 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/9.0))

## Quick Start

### Build

```powershell
cd followme-dev
dotnet build
```

### Install RIFT Addon

Copy the addon folder to RIFT:

```powershell
Copy-Item -Recurse followme-dev -Destination 'C:\Program Files (x86)\RIFT\Interface\AddOns\FollowMe' -Force
```

Reload the addon ingame:
```
/reloadui
```

Or run the reload script:
```powershell
scripts\Reload-RiftUi.cmd
```

### Run Tests & Smoke Tests

```powershell
# Unit tests
dotnet test

# Smoke: generate synthetic color-strip fixture and validate decode
dotnet run --project DesktopDotNet\FollowMe.Cli -- smoke

# Replay: test decode against a saved BMP
dotnet run --project DesktopDotNet\FollowMe.Cli -- replay .\fixtures\followme-color-core.bmp
```

### Live Capture & Decode

Prepare RIFT window (640×360):
```powershell
dotnet run --project DesktopDotNet\FollowMe.Cli -- prepare-window 32 32
```

Capture and save live telemetry:
```powershell
dotnet run --project DesktopDotNet\FollowMe.Cli -- capture-dump --backend desktopdup
```

Run live decode loop (5 seconds, 100ms sample interval):
```powershell
dotnet run --project DesktopDotNet\FollowMe.Cli -- live 5 100 --backend desktopdup
```

### HUD & Inspector

**Live stats HUD** (shows telemetry, multibox status):
```powershell
dotnet run --project DesktopDotNet\FollowMe.Hud
```

**Frame inspector** (visual debug, segment overlays, bit layouts):
```powershell
dotnet run --project DesktopDotNet\FollowMe.Inspector
```

### Multiboxing (Leader)

```powershell
dotnet run --project DesktopDotNet\FollowMe.Cli -- multibox-leader
```

### Multiboxing (Follower)

On the follower machine, edit `multibox.json`:
```json
{
  "Mode": "Follower",
  "TcpPort": 7742,
  "LeaderHost": "192.168.1.100",
  "LeaderName": "LeaderCharName",
  "FollowDistance": 8.0,
  "StopDistance": 3.5
}
```

Then run:
```powershell
dotnet run --project DesktopDotNet\FollowMe.Cli -- multibox-follower
```

## Frame Types

The color strip encodes up to 8 frame types. Each frame is **24 transport bytes**:

| Frame Type | ID | Purpose | Payload |
|------------|----|---------|---------|
| **CoreStatus** | 1 | Heartbeat | Player + target health, resource, flags |
| **PlayerStatsPage** | 2 | Rotating stats | Vitals, armor, resistances, etc. (5 schemas) |
| **PlayerPosition** | 3 | Leader position | x/y/z world coordinates (int32 × 100 scale) |
| **MultiBoxState** | 4 | Combat state | In-combat flag, hostile target name (10 chars) |

### Protocol Details

Each frame carries:
```
Bytes 1–2:   Magic "CL"
Byte 3:      Protocol/profile (0x01)
Byte 4:      Frame type + schema
Byte 5:      Sequence number
Byte 6–7:    Reserved + flags
Byte 8–9:    CRC-16 (header)
Byte 10–21:  Payload (12 bytes, varies by type)
Byte 22–25:  CRC-32C (payload)
```

**Transport:** 80 color segments (8px × 24px each) encode 64 payload symbols + 8-symbol control strip on each edge. 3 bits per color = 8-color alphabet (R, G, B, Mg, Cy, Ye, W, K).

## Project Structure

```
followme-dev/
├── Core/                     # Lua addon (RIFT side)
│   ├── Config.lua            # Profile, frame type definitions
│   ├── Protocol.lua          # Frame encoding/decoding
│   ├── Gather.lua            # Player state inspection
│   ├── MultiBox.lua          # Position + assist frame building
│   └── ...
├── DesktopDotNet/            # C# solution (.NET 9)
│   ├── FollowMe.Reader/      # Core: frame decode, telemetry aggregate
│   ├── FollowMe.Cli/         # Commands: smoke, replay, live, bench, multibox-leader/follower
│   ├── FollowMe.MultiBox/    # TCP relay, Win32 input, state machines
│   ├── FollowMe.Hud/         # WinForms live stats display
│   ├── FollowMe.Inspector/   # Diagnostic frame viewer
│   ├── FollowMe.Tests/       # xUnit protocol + integration tests
│   └── FollowMe.sln
├── fixtures/                 # Test data (BMP captures)
├── scripts/                  # Helper batch files
├── README.md                 # This file
├── CHANGELOG.md              # Version history
├── ARCHITECTURE.md           # Deep dive: protocol, TCP relay, Win32 control
├── CONTRIBUTING.md           # Dev setup, coding rules
└── LICENSE                   # MIT
```

## Outputs

Reader artifacts are written to `AppData\Local\FollowMe\DesktopDotNet\`:

- `fixtures/followme-color-core.bmp` — Synthetic smoke-test fixture
- `out/followme-color-capture-dump.bmp` — Live capture (raw)
- `out/followme-color-capture-dump-annotated.bmp` — Live capture (with segment overlays)
- `out/followme-color-capture-dump.json` — Decode metadata (offsets, CRC, symbol table)
- `out/followme-color-first-reject.bmp` — First rejected frame (for debugging)

## Validation

After any Lua addon change, reload ingame:
```
/reloadui
```

Or run tests:
```powershell
dotnet test
dotnet run --project DesktopDotNet\FollowMe.Cli -- smoke
```

Live capture remains the gold standard. If `capture-dump` or `live` produces rejects with a clear reason, the system is working as designed.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, coding conventions, and issue/PR guidelines.

## License

MIT — see [LICENSE](LICENSE) file.

---

**FollowMe | v0.1.0** — Optical telemetry bridge for RIFT multiboxing.
