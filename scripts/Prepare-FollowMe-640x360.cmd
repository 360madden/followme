@echo off
setlocal
call "%~dp0Run-FollowMe.cmd" -Mode prepare-window -Argument1 32 -Argument2 32
exit /b %ERRORLEVEL%
