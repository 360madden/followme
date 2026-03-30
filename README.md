# FollowMe

FollowMe is a reliability-first optical telemetry project for RIFT with four active parts:
- a Lua addon that renders a segmented color strip in game
- a `.NET 9` reader and CLI under `DesktopDotNet/`
- a Windows inspector for capture review
- a Windows HUD app that aggregates live player vitals and supported stat pages

The active transport is now a reader-first encoded color strip. The older barcode/matrix baseline is archived for reference only.

## Current Baseline

This repo now targets a single live profile:
- profile `P360C`
- client `640x360`
- top band `640x24`
- `80` vertical segments at `8x24`
- fixed `8-color` alphabet
- fixed control markers on both edges
- mixed live transport: fast `coreStatus` heartbeat plus rotating player stat pages

Current proof level:
- offline smoke, replay, bench, build, and tests remain the validation set; rerun them after this strip-layout change
- live capture remains centered on `DesktopDuplication` as the primary backend
- live decode now targets a full-size strip at `origin 0,0`, `pitch 8.0`, `scale 1.0` on the `640x360` client
- the reader still searches the legacy `0.35` live scale so older captures remain replayable
- capture runs now emit raw BMP, annotated BMP, and JSON sidecar diagnostics under `AppData\Local\FollowMe\DesktopDotNet\out`

## Quick Start

Prepare the RIFT window:

```powershell
dotnet run --project C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\DesktopDotNet\FollowMe.Cli\FollowMe.Cli.csproj -- prepare-window 32 32
```

Generate and verify a synthetic color-strip fixture:

```powershell
dotnet run --project C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\DesktopDotNet\FollowMe.Cli\FollowMe.Cli.csproj -- smoke
```

Replay a saved capture:

```powershell
dotnet run --project C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\DesktopDotNet\FollowMe.Cli\FollowMe.Cli.csproj -- replay C:\Users\mrkoo\AppData\Local\FollowMe\DesktopDotNet\fixtures\followme-color-core.bmp
```

Run the replay bench:

```powershell
dotnet run --project C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\DesktopDotNet\FollowMe.Cli\FollowMe.Cli.csproj -- bench
```

Capture the live top band:

```powershell
dotnet run --project C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\DesktopDotNet\FollowMe.Cli\FollowMe.Cli.csproj -- capture-dump --backend desktopdup
```

Run a short live decode:

```powershell
dotnet run --project C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\DesktopDotNet\FollowMe.Cli\FollowMe.Cli.csproj -- live 5 100 --backend desktopdup
```

Open the inspector:

```powershell
dotnet run --project C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\DesktopDotNet\FollowMe.Inspector\FollowMe.Inspector.csproj
```

Open the live HUD:

```powershell
dotnet run --project C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\DesktopDotNet\FollowMe.Hud\FollowMe.Hud.csproj
```

Wrapper scripts:
- [Prepare-FollowMe-640x360.cmd](C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\scripts\Prepare-FollowMe-640x360.cmd)
- [Smoke-FollowMe.cmd](C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\scripts\Smoke-FollowMe.cmd)
- [Bench-FollowMe.cmd](C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\scripts\Bench-FollowMe.cmd)
- [Live-FollowMe.cmd](C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\scripts\Live-FollowMe.cmd)
- [Open-FollowMe-Inspector.cmd](C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\scripts\Open-FollowMe-Inspector.cmd)
- [Open-FollowMe-Hud.cmd](C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\scripts\Open-FollowMe-Hud.cmd)
- [Reload-RiftUi.cmd](C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\scripts\Reload-RiftUi.cmd)
- [Resize-RiftClient-640x360.cmd](C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\scripts\Resize-RiftClient-640x360.cmd)

If RIFT was already running while Lua files changed, restart the client or reload the addon before expecting `capture-dump` or `live` to see the new strip.
`capture-dump`, `live`, and `watch` accept `--backend desktopdup|screen|printwindow`.
Default live backend order is `DesktopDuplication`, then `ScreenBitBlt`. `PrintWindow` is now debug-only.
`Reload-RiftUi.cmd` sends the official RIFT `/reloadui` command to the active game window so you can refresh addon changes without restarting the client.

## Outputs

Reader artifacts are written under:
`C:\Users\mrkoo\AppData\Local\FollowMe\DesktopDotNet`

Useful locations:
- `fixtures\followme-color-core.bmp`
- `out\followme-color-capture-dump.bmp`
- `out\followme-color-capture-dump-annotated.bmp`
- `out\followme-color-capture-dump.json`
- `out\followme-color-first-reject.bmp`

## Source Of Truth

- Product direction lives in [PROJECT_PROMPT.md](C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\PROJECT_PROMPT.md)
- Active desktop solution lives at [DesktopDotNet/FollowMe.sln](C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\FollowMe\DesktopDotNet\FollowMe.sln)
- Barcode-style and archive branches are reference only


## HUD usability notes
- The HUD now starts with **Always on top** enabled by default.
- The transport cadence is faster, while player stat sampling is cached so `Inspect.Stat()` is not spammed every frame.
