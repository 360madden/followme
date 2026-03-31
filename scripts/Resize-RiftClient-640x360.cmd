@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Resize-RiftClient-640x360.ps1" -ClientWidth 640 -ClientHeight 360
exit /b %ERRORLEVEL%
