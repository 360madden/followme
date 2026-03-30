using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FollowMe.Reader;

public enum CaptureBackend
{
    DesktopDuplication,
    ScreenBitBlt,
    PrintWindow
}

public readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}

internal readonly record struct CaptureTarget(
    nint WindowHandle,
    ScreenRect ClientRect,
    ScreenRect WindowRect,
    ScreenRect SourceRect,
    nint MonitorHandle);

public sealed record CaptureResult(
    Bgr24Frame Image,
    CaptureBackend Backend,
    int ClientX,
    int ClientY,
    int ClientWidth,
    int ClientHeight,
    int CaptureLeft,
    int CaptureTop,
    int CaptureWidth,
    int CaptureHeight,
    string RouteReason);

public static class WindowCaptureService
{
    private const uint MonitorDefaultToNearest = 2;
    private static bool _dpiAwarenessInitialized;

    public static nint FindRiftWindow()
    {
        var process = Process
            .GetProcesses()
            .FirstOrDefault(static candidate =>
                candidate.MainWindowHandle != nint.Zero &&
                (candidate.ProcessName.Equals("rift", StringComparison.OrdinalIgnoreCase)
                 || candidate.ProcessName.Equals("rift_x64", StringComparison.OrdinalIgnoreCase)));

        process ??= Process
            .GetProcesses()
            .FirstOrDefault(static candidate =>
                candidate.MainWindowHandle != nint.Zero &&
                candidate.MainWindowTitle.StartsWith("RIFT", StringComparison.OrdinalIgnoreCase) &&
                !candidate.MainWindowTitle.Contains("Minion", StringComparison.OrdinalIgnoreCase) &&
                !candidate.MainWindowTitle.Contains("Glyph", StringComparison.OrdinalIgnoreCase));

        return process?.MainWindowHandle ?? nint.Zero;
    }

    public static CaptureResult CaptureTopSlice(nint hwnd, StripProfile profile, int heightPadding, CaptureBackend backend)
    {
        EnsurePerMonitorDpiAwareness();

        var target = ResolveCaptureTarget(hwnd, profile);
        var captureHeight = Math.Min(target.SourceRect.Height, Math.Max(profile.BandHeight, profile.BandHeight + heightPadding));

        return backend switch
        {
            CaptureBackend.DesktopDuplication => DesktopDuplicationCaptureBackend.CaptureTopSlice(target, captureHeight, backend),
            CaptureBackend.ScreenBitBlt => CaptureScreen(target.SourceRect, target.ClientRect, captureHeight, backend),
            CaptureBackend.PrintWindow => CapturePrintWindow(hwnd, target.ClientRect, captureHeight, backend),
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };
    }

    internal static CaptureTarget ResolveCaptureTarget(nint hwnd, StripProfile profile)
    {
        if (IsIconic(hwnd))
        {
            throw new InvalidOperationException("The RIFT window is minimized.");
        }

        var clientRect = TryGetClientRectOnScreen(hwnd);
        if (clientRect is null || clientRect.Value.Width <= 0 || clientRect.Value.Height <= 0)
        {
            throw new InvalidOperationException("Could not resolve the RIFT client rectangle on screen.");
        }

        var resolvedClient = clientRect.Value;
        if (resolvedClient.Width < profile.BandWidth || resolvedClient.Height < profile.BandHeight)
        {
            throw new InvalidOperationException(
                $"The RIFT client rectangle is too small for {profile.Id}: {resolvedClient.Width}x{resolvedClient.Height}.");
        }

        var windowRect = GetWindowRectOnScreen(hwnd);
        var monitorHandle = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitorHandle == nint.Zero)
        {
            throw new InvalidOperationException("Could not determine the monitor that hosts the RIFT window.");
        }

        return new CaptureTarget(hwnd, resolvedClient, windowRect, resolvedClient, monitorHandle);
    }

    internal static ScreenRect? TryGetClientRectOnScreen(nint hwnd)
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

        return new ScreenRect(point.X, point.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    internal static ScreenRect GetWindowRectOnScreen(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowRect failed.");
        }

        return new ScreenRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static void EnsurePerMonitorDpiAwareness()
    {
        if (_dpiAwarenessInitialized)
        {
            return;
        }

        _dpiAwarenessInitialized = true;
        _ = SetProcessDpiAwarenessContext(new nint(-4));
    }

    private static CaptureResult CaptureScreen(ScreenRect captureRect, ScreenRect clientRect, int captureHeight, CaptureBackend backend)
    {
        var image = CaptureBitmap(captureRect.Width, captureHeight, (hdc) =>
        {
            var screenDc = GetDC(nint.Zero);
            try
            {
                if (!BitBlt(hdc, 0, 0, captureRect.Width, captureHeight, screenDc, captureRect.Left, captureRect.Top, 0x40CC0020))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt failed.");
                }
            }
            finally
            {
                _ = ReleaseDC(nint.Zero, screenDc);
            }
        });

        return new CaptureResult(
            image with { CaptureRouteReason = "screen" },
            backend,
            clientRect.Left,
            clientRect.Top,
            clientRect.Width,
            clientRect.Height,
            captureRect.Left,
            captureRect.Top,
            captureRect.Width,
            captureHeight,
            "screen");
    }

    private static CaptureResult CapturePrintWindow(nint hwnd, ScreenRect clientRect, int captureHeight, CaptureBackend backend)
    {
        var full = CaptureBitmap(clientRect.Width, clientRect.Height, (hdc) =>
        {
            if (!PrintWindow(hwnd, hdc, 0x00000003) && !PrintWindow(hwnd, hdc, 0x00000001))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "PrintWindow failed.");
            }
        });

        var cropped = full.Crop(0, 0, clientRect.Width, captureHeight, "printwindow") with { CaptureRouteReason = "printwindow" };
        return new CaptureResult(
            cropped,
            backend,
            clientRect.Left,
            clientRect.Top,
            clientRect.Width,
            clientRect.Height,
            clientRect.Left,
            clientRect.Top,
            cropped.Width,
            cropped.Height,
            "printwindow");
    }

    internal static Bgr24Frame CaptureBitmap(int width, int height, Action<nint> drawAction)
    {
        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC failed.");
        }

        var memoryDc = CreateCompatibleDC(screenDc);
        if (memoryDc == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateCompatibleDC failed.");
        }

        var bitmap = CreateCompatibleBitmap(screenDc, width, height);
        if (bitmap == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateCompatibleBitmap failed.");
        }

        var oldBitmap = SelectObject(memoryDc, bitmap);
        try
        {
            drawAction(memoryDc);
            var paddedStride = ((width * 3) + 3) & ~3;
            var bytes = new byte[paddedStride * height];
            var info = new BitmapInfo
            {
                bmiHeader = new BitmapInfoHeader
                {
                    biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    biWidth = width,
                    biHeight = height,
                    biPlanes = 1,
                    biBitCount = 24,
                    biCompression = 0,
                    biSizeImage = (uint)bytes.Length
                }
            };

            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var result = GetDIBits(memoryDc, bitmap, 0, (uint)height, handle.AddrOfPinnedObject(), ref info, 0);
                if (result != height)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDIBits failed.");
                }
            }
            finally
            {
                handle.Free();
            }

            return Bgr24Frame.FromPaddedBottomUpRows(width, height, bytes, "capture");
        }
        finally
        {
            _ = SelectObject(memoryDc, oldBitmap);
            _ = DeleteObject(bitmap);
            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(nint.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;
        public uint bmiColors;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleBitmap(nint hdc, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(nint hdcDest, int xDest, int yDest, int width, int height, nint hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(nint hdc, nint hBitmap, uint start, uint lines, nint bits, ref BitmapInfo info, uint usage);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint value);
}
