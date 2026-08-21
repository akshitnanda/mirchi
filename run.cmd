@echo off
setlocal
cd /d "%~dp0"
if not exist Mirchi.exe call build.cmd
if errorlevel 1 exit /b 1
start "" Mirchi.exe
