@echo off
setlocal
call "%~dp0Run-FollowMe.cmd" -Mode bench
exit /b %ERRORLEVEL%
