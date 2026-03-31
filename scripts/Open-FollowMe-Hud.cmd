@echo off
REM Version: 0.2.1
REM Purpose: Launch the FollowMe live player-stats HUD.
cd /d "%~dp0.."
dotnet run --project .\DesktopDotNet\FollowMe.Hud\FollowMe.Hud.csproj
REM End of script.
