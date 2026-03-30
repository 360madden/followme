@echo off
setlocal
call "%~dp0Run-FollowMe.cmd" -Mode smoke
exit /b %ERRORLEVEL%
