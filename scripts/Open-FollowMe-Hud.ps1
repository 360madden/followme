# Version: 0.2.1
# Purpose: Launch the FollowMe live player-stats HUD.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project .\DesktopDotNet\FollowMe.Hud\FollowMe.Hud.csproj
# End of script.
