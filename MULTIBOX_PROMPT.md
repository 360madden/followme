# FollowMe Multiboxing — Claude Code Build Prompt
# Version: 2.0 | Reflects actual codebase state as of 2026-03-30

---

## Existing architecture (read-only — do not break)

### How FollowMe works today

**Telemetry bridge (the core channel):**
The Lua addon renders a 640×24px color band of 80 segments (8px each) at the top of the RIFT window. Each segment shows one of 8 colors (3 bits). The C# reader captures this band via DesktopDuplication or ScreenBitBlt, decodes 64 payload symbols + 8-symbol control strips on each side, recovers 24 transport bytes (8 header + 12 payload + 4 CRC), and validates with CRC-16 (header) and CRC-32C (payload).

**Existing frame types:** `CoreStatus` (1), `PlayerStatsPage` (2, schemas 1–5).
**Profile:** `P360C`, 640×360 window, 640×24 band, 80 segments, 8px each.
**Tick source:** `Event.System.Update.Begin` at ~20 Hz; throttled to 0.05s. Stats refreshed every 0.5s.

**C# solution projects:**
- `FollowMe.Reader` — core library: `FrameProtocol`, `TelemetryAggregate`, `ColorStripAnalyzer`, `WindowCaptureService`, `TelemetryHudSnapshot`
- `FollowMe.Cli` — CLI: commands `smoke`, `live`, `watch`, `bench`, `capture-dump`, `prepare-window`
- `FollowMe.Hud` — WinForms stats HUD (`HudForm`), polls telemetry at 50ms
- `FollowMe.Inspector` — separate inspector tool
- `FollowMe.Tests` — xUnit tests

**Lua modules:** `Config`, `Gather`, `Protocol`, `Render`, `Bootstrap`, `Diagnostics`, `ErrorTrap`

---

## Task: Add multiboxing as a new modular subsystem

### Goals
1. Leader RIFT client broadcasts position (x/y/z) + current target info via the existing screen-render channel (new frame types)
2. Leader C# app (already reading its own screen) relays `LeaderState` to follower C# apps via TCP on the LAN
3. Follower C# app receives `LeaderState`, reads its own position (from its own screen), and drives movement via Win32 SendInput (W/A/D + yaw) — works in AND out of combat, no `/follow` dependency
4. Follower C# app injects `/assist [leaderName]` keystrokes when leader's hostile target changes (debounced)
5. Full UI integration: extend CLI with multibox commands; add a standalone MultiBox HUD window

### Non-goals for v1
- Pathfinding around obstacles
- More than one follower
- Ability-cast sequencing beyond target assist
- In-game Lua UI (use C# HUD only)

---

## Architecture

```
[Leader machine]
  RIFT + FollowMe addon (Lua)
    ─ renders: CoreStatus, PlayerStats (existing)
    ─ renders: NEW PlayerPosition frame (frame type 3)
    ─ renders: NEW MultiBoxState frame  (frame type 4)
         │ (existing DesktopDuplication channel)
  FollowMe.Cli or FollowMe.MultiBoxHud (C#)
    ─ reads its own screen (existing TelemetryAggregate)
    ─ NEW: MultiBoxLeaderSession → TcpLeaderBroadcast
         │
         └─────── TCP (port 7742, configurable) ──────────►
                                                    [Follower machine]
                                                      RIFT + FollowMe addon (Lua)
                                                        ─ renders: PlayerPosition (own coords)
                                                      FollowMe.Cli or FollowMe.MultiBoxHud (C#)
                                                        ─ reads its own screen (existing)
                                                        ─ NEW: MultiBoxFollowerSession ← TcpFollowerReceive
                                                        ─ NEW: Win32MovementController (SendInput)
                                                        ─ NEW: TargetAssistController (inject keystrokes)
```

No new IPC needed on each machine — the existing screen-capture channel is already the Lua→C# bridge.

---

## Step 1: Research (MUST complete before writing any Lua)

Search `site:seebs.net rift live [name]` or fetch `rift.mestoph.net` to verify:

| # | Question | Why it matters |
|---|----------|---------------|
| 1 | Does `Inspect.Unit.Detail("player")` return fields named `x`, `y`, `z`? Or different names? Do they update while moving? | Position frame encoding |
| 2 | Is `Command.Slash(text)` the correct API to fire `/assist name` without appearing in public chat? Any silent alternative? | Target assist injection |
| 3 | Does RIFT have a private addon message channel (e.g. `Command.Dispatch`, `Command.Message.Channel`) that does NOT appear in any chat log? | Backup communication channel |
| 4 | What is the correct event for a reliable per-frame tick? (Bootstrap uses `Event.System.Update.Begin` — confirm this fires while moving/in combat) | Follow persistence |
| 5 | Does `Inspect.Unit.Detail("player")` expose a facing/yaw angle field? | Lets C# calculate bearing without heuristics |

Document findings as a comment block at the top of each new Lua file before writing any logic.

---

## Step 2: New Lua modules

### New frame types (extend `Protocol.lua` and `Config.lua`)

Add to `Config.lua` frameTypes table:
```lua
playerPosition = 3,
multiBoxState  = 4,
```

**Frame 3 — PlayerPosition** (schema 1, 12-byte payload):
```
bytes 1–4:  X as int32 big-endian (world coord × 100, clamped to int32)
bytes 5–8:  Y as int32 big-endian
bytes 9–12: Z as int32 big-endian
```
Use existing `AppendBigEndian32` + a new `ClampInt32` helper.
Lua has no int32 signed concept — represent negative coords as two's complement uint32 (subtract from 4294967296 if negative, same decode in C#).

**Frame 4 — MultiBoxState** (schema 1, 12-byte payload):
```
byte  1:    flags (bit0=inCombat, bit1=hasTarget, bit2=targetHostile)
byte  2:    targetNameLen (0–10, clamped)
bytes 3–12: targetName first 10 chars as ASCII byte values (0-padded)
```

### New file: `Core/MultiBox.lua`

Module: `FollowMe.MultiBox`

```lua
-- Responsibilities:
--   Leader mode: on each tick, call BuildPositionSnapshot() and BuildMultiBoxStateSnapshot(),
--                build frames 3 and 4 (interleaved with existing frames), update Bootstrap state
--   Follower mode: expose SetLeaderTarget(name) which calls Command.Slash("/assist " .. name)
--                  (or the verified silent alternative)
--
-- Mode is set via FollowMe.MultiBox.SetMode("leader"|"follower"|"off")
-- Default: "off" (no change to existing behavior)
```

Nil-safety: all `Inspect.*` calls must be wrapped in `pcall` (follow `SafeUnitDetail` pattern in `Gather.lua`).

### Extend `Bootstrap.lua`

In the `Refresh` function, when `FollowMe.MultiBox` mode is `"leader"`, interleave frames 3 and 4 into the rotation alongside existing CoreStatus/PlayerStats frames. Suggested rotation:
```
sequence mod 10:
  0,1,2,3 → CoreStatus
  4       → PlayerPosition (frame 3)
  5       → MultiBoxState  (frame 4)
  6,7     → PlayerStats pages (existing)
  8       → PlayerPosition again
  9       → CoreStatus
```

### File header convention (all new Lua files)
```lua
-- FollowMe.MultiBox | v0.1.0 | N chars
-- Research findings:
--   Inspect.Unit.Detail("player").x/.y/.z: [VERIFIED or UNVERIFIED — state result]
--   Command.Slash: [VERIFIED or UNVERIFIED — state result]
--   Private channel: [VERIFIED or UNVERIFIED — state result]
```

---

## Step 3: New C# project — `FollowMe.MultiBox`

**Location:** `DesktopDotNet/FollowMe.MultiBox/`
**Type:** Class library (.NET 9, no WinForms dependency)
**References:** `FollowMe.Reader` only

### Sub-namespace layout

```
FollowMe.MultiBox/
  Config/
    MultiBoxConfig.cs         — root config record; loaded from multibox.json
    MultiBoxMode.cs           — enum: Off, Leader, Follower
  Protocol/
    PlayerPositionSnapshot.cs — record: float X, Y, Z
    MultiBoxStateSnapshot.cs  — record: bool InCombat, bool HasTarget, bool TargetHostile, string TargetName
    MultiBoxFrameProtocol.cs  — static: BuildPositionFrameBytes, BuildMultiBoxStateFrameBytes,
                                         TryParsePositionFrame, TryParseMultiBoxStateFrame
                               — extends existing FrameProtocol pattern exactly
  State/
    LeaderState.cs            — record: PlayerPositionSnapshot Position, MultiBoxStateSnapshot MultiBox,
                                        DateTimeOffset Timestamp
    FollowerState.cs          — record: PlayerPositionSnapshot Position, DateTimeOffset Timestamp
  Interfaces/
    ILeaderStateSource.cs     — ILeaderStateSource { LeaderState? Current; event Action<LeaderState> Updated; }
    IFollowerStateSource.cs   — same pattern for follower's own position
    IMultiBoxTransport.cs     — Task StartAsync(CancellationToken); event Action<LeaderState> LeaderStateReceived;
    IMovementController.cs    — void ApplyMovement(LeaderState leader, FollowerState follower, MultiBoxConfig cfg)
    ITargetController.cs      — void ApplyTarget(LeaderState leader, string lastKnownTarget)
  Sources/
    TelemetryLeaderStateSource.cs   — reads PlayerPosition + MultiBoxState frames from TelemetryAggregate;
                                       raises Updated event when new frame decoded
    TelemetryFollowerStateSource.cs — same for follower's own position
  Transport/
    TcpLeaderBroadcast.cs     — TcpListener on cfg.Port; serializes LeaderState as newline-delimited JSON;
                                 one connected follower at a time in v1
    TcpFollowerReceive.cs     — TcpClient to cfg.LeaderHost:cfg.Port; deserializes; raises LeaderStateReceived
  Controllers/
    Win32MovementController.cs      — P/Invoke SendInput; computes bearing + distance;
                                       holds W when distance > StopDistance; releases when ≤ StopDistance;
                                       steers with mouse delta (yaw) when bearing error > 5°
    Win32TargetController.cs        — debounced: when TargetName changes AND TargetHostile=true,
                                       injects keystrokes for "/assist [LeaderName]\n" via SendInput
  Session/
    MultiBoxLeaderSession.cs  — wires: TelemetryLeaderStateSource → TcpLeaderBroadcast
    MultiBoxFollowerSession.cs — wires: TcpFollowerReceive + TelemetryFollowerStateSource
                                         → Win32MovementController + Win32TargetController
    MultiBoxSessionFactory.cs — static Create(MultiBoxConfig) → IDisposable session
```

### Key design constraints
- All classes depend on **interfaces**, not concretions — constructor-injected
- `MultiBoxConfig` loaded from `multibox.json` in working directory; provide sane defaults
- `Win32MovementController` and `Win32TargetController` are Windows-only — isolate all P/Invoke there; interfaces are portable
- `TcpLeaderBroadcast` and `TcpFollowerReceive` use `System.Text.Json` (in-box, no NuGet)
- No `async void` — all async paths return `Task`; use `CancellationToken` throughout
- No static mutable state

### `MultiBoxConfig.cs` (full definition)
```csharp
public sealed record MultiBoxConfig
{
    public MultiBoxMode Mode { get; init; } = MultiBoxMode.Off;
    public int TcpPort { get; init; } = 7742;
    public string LeaderHost { get; init; } = "192.168.1.100";  // follower only
    public string LeaderName { get; init; } = "LeaderChar";      // for /assist
    public float FollowDistance { get; init; } = 8.0f;           // units — start moving
    public float StopDistance { get; init; } = 3.5f;             // units — stop moving
    public float BearingDeadZoneDeg { get; init; } = 5.0f;       // degrees — no steer
    public int TargetAssistDebounceMs { get; init; } = 800;       // ms between /assist calls
    public bool EnableMovement { get; init; } = true;
    public bool EnableTargetAssist { get; init; } = true;
}
```

---

## Step 4: Extend existing C# projects

### `FollowMe.Reader` — extend `TelemetryAggregate`

Add:
```csharp
public PlayerPositionFrame? PositionFrame { get; private set; }
public DateTimeOffset? PositionUpdatedAtUtc { get; private set; }
public MultiBoxStateFrame? MultiBoxFrame { get; private set; }
public DateTimeOffset? MultiBoxUpdatedAtUtc { get; private set; }
```
Extend `Apply(TelemetryFrame)` switch with the two new frame types.

### `FollowMe.Cli` — add multibox commands

Add to `Run()` switch:
```
"multibox-leader"   → RunMultiBoxLeader(args)
"multibox-follower" → RunMultiBoxFollower(args)
```

`RunMultiBoxLeader`: loads `multibox.json`, starts `MultiBoxLeaderSession`, runs until Ctrl+C, prints status line every 2s.
`RunMultiBoxFollower`: same but `MultiBoxFollowerSession`, also prints leader position + own position + distance each 2s.

### `FollowMe.Hud` — add MultiBox status panel (optional section, add to `HudForm`)

Add a new `GroupBox "MultiBox"` row at bottom of layout:
- Shows: Mode, Leader Pos (x/y/z), Follower Pos, Distance, Target, Last Assist, TCP status
- Colored indicator: Green = following, Yellow = stopped (close enough), Red = disconnected

---

## Step 5: Unit tests (`FollowMe.Tests`)

Add tests for:
1. `MultiBoxFrameProtocol.BuildPositionFrameBytes` round-trips through `TryParsePositionFrame` for positive, negative, and zero coords
2. `MultiBoxFrameProtocol.BuildMultiBoxStateFrameBytes` round-trips through `TryParseMultiBoxStateFrame` for all flag combinations and target name lengths 0, 1, 5, 10
3. `TelemetryLeaderStateSource` correctly raises `Updated` when a new position frame is decoded
4. Existing smoke test still passes (non-regression)

---

## Build order

1. **Research** — verify all 5 RIFT API questions; document inline
2. **Protocol layer** — `MultiBoxFrameProtocol.cs` (C#) + frame-type extensions in `Protocol.lua` and `Config.lua`
3. **Unit tests for protocol layer** — verify round-trip before anything else
4. **State + Interfaces** — `LeaderState`, `FollowerState`, all `I*` interfaces
5. **Transport layer** — `TcpLeaderBroadcast`, `TcpFollowerReceive` (can be tested standalone with a mock source)
6. **Sources** — `TelemetryLeaderStateSource`, `TelemetryFollowerStateSource` (extend `TelemetryAggregate`)
7. **Controllers** — `Win32MovementController`, `Win32TargetController`
8. **Sessions + Factory** — wire everything; add CLI commands
9. **Lua** — `Core/MultiBox.lua`; extend `Bootstrap.lua`
10. **HUD extension** — add MultiBox panel to `HudForm`
11. **Integration test** — `multibox-leader smoke` command that runs without a live game: uses synthetic position frames

---

## Acceptance criteria

| Test | Pass condition |
|------|---------------|
| Protocol round-trip | Position ± 6500 coords at ×100 scale survives encode→decode with < 0.01 unit error |
| TCP relay | Leader C# sends `LeaderState` JSON; follower C# receives + deserializes within 200ms on localhost |
| Movement logic | `Win32MovementController.ComputeMovement(leader, follower)` returns `MoveForward=true` when distance > FollowDistance, `false` when < StopDistance |
| Target debounce | `Win32TargetController` fires assist at most once per TargetAssistDebounceMs regardless of frame rate |
| No chat leak | No `Command.Message` or chat slash command fires when mode is "off" or during position-only frames |
| Non-regression | All existing `FollowMe.Tests` still pass |

---

## Coding conventions (must follow throughout)

- All new Lua: `-- FollowMe.MultiBox | vX.Y | N chars` at top and bottom
- All `Inspect.*` Lua calls: wrapped in `pcall`, nil-check result before use (see `SafeUnitDetail` in `Gather.lua`)
- All C# records: immutable `init`-only properties
- Flag all nil-reference risks with a `// NilRisk:` comment before finalizing
- No static mutable fields in C# (use instances injected via constructor)
- No new NuGet packages without explicit justification
- `System.Text.Json` for all JSON (no Newtonsoft.Json additions)
- All P/Invoke in `Win32*.cs` files only — zero Win32 calls in library or session code
