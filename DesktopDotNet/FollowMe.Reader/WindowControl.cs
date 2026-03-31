using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FollowMe.Reader;

public sealed record WindowGeometry(
    int WindowLeft,
    int WindowTop,
    int WindowWidth,
    int WindowHeight,
    int ClientLeft,
    int ClientTop,
    int ClientWidth,
    int ClientHeight,
    bool IsMinimized,
    long Style,
    long ExStyle);

public sealed record WindowResizeResult(
    WindowGeometry Before,
    WindowGeometry After,
    int RequestedClientWidth,
    int RequestedClientHeight,
    bool Succeeded,
    string Reason);

public static class WindowControlService
{
    private const int SwRestore = 9;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    public static WindowGeometry DescribeWindow(nint hwnd)
    {
        var isMinimized = IsIconic(hwnd);
        var windowRect = GetWindowRectOnScreen(hwnd);
        var clientRect = TryGetClientRectOnScreen(hwnd);
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();

        return new WindowGeometry(
            windowRect.X,
            windowRect.Y,
            windowRect.Width,
            windowRect.Height,
            clientRect?.X ?? 0,
            clientRect?.Y ?? 0,
            clientRect?.Width ?? 0,
            clientRect?.Height ?? 0,
            isMinimized,
            style,
            exStyle);
    }

    public static WindowResizeResult EnsureClientSize(nint hwnd, int requestedClientWidth, int requestedClientHeight, int? left = null, int? top = null)
    {
        var before = DescribeWindow(hwnd);

        if (before.IsMinimized)
        {
            ShowWindow(hwnd, SwRestore);
            Thread.Sleep(200);
        }

        var working = DescribeWindow(hwnd);
        var targetLeft = left ?? NormalizeScreenCoordinate(working.WindowLeft, SmCxScreen);
        var targetTop = top ?? NormalizeScreenCoordinate(working.WindowTop, SmCyScreen);
        var outerSize = CalculateOuterSize(requestedClientWidth, requestedClientHeight, working.Style, working.ExStyle);

        if (!SetWindowPos(hwnd, nint.Zero, targetLeft, targetTop, outerSize.Width, outerSize.Height, SwpNoZOrder | SwpNoActivate))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPos failed.");
        }

        Thread.Sleep(100);

        var after = DescribeWindow(hwnd);
        var widthError = requestedClientWidth - after.ClientWidth;
        var heightError = requestedClientHeight - after.ClientHeight;
        if (Math.Abs(widthError) > 0 || Math.Abs(heightError) > 0)
        {
            var correctedWidth = Math.Max(after.WindowWidth + widthError, requestedClientWidth);
            var correctedHeight = Math.Max(after.WindowHeight + heightError, requestedClientHeight);
            if (!SetWindowPos(hwnd, nint.Zero, targetLeft, targetTop, correctedWidth, correctedHeight, SwpNoZOrder | SwpNoActivate))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPos correction failed.");
            }

            Thread.Sleep(100);
            after = DescribeWindow(hwnd);
        }

        var succeeded = after.ClientWidth == requestedClientWidth && after.ClientHeight == requestedClientHeight;
        var reason = succeeded
            ? "Window client area matches the requested profile."
            : $"Requested {requestedClientWidth}x{requestedClientHeight}, got {after.ClientWidth}x{after.ClientHeight}. Ensure RIFT is in windowed mode.";

        return new WindowResizeResult(before, after, requestedClientWidth, requestedClientHeight, succeeded, reason);
    }

    private static int NormalizeScreenCoordinate(int coordinate, int metric)
    {
        var screenSize = GetSystemMetrics(metric);
        if (coordinate < 0 || coordinate > screenSize)
        {
            return 32;
        }

        return coordinate;
    }

    private static Size CalculateOuterSize(int clientWidth, int clientHeight, long style, long exStyle)
    {
        var rect = new Rect
        {
            Left = 0,
            Top = 0,
            Right = clientWidth,
            Bottom = clientHeight
        };

        if (!AdjustWindowRectEx(ref rect, (int)style, false, (int)exStyle))
        {
            return new Size(clientWidth, clientHeight);
        }

        return new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static NativeRect? TryGetClientRectOnScreen(nint hwnd)
    {
        if (!GetClientRect(hwnd, out var rect))
        {
            return null;
        }

        var point = new NativePoint();
        if (!ClientToScreen(hwnd, ref point))
        {
            return null;
        }

        return new NativeRect(point.X, point.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static NativeRect GetWindowRectOnScreen(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowRect failed.");
        }

        return new NativeRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private readonly record struct NativeRect(int X, int Y, int Width, int Height);

    private readonly record struct Size(int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AdjustWindowRectEx(ref Rect rect, int style, bool hasMenu, int exStyle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(nint hwnd, int command);
}
