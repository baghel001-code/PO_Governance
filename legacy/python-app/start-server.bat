@echo off
rem Runs the VMM PO Governance server in this window (Ctrl+C or close to stop).
rem For an always-on setup use install-service.ps1 instead.
cd /d "%~dp0"
setlocal EnableDelayedExpansion

set "PY="
rem 1) Python unzipped into a "python" folder next to this file
if exist "%~dp0python\python.exe" set "PY=%~dp0python\python.exe"
rem 2) Usual install locations (per-user and all-users installs), newest first
if not defined PY for /f "delims=" %%D in ('dir /b /ad /o-n "%LOCALAPPDATA%\Programs\Python\Python3*" 2^>nul') do if not defined PY if exist "%LOCALAPPDATA%\Programs\Python\%%D\python.exe" set "PY=%LOCALAPPDATA%\Programs\Python\%%D\python.exe"
if not defined PY for /f "delims=" %%D in ('dir /b /ad /o-n "%ProgramFiles%\Python3*" 2^>nul') do if not defined PY if exist "%ProgramFiles%\%%D\python.exe" set "PY=%ProgramFiles%\%%D\python.exe"
if not defined PY for /f "delims=" %%D in ('dir /b /ad /o-n "C:\Python3*" 2^>nul') do if not defined PY if exist "C:\%%D\python.exe" set "PY=C:\%%D\python.exe"
rem    ...also Python installed under another Windows user's profile
if not defined PY for /d %%U in ("C:\Users\*") do for /d %%D in ("%%U\AppData\Local\Programs\Python\Python3*") do if not defined PY if exist "%%D\python.exe" set "PY=%%D\python.exe"
rem 3) The "py" launcher or python on PATH (skipping the Microsoft Store shortcut)
if not defined PY for /f "delims=" %%P in ('py -3 -c "import sys;print(sys.executable)" 2^>nul') do set "PY=%%P"
if not defined PY for /f "delims=" %%P in ('where python 2^>nul ^| findstr /v /i "WindowsApps"') do if not defined PY set "PY=%%P"

if not defined PY (
  echo.
  echo Python was not found on this computer.
  echo Either install Python 3.8 or newer from python.org, or unzip the Windows
  echo "embeddable package" of Python into a folder named "python" next to this file.
  echo See README.md, section 1.
  echo.
  pause
  exit /b 1
)

echo Using Python: !PY!
echo Starting VMM PO Governance server...  (settings: config.json, log: logs\server.log)
echo Keep this window open. Close it to stop the portal.
echo.
"!PY!" "%~dp0server.py"
echo.
echo The server stopped. If there is an error above, send a screenshot of this window.
pause
