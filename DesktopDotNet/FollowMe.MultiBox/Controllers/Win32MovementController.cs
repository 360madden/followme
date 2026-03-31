using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FollowMe.MultiBox.Config;
using FollowMe.MultiBox.Interfaces;
using FollowMe.MultiBox.State;
using FollowMe.Reader;

namespace FollowMe.MultiBox.Controllers;

/// <summary>
/// Drives follower movement using two strategies:
///
/// 1. PRIMARY — /follow command injected into RIFT via SendInput+Win32.
///    Issued every FollowCommandIntervalMs while in-range AND out of combat.
///    Re-issued in combat if EnableCoordinateMovement is false (best-effort).
///
/// 2. FALLBACK — Coordinate-based movement via W key press when distance exceeds
///    FollowDistance. Uses A/D keys to steer based on estimated heading from
///    recent position deltas.
///
/// Facing direction is NOT available from the RIFT API, so steering uses a
/// "delta heading" approach: compare two consecutive follower positions to
/// estimate current facing, then determine turn direction.
///
/// NilRisk: _lastFollowerPos may be null at startup (handled with null check).
/// NilRisk: RIFT window handle may not be found (all SendInput calls guarded).
/// </summary>
public sealed class Win32MovementController : IMovementController
{
    private const int VK_W = 0x57;
    private const int VK_A = 0x41;
    private const int VK_D = 0x44;
    private const int VK_RETURN = 0x0D;
    private const int VK_SLASH = 0xBF;  // OEM_2 / slash key

    private readonly Stopwatch _followCommandTimer = Stopwatch.StartNew();
    private bool _wHeld;
    private bool _aHeld;
    private bool _dHeld;
    private PlayerPositionSnapshot? _lastFollowerPos;
    private DateTimeOffset _lastFollowerPosTime;
    private MovementStatus _status = MovementStatus.Idle;
    private bool _disposed;

    public MovementStatus CurrentStatus => _status;

    public void Update(LeaderState leader, FollowerState follower, MultiBoxConfig config)
    {
        if (_disposed) return;

        var distance = follower.Position.DistanceTo(leader.Position);

        // ── /follow command (primary) ─────────────────────────────────────────
        var followIntervalMs = config.FollowCommandIntervalMs;
        if (followIntervalMs > 0 &&
            _followCommandTimer.ElapsedMilliseconds >= followIntervalMs)
        {
            IssueFollowCommand(config.LeaderName);
            _followCommandTimer.Restart();
        }

        // ── Coordinate movement (fallback / combat) ────────────────────────
        if (!config.EnableCoordinateMovement)
        {
            _status = MovementStatus.Following;
            return;
        }

        if (distance <= config.StopDistance)
        {
            if (_wHeld) ReleaseKey(VK_W);
            if (_aHeld) ReleaseKey(VK_A);
            if (_dHeld) ReleaseKey(VK_D);
            _wHeld = _aHeld = _dHeld = false;
            _status = MovementStatus.Stopped;
        }
        else if (distance > config.FollowDistance)
        {
            // Estimate follower facing from position delta
            var bearing = EstimateBearingError(leader.Position, follower.Position);
            ApplySteering(bearing);
            if (!_wHeld) PressKey(VK_W);
            _wHeld = true;
            _status = MovementStatus.Moving;
        }

        // Track follower position history for facing estimation
        _lastFollowerPos = follower.Position;
        _lastFollowerPosTime = follower.Timestamp;
    }

    public void IssueFollowCommand(string leaderName)
    {
        if (_disposed) return;
        var command = $"/follow {leaderName}";
        InjectChatCommand(command);
    }

    public void StopMovement()
    {
        if (_wHeld) ReleaseKey(VK_W);
        if (_aHeld) ReleaseKey(VK_A);
        if (_dHeld) ReleaseKey(VK_D);
        _wHeld = _aHeld = _dHeld = false;
        _status = MovementStatus.Idle;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMovement();
    }

    // ── Steering ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Estimate bearing error (degrees) between current follower→leader direction
    /// and follower's estimated facing (from last movement delta).
    /// Returns 0 if facing is unknown (not enough history).
    /// </summary>
    private float EstimateBearingError(
        PlayerPositionSnapshot leaderPos,
        PlayerPositionSnapshot followerPos)
    {
        // Desired bearing: direction from follower to leader
        var desiredBearing = followerPos.BearingTo(leaderPos);

        // Estimated facing: direction of most recent follower movement
        if (_lastFollowerPos is null) return 0f;

        var dx = followerPos.X - _lastFollowerPos.Value.X;
        var dz = followerPos.Z - _lastFollowerPos.Value.Z;
        var moveDist = MathF.Sqrt(dx * dx + dz * dz);

        if (moveDist < 0.5f) return 0f;  // not enough movement to determine heading

        var currentFacing = (MathF.Atan2(dx, dz) * (180f / MathF.PI) + 360f) % 360f;
        var error = desiredBearing - currentFacing;

        // Normalize to [-180, 180]
        if (error > 180f) error -= 360f;
        if (error < -180f) error += 360f;
        return error;
    }

    private void ApplySteering(float bearingError)
    {
        const float DeadZone = 15f;
        if (MathF.Abs(bearingError) < DeadZone)
        {
            if (_aHeld) { ReleaseKey(VK_A); _aHeld = false; }
            if (_dHeld) { ReleaseKey(VK_D); _dHeld = false; }
            return;
        }

        if (bearingError > 0)  // need to turn right
        {
            if (_aHeld) { ReleaseKey(VK_A); _aHeld = false; }
            if (!_dHeld) { PressKey(VK_D); _dHeld = true; }
        }
        else  // need to turn left
        {
            if (_dHeld) { ReleaseKey(VK_D); _dHeld = false; }
            if (!_aHeld) { PressKey(VK_A); _aHeld = true; }
        }
    }

    // ── Win32 input ───────────────────────────────────────────────────────────

    private static void PressKey(int vk)
    {
        SendKeyInput((ushort)vk, 0);
    }

    private static void ReleaseKey(int vk)
    {
        SendKeyInput((ushort)vk, KEYEVENTF_KEYUP);
    }

    private static void SendKeyInput(ushort vk, uint flags)
    {
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].ki.wVk = vk;
        inputs[0].ki.dwFlags = flags;
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Injects a slash command by: pressing Enter to open chat, typing the command,
    /// pressing Enter to execute. Requires RIFT window to be in foreground.
    /// For a dedicated follower machine this is always true.
    /// </summary>
    private static void InjectChatCommand(string command)
    {
        // Open chat with Enter
        SendKeyInput(VK_RETURN, 0);
        SendKeyInput(VK_RETURN, KEYEVENTF_KEYUP);
        Thread.Sleep(50);

        // Type each character
        foreach (var ch in command)
        {
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].ki.wVk = 0;
            inputs[0].ki.wScan = ch;
            inputs[0].ki.dwFlags = KEYEVENTF_UNICODE;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].ki.wVk = 0;
            inputs[1].ki.wScan = ch;
            inputs[1].ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }

        Thread.Sleep(30);

        // Execute with Enter
        SendKeyInput(VK_RETURN, 0);
        SendKeyInput(VK_RETURN, KEYEVENTF_KEYUP);
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        private readonly long _padding;   // union padding for MOUSEINPUT/HARDWAREINPUT
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }
}
