@echo off
rem COTK - Crown of the King: lanceur avec connexion (profil / skins / admin F8).
if exist "%~dp0COTK.Launcher.exe" (
  start "" "%~dp0COTK.Launcher.exe"
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0PLAY.ps1"
)
