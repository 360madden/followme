using System.Diagnostics;
using System.Runtime.InteropServices;
using FollowMe.MultiBox.Config;
using FollowMe.MultiBox.Interfaces;
using FollowMe.MultiBox.State;

namespace FollowMe.MultiBox.Controllers;

/// <summary>
/// Issues /assist commands via Win32 SendInput when the leader's hostile target changes.
/// Debounced by TargetAssistDebounceMs to avoid spamming during rapid targeting.
///
/// NilRisk: leader.MultiBox.TargetName may be empty — checked before firing.
/// NilRisk: _lastAssistedTarget may not match current target — handled explicitly.
/// </summary>
public sealed class Win32TargetController : ITargetController
{
    private readonly MultiBoxConfig _config;
    private readonly Stopwatch _debounceTimer = Stopwatch.StartNew();
    private bool _disposed;

    public string? LastAssistedTarget { get; private set; }
    public DateTimeOffset? LastAssistTime { get; private set; }

    public Win32TargetController(MultiBoxConfig config)
    {
        _config = config;
    }

    public void Update(LeaderState leader)
    {
        if (_disposed || !_config.EnableTargetAssist) return;

        var mb = leader.MultiBox;

        // Only assist on hostile targets
        if (!mb.HasTarget || !mb.TargetHostile) return;
        if (string.IsNullOrEmpty(mb.TargetName)) return;

        // Skip if same target and debounce hasn't elapsed
        var targetChanged = mb.TargetName != LastAssistedTarget;
        var debounceElapsed = _debounceTimer.ElapsedMilliseconds >= _config.TargetAssistDebounceMs;

        if (!targetChanged && !debounceElapsed) return;

        InjectAssistCommand(_config.LeaderName);
        LastAssistedTarget = mb.TargetName;
        LastAssistTime = DateTimeOffset.UtcNow;
        _debounceTimer.Restart();
    }

    public void Dispose()
    {
        _disposed = true;
    }

    // ── Win32 input ───────────────────────────────────────────────────────────

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_RETURN = 0x0D;

    private static void InjectAssistCommand(string leaderName)
    {
        var command = $"/assist {leaderName}";

        // Open chat
        SendKeyDown(VK_RETURN);
        SendKeyUp(VK_RETURN);
        Thread.Sleep(50);

        // Type command
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

        // Execute
        SendKeyDown(VK_RETURN);
        SendKeyUp(VK_RETURN);
    }

    private static void SendKeyDown(ushort vk)
    {
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].ki.wVk = vk;
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyUp(ushort vk)
    {
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].ki.wVk = vk;
        inputs[0].ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        private readonly long _padding;
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
