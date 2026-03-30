@echo off
setlocal
set "REPO_ROOT=%~dp0.."
dotnet run --project "%REPO_ROOT%\DesktopDotNet\FollowMe.Inspector\FollowMe.Inspector.csproj" %*
exit /b %ERRORLEVEL%
