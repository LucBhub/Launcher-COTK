@echo off
if not exist "%~dp0out\COTK.Launcher.Next.exe" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
  if errorlevel 1 exit /b 1
)
start "" "%~dp0out\COTK.Launcher.Next.exe" --preview
