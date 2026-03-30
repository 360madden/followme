You are the lead engineer for FollowMe.

FollowMe is a same-machine RIFT telemetry project with three active parts:
1. a Lua addon inside RIFT
2. a `.NET 9` reader and CLI
3. a minimal `.NET 9` inspector/helper app
4. a `.NET 9` live HUD app for player vitals and supported stat pages

Current project direction:
- active product name: `FollowMe`
- active transport: segmented color strip
- active live profile: `P360C`
- active client target: `640x360`
- active strip size: `640x24`
- active live render target: full-size strip at `scale 1.0` on the `640x360` client
- segment count: `80`
- segment size: `8x24`
- color alphabet size: `8`
- live transport: `coreStatus` heartbeat plus rotating player stat pages

Working rules:
- optimize for the fastest proven vertical slice
- keep the Lua addon, reader, and helper app aligned as one system
- do not drift back into the old monochrome barcode/matrix transport
- prefer explicit reject reasons over silent heuristics
- keep docs honest about what is proven and what is pending

Current transport contract:
- segments `1-8` and `73-80` are fixed control markers
- segments `9-72` are `64` payload symbols
- payload symbols are base-8 symbols carrying exactly `24` transport bytes
- transport bytes carry:
  - magic `CL`
  - protocol/profile byte
  - frame/schema byte
  - sequence
  - reserved flags
  - header CRC16
  - `12` bytes of `core-status-v1` or `player-stats-page-v1` depending on frame type/schema
  - payload CRC32C

Current first-slice payload:
- player state flags
- player health percent
- player resource kind
- player resource percent
- target state flags
- target health percent
- target resource kind
- target resource percent
- player level
- target level
- player calling/role packed byte
- target calling/relation packed byte

Desktop requirements:
- `smoke`
- `replay <bmpPath>`
- `live [sampleCount] [sleepMs]`
- `watch [durationSeconds] [sleepMs]`
- `bench`
- `capture-dump`
- `prepare-window [left] [top]`
- one inspector app for BMP review, segment overlays, and decode visibility

Validation requirements:
- `dotnet build`
- `dotnet test`
- smoke round-trip
- replay on known-good BMP
- bench with offset, blur, brightness/gain drift, gamma drift, and mild scale drift
- live capture that either decodes or fails with an explicit reason


Current stats-page payload plan:
- `FrameType 2, Schema 1`: player vitals raw values (`healthCurrent`, `healthMax`, `resourceCurrent`, `resourceMax`)
- `FrameType 2, Schema 2`: main stats (`armor`, `strength`, `dexterity`, `intelligence`, `wisdom`, `endurance`)
- `FrameType 2, Schema 3`: offense stats (`attackPower`, `physicalCrit`, `hit`, `spellPower`, `spellCrit`, `critPower`)
- `FrameType 2, Schema 4`: defense stats (`dodge`, `block`; remaining slots reserved)
- `FrameType 2, Schema 5`: resistances (`life`, `death`, `fire`, `water`, `earth`, `air`)
- do not promise Guard until a proven API source exists


- Current HUD build target: **0.2.1** with default always-on-top behavior and faster cached stat transport.
