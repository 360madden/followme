# Changelog

All notable changes to FollowMe are documented here.

## [0.1.0] - 2026-03-30

### Added

- **Optical telemetry bridge**: Lua addon renders 640×24px color strip encoding player state (health, resource, target, stats)
- **Frame decoder (C#)**: Color-to-byte conversion, CRC-16 (header) + CRC-32C (payload) validation, frame type dispatch
- **Frame types**:
  - `CoreStatus` (type 1): Player + target health, resource, state flags, level
  - `PlayerStatsPage` (type 2, 5 schemas): Vitals, armor, offense, defense, resistances
  - `PlayerPosition` (type 3): World coordinates (x/y/z, int32 × 100 scale)
  - `MultiBoxState` (type 4): Combat state, hostile target name
- **Lua addon modules**:
  - `Config.lua`: Profile definitions (P360C, 640×360 window, 80 segments, 8px each)
  - `Protocol.lua`: Frame encoding with CRC, symbol alphabet
  - `Gather.lua`: Player state inspection via RIFT Inspect API
  - `MultiBox.lua`: Position + assist frame building (leader mode)
  - `Bootstrap.lua`: Main loop, frame rotation (CoreStatus + Stats + Position + MultiBox)
  - `Diagnostics.lua`: Frame history, rejection tracking
  - `ErrorTrap.lua`: Protected calls for addon stability

- **C# solution (.NET 9)**:
  - `FollowMe.Reader`: Frame decoder, telemetry aggregate (CoreStatus, PlayerStatsPage, Position, MultiBox snapshots)
  - `FollowMe.Cli`: Commands: `smoke` (synthetic test), `replay` (BMP playback), `live` (real-time capture), `bench` (performance), `capture-dump` (save artifacts), `prepare-window`, `multibox-leader`, `multibox-follower`
  - `FollowMe.MultiBox`: TCP relay, state machines, Win32 input controllers
    - `TcpLeaderBroadcast`: Leader broadcasts `LeaderState` (position + combat info) to follower
    - `TcpFollowerReceive`: Follower receives and deserializes
    - `Win32MovementController`: WASD forward + mouse yaw steering
    - `Win32TargetController`: Debounced `/assist [LeaderName]` injection
  - `FollowMe.Hud`: Windows Forms live telemetry display (health bars, stats pages, multibox status panel)
  - `FollowMe.Inspector`: Diagnostic frame viewer with segment overlays and bit layout
  - `FollowMe.Tests`: xUnit protocol tests (frame round-trip, CRC validation, position encoding, state snapshots)

- **Capture backends**: DesktopDuplication (preferred), ScreenBitBlt (fallback), PrintWindow (debug-only)

- **Diagnostics & observability**:
  - Per-frame decode metadata (offset, pitch, scale, symbol errors)
  - BMP export: raw capture + annotated (segment boxes, symbol grid)
  - JSON sidecar: frame type, CRC pass/fail, latency histogram
  - Explicit reject reasons (bad magic, CRC fail, out-of-bounds, etc.)

- **Testing infrastructure**:
  - Smoke: synthetic color-strip generation with known-good frames
  - Replay: read BMP fixture and decode as if live
  - Bench: position offset, blur, brightness, gamma, scale drift robustness
  - Unit tests: frame protocol round-trip, CRC, position encoding (negative coords, int32 bounds)

- **Wrapper scripts** (`scripts/`):
  - `Prepare-FollowMe-640x360.cmd`: Window layout helper
  - `Smoke-FollowMe.cmd`, `Bench-FollowMe.cmd`, `Live-FollowMe.cmd`: Test automation
  - `Open-FollowMe-Inspector.cmd`, `Open-FollowMe-Hud.cmd`: Tool launchers
  - `Reload-RiftUi.cmd`: Addon reload without client restart
  - `Resize-RiftClient-640x360.cmd`: Window sizing

- **Documentation**:
  - `README.md`: Overview, quick start, usage examples
  - `ARCHITECTURE.md`: Protocol spec, frame layouts, TCP relay design, Win32 control flow
  - `CONTRIBUTING.md`: Dev setup, coding rules (Lua 5.1 + RIFT API, .NET 9 nullable)
  - `LICENSE`: MIT
  - `CHANGELOG.md` (this file)

### Profile & Configuration

- **Profile**: P360C (640×360 window, fixed layout)
- **Strip layout**: 640×24px, 80 segments × 8px each
- **Color alphabet**: 8 colors (R, G, B, Magenta, Cyan, Yellow, White, Black) → 3 bits per symbol
- **Frame transport**: 80 segments = 8 control (left) + 64 payload + 8 control (right)
- **Payload symbols**: 64 symbols → 24 transport bytes (header 8 + payload 12 + CRC 4)
- **TCP relay**: Port 7742 (configurable), leader-only broadcast, one follower at a time

### Known Limitations

- v0.1.0 targets one follower per leader (multibox-follower is 1:1 TCP)
- No pathfinding; movement is direct bearing + distance (works in open terrain)
- Stats refresh cached at 0.5s interval (not per-frame)
- Assisted target only fires `/assist` on hostile; no automatic ability sequencing

### Testing Status

- ✅ Smoke round-trip (synthetic fixture decode)
- ✅ Replay on known-good BMP fixture
- ✅ Bench offset/blur/brightness drift
- ✅ Live capture decode (DesktopDuplication)
- ✅ Protocol unit tests (frame encoding, CRC, position int32 bounds)
- ✅ `dotnet build` + `dotnet test` pass on .NET 9 SDK
- ⏳ Live multibox (leader + follower) integration test: in progress

---

## Future Releases

### [0.2.0] (planned)

- Multiple follower support (broadcast to N connected clients)
- Configurable capture intervals and frame rotation
- Extended stats pages (more defense/resistance detail)
- HUD themeable colors
- Multibox-aware addon menu (ingame mode selector)

### [0.3.0] (planned)

- Simple pathfinding (stay-behind, obstacle avoidance)
- Combat awareness (auto-stop in melee range)
- Ability sequencing (follow-up spells after leader cast)

### Beyond

- Cross-machine network (non-LAN relay)
- Linux/Mac support (cross-platform capture backends)
