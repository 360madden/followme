param(
  [int]$ClientWidth = 640,
  [int]$ClientHeight = 360,
  [int]$Left = -1,
  [int]$Top = -1
)

$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class FollowMeWindowTools
{
    public const int SwRestore = 9;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const int GwlStyle = -16;
    public const int GwlExStyle = -20;
    public const int SmCxScreen = 0;
    public const int SmCyScreen = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AdjustWindowRectEx(ref Rect rect, int style, bool hasMenu, int exStyle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ClientToScreen(IntPtr hwnd, ref Point point);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShowWindow(IntPtr hwnd, int command);
}
"@

function Get-RiftWindowProcess {
  $candidates = Get-Process | Where-Object {
    $_.MainWindowHandle -ne 0
  }

  $directMatch = $candidates | Where-Object {
    $_.ProcessName -match '^rift(_x64)?$'
  } | Select-Object -First 1

  if ($null -ne $directMatch) {
    return $directMatch
  }

  $titleMatch = $candidates | Where-Object {
    ($_.MainWindowTitle -match '^RIFT(\b|$)') -and
    ($_.MainWindowTitle -notmatch 'Minion')
  } | Select-Object -First 1

  if ($null -ne $titleMatch) {
    return $titleMatch
  }

  return $null
}

function Get-WindowGeometry {
  param(
    [System.IntPtr]$Handle
  )

  $windowRect = New-Object FollowMeWindowTools+Rect
  if (-not [FollowMeWindowTools]::GetWindowRect($Handle, [ref]$windowRect)) {
    throw "GetWindowRect failed."
  }

  $clientRect = New-Object FollowMeWindowTools+Rect
  $clientPoint = New-Object FollowMeWindowTools+Point
  $hasClient = [FollowMeWindowTools]::GetClientRect($Handle, [ref]$clientRect) -and [FollowMeWindowTools]::ClientToScreen($Handle, [ref]$clientPoint)

  $style = [FollowMeWindowTools]::GetWindowLongPtr($Handle, [FollowMeWindowTools]::GwlStyle).ToInt64()
  $exStyle = [FollowMeWindowTools]::GetWindowLongPtr($Handle, [FollowMeWindowTools]::GwlExStyle).ToInt64()

  return [pscustomobject]@{
    WindowLeft = $windowRect.Left
    WindowTop = $windowRect.Top
    WindowWidth = $windowRect.Right - $windowRect.Left
    WindowHeight = $windowRect.Bottom - $windowRect.Top
    ClientLeft = if ($hasClient) { $clientPoint.X } else { 0 }
    ClientTop = if ($hasClient) { $clientPoint.Y } else { 0 }
    ClientWidth = if ($hasClient) { $clientRect.Right - $clientRect.Left } else { 0 }
    ClientHeight = if ($hasClient) { $clientRect.Bottom - $clientRect.Top } else { 0 }
    IsMinimized = [FollowMeWindowTools]::IsIconic($Handle)
    Style = $style
    ExStyle = $exStyle
  }
}

function Normalize-ScreenCoordinate {
  param(
    [int]$Coordinate,
    [int]$Metric
  )

  $screenSize = [FollowMeWindowTools]::GetSystemMetrics($Metric)
  if ($Coordinate -lt 0 -or $Coordinate -gt $screenSize) {
    return 32
  }

  return $Coordinate
}

function Get-OuterSize {
  param(
    [int]$RequestedClientWidth,
    [int]$RequestedClientHeight,
    [long]$Style,
    [long]$ExStyle
  )

  $rect = New-Object FollowMeWindowTools+Rect
  $rect.Left = 0
  $rect.Top = 0
  $rect.Right = $RequestedClientWidth
  $rect.Bottom = $RequestedClientHeight

  if (-not [FollowMeWindowTools]::AdjustWindowRectEx([ref]$rect, [int]$Style, $false, [int]$ExStyle)) {
    return [pscustomobject]@{
      Width = $RequestedClientWidth
      Height = $RequestedClientHeight
    }
  }

  return [pscustomobject]@{
    Width = $rect.Right - $rect.Left
    Height = $rect.Bottom - $rect.Top
  }
}

$process = Get-RiftWindowProcess
if ($null -eq $process) {
  Write-Error "No RIFT window was found. Start the game in windowed mode first."
}

$handle = [System.IntPtr]$process.MainWindowHandle
$before = Get-WindowGeometry -Handle $handle

if ($before.IsMinimized) {
  [void][FollowMeWindowTools]::ShowWindow($handle, [FollowMeWindowTools]::SwRestore)
  Start-Sleep -Milliseconds 200
}

$working = Get-WindowGeometry -Handle $handle
$targetLeft = if ($Left -ge 0) { $Left } else { Normalize-ScreenCoordinate -Coordinate $working.WindowLeft -Metric ([FollowMeWindowTools]::SmCxScreen) }
$targetTop = if ($Top -ge 0) { $Top } else { Normalize-ScreenCoordinate -Coordinate $working.WindowTop -Metric ([FollowMeWindowTools]::SmCyScreen) }
$outerSize = Get-OuterSize -RequestedClientWidth $ClientWidth -RequestedClientHeight $ClientHeight -Style $working.Style -ExStyle $working.ExStyle

if (-not [FollowMeWindowTools]::SetWindowPos($handle, [System.IntPtr]::Zero, $targetLeft, $targetTop, $outerSize.Width, $outerSize.Height, [FollowMeWindowTools]::SwpNoZOrder -bor [FollowMeWindowTools]::SwpNoActivate)) {
  throw "SetWindowPos failed."
}

Start-Sleep -Milliseconds 100

$after = Get-WindowGeometry -Handle $handle
$widthError = $ClientWidth - $after.ClientWidth
$heightError = $ClientHeight - $after.ClientHeight

if ($widthError -ne 0 -or $heightError -ne 0) {
  $correctedWidth = [Math]::Max($after.WindowWidth + $widthError, $ClientWidth)
  $correctedHeight = [Math]::Max($after.WindowHeight + $heightError, $ClientHeight)

  if (-not [FollowMeWindowTools]::SetWindowPos($handle, [System.IntPtr]::Zero, $targetLeft, $targetTop, $correctedWidth, $correctedHeight, [FollowMeWindowTools]::SwpNoZOrder -bor [FollowMeWindowTools]::SwpNoActivate)) {
    throw "SetWindowPos correction failed."
  }

  Start-Sleep -Milliseconds 100
  $after = Get-WindowGeometry -Handle $handle
}

Write-Host ("RIFT window: {0}" -f $process.MainWindowTitle) -ForegroundColor Cyan
Write-Host ("Before client area: {0}x{1}" -f $before.ClientWidth, $before.ClientHeight)
Write-Host ("After client area:  {0}x{1}" -f $after.ClientWidth, $after.ClientHeight)

if ($after.ClientWidth -eq $ClientWidth -and $after.ClientHeight -eq $ClientHeight) {
  Write-Host ("Resize succeeded at {0},{1} -> {2}x{3} client area." -f $targetLeft, $targetTop, $ClientWidth, $ClientHeight) -ForegroundColor Green
  exit 0
}

Write-Error ("Resize did not hit the requested {0}x{1} client area. Got {2}x{3}. Make sure RIFT is in windowed mode." -f $ClientWidth, $ClientHeight, $after.ClientWidth, $after.ClientHeight)
