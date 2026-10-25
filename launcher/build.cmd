@echo off
setlocal EnableExtensions
rem Build the launcher with the in-box .NET Framework 4.8 compiler.
rem ASCII-only on purpose (cmd reads .cmd in the OEM code page).
set "HERE=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%build.ps1" %*
set "CODE=%ERRORLEVEL%"
echo.
if not "%CODE%"=="0" echo [FAILED] build exit code %CODE%
exit /b %CODE%
