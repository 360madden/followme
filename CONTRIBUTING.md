# Contributing to FollowMe

Thanks for your interest in contributing! This document covers dev setup, coding conventions, and the PR workflow.

## Development Setup

### Requirements

- **Windows 10/11**
- **.NET 9 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/9.0))
- **RIFT game client** (for live testing)
- **Visual Studio Code** or **Visual Studio 2022** (optional, but recommended)
- **Git** ([download](https://git-scm.com/))

### Clone & Build

```powershell
git clone https://github.com/YOUR_USERNAME/followme.git
cd followme-dev
dotnet build
dotnet test
```

### Quick Test

Run the smoke test to verify everything works:

```powershell
dotnet run --project DesktopDotNet\FollowMe.Cli -- smoke
```

Expected output: Frame encoded and decoded successfully, CRC validation passed.

## Coding Conventions

### Lua (RIFT Addon)

The Lua addon runs inside RIFT and must be reliable — bad code crashes the game.

**Language & API:**
- **Lua 5.1** (RIFT-bundled version)
- **RIFT API only** — search before writing any new code:
  - `site:seebs.net rift live [FunctionName]` (direct fetch often blocked; use search)
  - `rift.mestoph.net` (current community patterns)
  - `rift.magelo.com` (static reference: dungeons, bosses, NPCs, items, achievements)
  - Never rely on training data; assume APIs change and verify before use

**Conventions:**
- File header with name, version, and character count:
  ```lua
  -- FollowMe.Config | v0.1.0 | 2,413 chars
  ```
- All `Inspect.*` and `Command.*` calls wrapped in `pcall()` to catch errors
- Nil-check all results before use (see `SafeUnitDetail` pattern in `Gather.lua`)
- Minimize global state; prefer module-local variables
- No external libraries; stick to RIFT's builtin Lua environment
- Comment complex logic; prefer self-documenting names over clever tricks

**File header for research findings:**
```lua
-- FollowMe.MultiBox | v0.1.0 | 9,784 chars
-- Research findings (verified against seebs.net + rift.mestoph.net):
--   Inspect.Unit.Detail("player").x/y/z: VERIFIED — returns world coords, updates while moving
--   Command.Slash: VERIFIED — silent injection, no chat log
--   Private addon channel: UNVERIFIED (blocking research)
```

**Testing:**
- Manual ingame: `/reloadui` after changes, watch for errors
- Use the HUD to observe telemetry changes in real-time
- Run smoke/replay/bench on the C# side after any Lua change

### C# (.NET 9)

**Language & Target:**
- **C# 12** (.NET 9 SDK)
- **Nullable reference types enabled** (`<Nullable>enable</Nullable>` in `.csproj`)
- **No unsafe code** (keep it portable)
- **No new NuGet packages without justification** — prefer inbox libraries

**Conventions:**
- **Immutable records** for all data models (`init`-only properties)
- **Constructor-injected dependencies** — no static mutable state
- **Interface-based design** — all major classes expose an interface
- **Async patterns**: `Task`, `Task<T>`, `CancellationToken` throughout (no `async void`)
- **Error handling**: explicit exception types; document failure modes
- **P/Invoke isolation**: all Win32 calls in `Win32*.cs` files only
- **JSON serialization**: `System.Text.Json` only (no Newtonsoft.Json)
- **Comments**: flag nil-reference risks with `// NilRisk:` before shipping

**File header:**
```csharp
// FollowMe.MultiBox.Win32MovementController | v0.1.0
// Isolated P/Invoke for Win32 SendInput movement control.
```

**Nil-safety checklist:**
Before finalizing any C# code, flag potential null-reference scenarios:
```csharp
public LeaderState? Current { get; private set; }  // NilRisk: callers must null-check

public void ApplyMovement(LeaderState leader, FollowerState follower)
{
    if (leader == null) throw new ArgumentNullException(nameof(leader));  // Fail-fast
    // ...
}
```

**Testing:**
- Unit tests in `FollowMe.Tests/` using xUnit
- Test protocol round-trip (encode → decode)
- Test error paths (CRC fail, out-of-bounds, malformed frames)
- Run `dotnet test` before every PR

## Project Organization

```
followme-dev/
├── Core/                     # Lua addon
│   ├── Config.lua            # Frame type & profile definitions
│   ├── Protocol.lua          # Encoding/decoding logic
│   ├── Gather.lua            # State inspection (with error traps)
│   ├── MultiBox.lua          # Position & assist frames
│   └── Bootstrap.lua         # Main update loop
├── DesktopDotNet/            # C# solution
│   ├── FollowMe.Reader/      # Core decode + telemetry aggregate
│   │   ├── FrameProtocol.cs  # Byte manipulation, CRC
│   │   ├── TelemetryAggregate.cs
│   │   └── Capture.cs
│   ├── FollowMe.MultiBox/    # TCP relay + Win32 input
│   │   ├── Config/
│   │   ├── Protocol/
│   │   ├── Transport/        # TcpLeaderBroadcast, TcpFollowerReceive
│   │   ├── Controllers/      # Win32MovementController, Win32TargetController
│   │   ├── Sources/          # TelemetryLeaderStateSource, etc.
│   │   └── Session/          # MultiBoxLeaderSession, MultiBoxFollowerSession
│   ├── FollowMe.Cli/         # Command-line interface
│   ├── FollowMe.Hud/         # WinForms UI
│   ├── FollowMe.Inspector/   # Diagnostic viewer
│   └── FollowMe.Tests/       # Unit tests
└── fixtures/                 # Test data (BMPs)
```

## Submitting Issues

Before opening an issue, check the [existing issues](https://github.com/YOUR_USERNAME/followme/issues) to avoid duplicates.

**Bug reports** should include:
1. What you tried (command, config, addon state)
2. What happened (error message, unexpected behavior)
3. What you expected
4. Steps to reproduce
5. System info (Windows version, .NET version, RIFT version)
6. Any artifacts (`capture-dump` BMP, JSON sidecar, error log)

**Feature requests** should include:
1. What problem it solves
2. How it fits into the roadmap (multibox, stats, UI, etc.)
3. Any alternative approaches you considered

See `.github/ISSUE_TEMPLATE/` for templates.

## Submitting Pull Requests

1. **Fork** the repository
2. **Create a feature branch** from `main`:
   ```bash
   git checkout -b feature/your-feature-name
   ```
3. **Make your changes** following the coding conventions above
4. **Write tests** for any new logic (especially protocol changes)
5. **Run the full suite**:
   ```powershell
   dotnet build
   dotnet test
   dotnet run --project DesktopDotNet\FollowMe.Cli -- smoke
   ```
6. **Commit with a clear message**:
   ```
   Add multibox position frame support

   - New frame type 3 (PlayerPosition) encodes x/y/z at int32 × 100 scale
   - Unit tests verify round-trip for positive, negative, and boundary coords
   - Lua protocol updated; C# decoder extended
   - Smoke test still passes
   ```
7. **Push to your fork** and **open a PR** against `main`

**PR requirements:**
- Clear title and description (what + why)
- All tests passing
- Code follows conventions (Lua 5.1 + RIFT API verified, C# nullable + immutable records)
- No new NuGet packages without justification
- Documented limitations or known issues

## Release Process

1. Update version in `CHANGELOG.md` with date and features
2. Update `README.md` as needed
3. Create a git tag: `git tag v0.2.0`
4. Push tag: `git push origin v0.2.0`
5. GitHub Actions will build and create a release artifact

## Support

- **Questions?** Open an issue with the `question` label
- **Stuck on setup?** See the dev setup section above
- **RIFT API questions?** Check [seebs.net](https://seebs.net/) or [rift.mestoph.net](https://rift.mestoph.net/)

---

Thanks for contributing! Together we're building a reliable, observable multibox system.
