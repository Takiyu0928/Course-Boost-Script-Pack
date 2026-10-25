@echo off
setlocal EnableExtensions
rem Build (including tests) and run both suites:
rem   1) unit/integration tests   2) UI contract smoke test (off-screen)
rem ASCII-only on purpose (cmd reads .cmd in the OEM code page).
set "HERE=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%build.ps1" -Test
if errorlevel 1 (
	echo.
	echo [FAILED] build failed
	exit /b 1
)

echo.
echo ===== unit / integration tests =====
"%HERE%build\TimeSaver.Tests.exe"
set "CODE1=%ERRORLEVEL%"

echo.
echo ===== UI contract smoke test =====
"%HERE%build\TimeSaver.Tests.exe" --ui-selftest
set "CODE2=%ERRORLEVEL%"

echo.
if not "%CODE1%"=="0" echo [FAILED] unit/integration tests
if not "%CODE2%"=="0" echo [FAILED] UI smoke test
if "%CODE1%"=="0" if "%CODE2%"=="0" echo [OK] all suites passed
if "%CODE1%"=="0" if "%CODE2%"=="0" exit /b 0
exit /b 1
