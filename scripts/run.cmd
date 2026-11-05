@echo off
rem Double-click to build the tool and open its window with normal rights.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" -NoElevate
if errorlevel 1 pause
