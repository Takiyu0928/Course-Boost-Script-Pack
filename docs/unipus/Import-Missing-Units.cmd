@echo off
setlocal
title UnipusAI - Import missing units
set "tsUnipusDir=%~dp0"
if exist "%tsUnipusDir%UnipusAI.exe" goto found
set "tsUnipusDir=%~dp0UnipusAI-v3.6-windows-x86-64\"
if exist "%tsUnipusDir%UnipusAI.exe" goto found
echo UnipusAI.exe was not found.
echo Place this CMD inside the UnipusAI folder or beside that folder.
set "tsImportExit=2"
goto done

:found
pushd "%tsUnipusDir%"
if errorlevel 1 (
  echo Could not open the UnipusAI folder.
  set "tsImportExit=2"
  goto done
)
if not exist "config.json" (
  echo config.json was not found. Configure and save UnipusAI first.
  set "tsImportExit=2"
  popd
  goto done
)
echo Close other UnipusAI instances before continuing.
echo Incremental content import. Existing database is not cleared.
echo This command does not run answering or coursework submission.
echo Media transcription may download local model files if enabled.
echo.
".\UnipusAI.exe" dump-text
set "tsImportExit=%errorlevel%"
popd

:done
echo.
echo Exit code: %tsImportExit%
echo Keep the final summary or error text if you need help.
if /i not "%~1"=="--no-pause" pause
exit /b %tsImportExit%
