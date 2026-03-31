@echo off
setlocal
call "%~dp0Run-FollowMe.cmd" -Mode live -Argument1 10 -Argument2 100
exit /b %ERRORLEVEL%
