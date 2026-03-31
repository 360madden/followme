@echo off
setlocal

set "PWSH="
for %%I in (pwsh.exe) do set "PWSH=%%~$PATH:I"
if not defined PWSH if exist "%ProgramFiles%\PowerShell\7\pwsh.exe" set "PWSH=%ProgramFiles%\PowerShell\7\pwsh.exe"
if not defined PWSH if exist "%ProgramFiles%\PowerShell\7-preview\pwsh.exe" set "PWSH=%ProgramFiles%\PowerShell\7-preview\pwsh.exe"

if defined PWSH (
    "%PWSH%" -ExecutionPolicy Bypass -File "%~dp0Run-FollowMe.ps1" %*
) else (
    powershell -ExecutionPolicy Bypass -File "%~dp0Run-FollowMe.ps1" %*
)

exit /b %ERRORLEVEL%
