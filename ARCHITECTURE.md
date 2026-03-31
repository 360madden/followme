# FollowMe Architecture

This document describes the complete system design: the optical telemetry protocol, the TCP relay, and the Win32 input controller.

## System Overview

```
┌────────────────────────────────────────────────────────────────────┐
│                     [Leader RIFT Machine]                          │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌─────────────────────────────────────┐                         │
│  │     RIFT Game Client (640×360)      │                         │
│  ├─────────────────────────────────────┤                         │
│  │ Lua Addon (FollowMe)                │                         │
│  │  ├─ Gather: Player state via API    │                         │
│  │  ├─ Protocol: Encode frames         │                         │
│  │  ├─ Render: 640×24px color strip    │                         │
│  │  └─ Bootstrap: Frame rotation loop  │                         │
│  │      (CoreStatus + Stats +          │                         │
│  │       Position + MultiBox frames)   │                         │
│  └──────────────────┬──────────────────┘                         │
│                     │ (pixel color data at window top)            │
│  ┌──────────────────▼──────────────────┐                         │
│  │  FollowMe.Cli / FollowMe.Hud        │                         │
│  │  ├─ DesktopDuplication capture      │                         │
│  │  ├─ Frame decoder (CRC validate)    │                         │
│  │  ├─ TelemetryAggregate              │                         │
│  │  └─ TcpLeaderBroadcast              │                         │
│  │      (sends LeaderState JSON)       │                         │
│  └──────────────────┬──────────────────┘                         │
│                     │ TCP:7742                                    │
└─────────────────────┼──────────────────────────────────────────────┘
                      │
                      │ (LeaderState: position + combat + timestamp)
                      │
┌─────────────────────▼──────────────────────────────────────────────┐
│                  [Follower RIFT Machine]                           │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌──────────────────────────────────┐                            │
│  │  FollowMe.Cli / FollowMe.Hud     │                            │
│  │  ├─ TcpFollowerReceive           │                            │
│  │  │   (receives LeaderState JSON) │                            │
│  │  ├─ DesktopDuplication capture   │                            │
│  │  │   (own position frames)       │                            │
│  │  ├─ Win32MovementController      │                            │
│  │  │   (WASD + mouse yaw steering) │                            │
│  │  └─ Win32TargetController        │                            │
│  │      (/assist debounced)         │                            │
│  └──────────────────┬───────────────┘                            │
│                     │ (Win32 SendInput events)                    │
│  ┌──────────────────▼──────────────────┐                         │
│  │     RIFT Game Client (640×360)      │                         │
│  ├─────────────────────────────────────┤                         │
│  │ Lua Addon (FollowMe)                │                         │
│  │  ├─ Gather: Own position + state    │                         │
│  │  └─ Render: Position frame          │                         │
│  └─────────────────────────────────────┘                         │
│                     │ (pixel data at window top)                  │
└─────────────────────────────────────────────────────────────────────┘
```

## Color Strip Protocol

### Physical Layout

- **Window target**: 640×360 (profile P360C)
- **Strip location**: Top-left, from (0, 0) to (640, 24)
- **Segments**: 80 × 8px wide, 24px tall
- **Control edges**: Segments 1–8 (left) and 73–80 (right) are fixed markers
- **Payload area**: Segments 9–72 (64 segments)

### Color Alphabet (8-Color)

Each segment displays one of 8 colors, representing one 3-bit symbol:

| Bits | Color | R | G | B | Hex |
|------|-------|---|---|---|-----|
| 000  | Black | 0 | 0 | 0 | #000000 |
| 001  | Red   | 255 | 0 | 0 | #FF0000 |
| 010  | Green | 0 | 255 | 0 | #00FF00 |
| 011  | Yellow| 255 | 255 | 0 | #FFFF00 |
| 100  | Blue  | 0 | 0 | 255 | #0000FF |
| 101  | Magenta | 255 | 0 | 255 | #FF00FF |
| 110  | Cyan  | 0 | 255 | 255 | #00FFFF |
| 111  | White | 255 | 255 | 255 | #FFFFFF |

**Decoding:** Each segment's average RGB is computed. If R > 128, set bit 2; if G > 128, set bit 1; if B > 128, set bit 0. This produces a 3-bit symbol.

### Frame Structure

Each transmission is **64 payload symbols** (3 bits each = 192 bits = 24 bytes) surrounded by control markers:

```
[Control L] [Control L] [Control L] [Control L] [Control L] [Control L] [Control L] [Control L]
[Payload]  [Payload]  [Payload]  [Payload]  [Payload]  [Payload]  [Payload]  [Payload]  ... (64 symbols)
[Control R] [Control R] [Control R] [Control R] [Control R] [Control R] [Control R] [Control R]
```

**Control markers:**
- All left edge segments display fixed color (e.g., R=255, G=0, B=0 = Red = binary 001)
- All right edge segments display a different fixed color (e.g., R=0, G=255, B=0 = Green = binary 010)
- Decoder verifies left/right colors match expected values; if not, reject the frame

### Frame Transport Format

Each 24-byte frame is encoded as:

```
Byte Offset  Field              Size  Notes
───────────────────────────────────────────────────────────
0–1          Magic              2     Always "CL" (0x43, 0x4C)
2            Protocol/Profile   1     Always 0x01 (current version)
3            Frame Type + Sch.  1     High nibble=type (1–4), low=schema (0–15)
4            Sequence           1     Rolling counter (0–255)
5            Flags              1     Reserved for future use
6–7          Header CRC-16      2     CRC-16-CCITT over bytes 0–5
8–19         Payload            12    Varies by frame type (see below)
20–23        Payload CRC-32C    4     CRC-32C over bytes 8–19
───────────────────────────────────────────────────────────
Total: 24 bytes
```

**Encoding:** 24 bytes = 192 bits → 64 3-bit symbols → 64 color segments

**Decoding:** 64 color segments → 64 3-bit symbols → 24 bytes (with CRC validation at each step)

## Frame Types

### Frame Type 1: CoreStatus (Type=1, Schema=1)

**Purpose:** Heartbeat frame with player and target health, resource, and state.

**Payload (12 bytes):**

```
Byte Offset  Field                    Size  Type       Notes
─────────────────────────────────────────────────────────────
0            Player State Flags       1     uint8      Bit 0=inCombat, 1=alive, ...
1            Player Health %          1     uint8      0–100, or 255=unknown
2            Player Resource Kind     1     uint8      0=health, 1=mana, 2=energy, etc.
3            Player Resource %        1     uint8      0–100, or 255=unknown
4            Target State Flags       1     uint8      Bit 0=alive, 1=hostile, ...
5            Target Health %          1     uint8      0–100, or 255=unknown
6            Target Resource Kind     1     uint8      (unused, reserved)
7            Target Resource %        1     uint8      0–100, or 255=unknown
8            Player Level             1     uint8      1–80
9            Target Level             1     uint8      1–80 or 255=unknown
10           Player Calling+Role      1     uint8      High 4 bits=calling, low=role
11           Target Calling+Relation  1     uint8      High 4 bits=calling, low=relation
─────────────────────────────────────────────────────────────
Total: 12 bytes
```

**Rotation:** CoreStatus frames are sent every ~50ms (throttled to 0.05s) at approximately 20 Hz.

### Frame Type 2: PlayerStatsPage (Type=2, Schema=1–5)

**Purpose:** Extended player stats, rotated across multiple schemas.

**Schema 1: Vitals (raw values)**
```
Byte Offset  Field           Size  Type    Notes
──────────────────────────────────────────────
0–1          Health Current  2     uint16  (little-endian or big-endian — TBD)
2–3          Health Max      2     uint16
4–5          Resource Curr.  2     uint16
6–7          Resource Max    2     uint16
8–11         Reserved        4     uint32  (padding)
──────────────────────────────────────────────
Total: 12 bytes
```

**Schema 2: Main Stats**
```
Byte Offset  Field         Size  Type    Notes
───────────────────────────────────────────────
0            Armor         1     uint8   0–255
1            Strength      1     uint8   0–255
2            Dexterity     1     uint8   0–255
3            Intelligence  1     uint8   0–255
4            Wisdom        1     uint8   0–255
5            Endurance     1     uint8   0–255
6–11         Reserved      6     uint8   (padding)
───────────────────────────────────────────────
Total: 12 bytes
```

**Schema 3: Offense Stats**
```
Byte Offset  Field         Size  Type    Notes
───────────────────────────────────────────────
0–1          Attack Power  2     uint16
2            Physical Crit 1     uint8   0–100 (%)
3            Hit           1     uint8   0–100 (%)
4–5          Spell Power   2     uint16
6            Spell Crit    1     uint8   0–100 (%)
7–11         Reserved      5     uint8   (padding)
───────────────────────────────────────────────
Total: 12 bytes
```

**Schema 4: Defense Stats**
```
Byte Offset  Field    Size  Type    Notes
──────────────────────────────────────────
0            Dodge    1     uint8   0–100 (%)
1            Block    1     uint8   0–100 (%)
2–11         Reserved 10    uint8   (padding)
──────────────────────────────────────────
Total: 12 bytes
```

**Schema 5: Resistances**
```
Byte Offset  Field   Size  Type    Notes
─────────────────────────────────────────
0            Life    1     uint8   0–100 (%)
1            Death   1     uint8   0–100 (%)
2            Fire    1     uint8   0–100 (%)
3            Water   1     uint8   0–100 (%)
4            Earth   1     uint8   0–100 (%)
5            Air     1     uint8   0–100 (%)
6–11         Reserved 6    uint8   (padding)
─────────────────────────────────────────
Total: 12 bytes
```

**Rotation:** Stats pages rotate in order (Schema 1 → 2 → 3 → 4 → 5 → 1 ...), one per ~250ms (5 schemas × 0.05s ≈ 0.25s).

### Frame Type 3: PlayerPosition (Type=3, Schema=1)

**Purpose:** World coordinates of the player (leader broadcasts, follower reads own).

**Payload (12 bytes):**

```
Byte Offset  Field     Size  Type    Notes
─────────────────────────────────────────────────────────
0–3          X (int32 BE) 4  int32   World coord × 100 scale, big-endian
4–7          Y (int32 BE) 4  int32   Same scale
8–11         Z (int32 BE) 4  int32   Same scale
─────────────────────────────────────────────────────────
Total: 12 bytes
```

**Encoding:** Position coords are multiplied by 100, clamped to int32 range [–2,147,483,648 to +2,147,483,647], and serialized as big-endian 32-bit signed integers.

**Decoding:** Read 4 bytes per axis as big-endian int32, divide by 100.0 to recover float world coordinates.

**Example:**
- Leader at world coords (123.45, 200.67, -50.12)
- Encode as: 12345, 20067, -5012 (int32 big-endian)
- Frame bytes: 0x00003039, 0x00004E53, 0xFFFFEC1C
- Follower decodes: 123.45, 200.67, -50.12

**Transmission:** Position frames are sent interleaved with CoreStatus/Stats at ~100ms intervals (every 2–3 ticks).

### Frame Type 4: MultiBoxState (Type=4, Schema=1)

**Purpose:** Leader's combat state and hostile target name (for follower assist injection).

**Payload (12 bytes):**

```
Byte Offset  Field         Size  Type      Notes
──────────────────────────────────────────────────────
0            Flags         1     uint8     Bit 0=inCombat, 1=hasTarget, 2=targetHostile
1            TargetLen     1     uint8     0–10 (clamped length of target name)
2–11         TargetName    10    ASCII     First 10 chars of target name (0-padded)
──────────────────────────────────────────────────────
Total: 12 bytes
```

**Flags:**
- Bit 0: `inCombat` (1 = in combat, 0 = out of combat)
- Bit 1: `hasTarget` (1 = target exists, 0 = no target)
- Bit 2: `targetHostile` (1 = target is hostile, 0 = friendly/neutral)
- Bits 3–7: Reserved (set to 0)

**TargetName encoding:**
- If no target: `TargetLen = 0`, name bytes all 0x00
- If target exists: `TargetLen = min(strlen(name), 10)`, name bytes are ASCII codes (or 0x00 if padding)

**Example:**
- Leader is in combat, target is "Dragnoth" (hostile)
- Flags = 0x07 (bits 0, 1, 2 all set)
- TargetLen = 8
- TargetName = "Dragnoth\0\0"
- Frame bytes: 0x07, 0x08, 0x44, 0x72, 0x61, 0x67, 0x6E, 0x6F, 0x74, 0x68, 0x00, 0x00

**Transmission:** Sent every ~50ms alongside CoreStatus and Position frames.

## CRC Details

### CRC-16-CCITT (Header)

Used to validate bytes 0–5 (magic, protocol, frame type, sequence, flags).

```
Polynomial: 0x1021
Initial:    0xFFFF
Reflected input:  No
Reflected output: No
Final XOR: 0x0000
```

Stored as 2 bytes (big-endian) at bytes 6–7.

**Rationale:** Quick check that frame boundaries are intact before attempting to decode.

### CRC-32C (Payload)

Used to validate bytes 8–19 (the 12-byte payload).

```
Polynomial: 0x1EDC6F41 (Castagnoli)
Initial:    0xFFFFFFFF
Reflected input:  Yes
Reflected output: Yes
Final XOR: 0xFFFFFFFF
```

Stored as 4 bytes (little-endian) at bytes 20–23.

**Rationale:** Industry-standard Castagnoli CRC, fast to compute, low collision risk for random data.

## Frame Rotation (Bootstrap)

The Lua addon sends frames in a repeating sequence to maximize data throughput while keeping the heartbeat frequent:

```
Tick (sequence mod 10)  Frame Type           Purpose
─────────────────────────────────────────────────────────
0                       CoreStatus           Health update
1                       CoreStatus           Health update (2/4 ticks)
2                       CoreStatus           Health update (3/4 ticks)
3                       CoreStatus           Health update (4/4 ticks)
4                       PlayerPosition       Leader position (multibox)
5                       MultiBoxState        Leader combat state (multibox)
6                       PlayerStatsPage (1)  Vitals
7                       PlayerStatsPage (2)  Main stats
8                       PlayerPosition       Repeat position (multibox)
9                       CoreStatus           Back to heartbeat
───────────────────────────────────────────────────────────

Total cycle: ~500ms (10 ticks × 0.05s throttle)
```

**Rationale:**
- CoreStatus every 50ms ensures health updates are visible
- Position and MultiBoxState every 100ms (ticks 4, 8) give follower frequent location updates
- Stats pages rotate slowly (every 250ms per schema) to avoid overwhelming the reader

## Capture Backends

The C# reader uses different methods to capture the color strip, tried in this order:

### DesktopDuplication (Preferred)

- **API:** Windows.Graphics.Capture (DirectX 11)
- **Pros:** Hardware-accelerated, reliable, works with transparent windows
- **Cons:** Requires Windows 10+, may fail if Windows is in screensaver
- **Performance:** ~60 FPS capable

### ScreenBitBlt (Fallback)

- **API:** User32.BitBlt (GDI)
- **Pros:** Works on older Windows, always available
- **Cons:** Slower, can be blocked by overlays
- **Performance:** ~30 FPS

### PrintWindow (Debug Only)

- **API:** User32.PrintWindow (WM_PRINT)
- **Pros:** Works with some window types GDI can't reach
- **Cons:** Very slow, unreliable with modern games
- **Performance:** ~5–10 FPS
- **Status:** Disabled for production; debug-only fallback

**Selection logic:**
```
if DesktopDuplication.Available:
    try: use DesktopDuplication
    catch: fall back to ScreenBitBlt
else:
    use ScreenBitBlt
```

## TCP Relay (Multiboxing)

### LeaderState JSON Format

The leader broadcasts its position and combat state to the follower as newline-delimited JSON:

```json
{
  "position": {
    "x": 123.45,
    "y": 200.67,
    "z": -50.12
  },
  "multiBox": {
    "inCombat": true,
    "hasTarget": true,
    "targetHostile": true,
    "targetName": "Dragnoth"
  },
  "timestamp": "2026-03-30T15:42:17.1234567Z"
}
```

Sent once per decoded PlayerPosition frame (~100ms intervals).

### TcpLeaderBroadcast

- **Listen port:** 7742 (configurable via `multibox.json`)
- **Protocol:** Newline-delimited JSON (each message ends with `\n`)
- **Connections:** One follower per leader in v0.1.0; broadcast to multiple clients in v0.2.0+
- **Send rate:** ~10 updates/sec (one per PlayerPosition frame)

**State machine:**
```
[Idle] → [Listening on :7742]
            ↓ (client connects)
         [Connected] → [Send LeaderState JSON on each frame update]
            ↓ (client disconnects or error)
         [Listening on :7742] → (repeat)
```

### TcpFollowerReceive

- **Connect to:** `LeaderHost:7742` (from config, e.g., 192.168.1.100)
- **Receive:** Newline-delimited JSON
- **Parse:** Deserialize to `LeaderState` object
- **Error handling:** If parse fails, log and skip frame; if connection drops, reconnect after 1s delay

**State machine:**
```
[Idle] → [Connecting to LeaderHost:7742]
            ↓ (connection OK)
         [Receiving JSON frames]
            ├─ OnLeaderStateReceived event fired for each frame
            └─ (if connection drops)
                ↓ (wait 1s, reconnect)
         [Connecting to LeaderHost:7742] → (repeat)
```

## Win32 Movement Controller

### Design

The follower's `Win32MovementController` computes movement commands from leader position, own position, and leader bearing, then injects Win32 `SendInput` keyboard events (W, A, D) and mouse movement.

**Inputs:**
- `leaderPosition`: (x, y, z) from LeaderState
- `followerPosition`: (x, y, z) from own telemetry frame
- `config.FollowDistance`: Distance to start moving (units)
- `config.StopDistance`: Distance to stop moving (units)
- `config.BearingDeadZoneDeg`: Bearing error threshold (degrees) before steering

**Outputs:**
- `W` key down (move forward) or up (stop)
- Mouse X delta (yaw steering, scaled by bearing error)

### Movement Logic

```
1. Compute distance:
   dist = sqrt((leader.x - follower.x)² + (leader.y - follower.y)² + (leader.z - follower.z)²)

2. Compute bearing (horizontal plane):
   dx = leader.x - follower.x
   dy = leader.y - follower.y
   leaderBearing = atan2(dy, dx)

3. Bearing error (follower's yaw must match leader's direction):
   followerYaw = current facing (from player telemetry or estimated)
   bearingError = normalize(leaderBearing - followerYaw) to [-180, +180] degrees

4. Movement decision:
   if dist > config.FollowDistance:
       SendInput(W key down)  // Move forward
   else if dist < config.StopDistance:
       SendInput(W key up)    // Stop
   // else: hold current state

5. Steering decision:
   if |bearingError| > config.BearingDeadZoneDeg:
       mouseX_delta = bearingError * sensitivity_factor
       SendInput(mouse move by mouseX_delta)
   // else: no steering input
```

**Thread safety:** The controller runs in a dedicated thread; position updates are synchronized via a lock or concurrent data structure.

### Win32 SendInput

All input is injected via `User32.SendInput` (P/Invoke).

```csharp
[DllImport("user32.dll")]
public static extern uint SendInput(
    uint cInputs,                      // Number of INPUT structures
    [In] INPUT[] pInputs,               // Input array
    int cbSize);                        // Size of INPUT struct

// INPUT union (keyboard + mouse variants)
[StructLayout(LayoutKind.Explicit, Size = 40)]
public struct INPUT
{
    [FieldOffset(0)]
    public INPUT_TYPE type;

    [FieldOffset(8)]
    public MOUSEINPUT mi;

    [FieldOffset(8)]
    public KEYBDINPUT ki;
}
```

**Why SendInput?**
- Works while RIFT window is not in focus (multibox headless)
- No clipboard pollution
- Atomic injection of multiple keys/mouse events
- ~1ms latency (acceptable for ~100ms LeaderState updates)

## Win32 Target Controller

### Design

When the leader's hostile target changes, the follower injects `/assist [LeaderName]` to auto-target.

**Debounce:** Fires at most once per `config.TargetAssistDebounceMs` (default 800ms) to avoid spam.

**State tracking:**
```csharp
string? lastTargetName = null;
DateTimeOffset lastAssistTime = DateTimeOffset.MinValue;

void OnLeaderStateReceived(LeaderState leader)
{
    if (leader.MultiBox.TargetHostile &&
        leader.MultiBox.TargetName != lastTargetName &&
        (Now - lastAssistTime) > debounceMs)
    {
        SendInput("/assist " + leader.MultiBox.TargetName + "\n");
        lastTargetName = leader.MultiBox.TargetName;
        lastAssistTime = Now;
    }
}
```

**Character filtering:** Target name is sanitized (alphanumerics + space only) to prevent command injection.

## Diagnostics & Observability

### Frame Rejection Tracking

Every decode attempt records one of:
- ✅ **Accept**: Frame decoded successfully, CRC valid
- ❌ **Reject (reason)**:
  - `BadMagic`: First 2 bytes != "CL"
  - `BadFrameType`: Frame type > 4 or unknown schema
  - `BadHeaderCrc`: CRC-16 over header mismatch
  - `BadPayloadCrc`: CRC-32C over payload mismatch
  - `OutOfBounds`: Symbol index outside [0, 63]
  - `NoControlMarkers`: Control edge colors don't match expected
  - `SymbolError`: Too many high bits (noise/blur)

### Artifacts

After each capture or replay session, the reader writes to `AppData\Local\FollowMe\DesktopDotNet\`:

**BMP files:**
- `followme-color-capture-dump.bmp`: Raw 640×24px RGB capture
- `followme-color-capture-dump-annotated.bmp`: Same + segment grid overlay + symbol values
- `followme-color-first-reject.bmp`: First rejected frame (for debugging)

**JSON sidecar** (`followme-color-capture-dump.json`):
```json
{
  "captureWidth": 640,
  "captureHeight": 24,
  "stripOffset": { "x": 0, "y": 0 },
  "segmentWidth": 8,
  "controlMarkerColor": "FF0000",
  "symbols": [
    { "index": 0, "rgb": "#FF0000", "bits": "001", "error": 0 },
    { "index": 1, "rgb": "#FF0001", "bits": "001", "error": 12 }
  ],
  "frames": [
    {
      "sequenceNum": 0,
      "frameType": 1,
      "schema": 1,
      "headerCrc": "0x1234",
      "payloadCrc": "0xABCDEF01",
      "status": "Accept",
      "decodedMs": 2.34
    }
  ],
  "rejectReasons": {
    "BadMagic": 0,
    "BadHeaderCrc": 3,
    "BadPayloadCrc": 1,
    "OutOfBounds": 0
  }
}
```

---

**FollowMe Architecture | v0.1.0** — Reliability-first design with observable, testable protocols.
