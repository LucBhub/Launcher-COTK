# Installer COTK - copie le launcher, cree les raccourcis et l'entree de
# desinstallation. Lance par Install-COTK.bat depuis le zip extrait.

$ErrorActionPreference = 'Stop'

$source = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $env:LOCALAPPDATA 'COTK'

Write-Host '=== Installation du launcher COTK ===' -ForegroundColor Cyan

# Ferme une instance en cours pour ne pas copier sur des fichiers verrouilles
Get-Process COTK.Launcher -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Fermeture du launcher en cours (PID $($_.Id))..."
    $_.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 800
    if (-not $_.HasExited) { Stop-Process -Id $_.Id -Force }
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null

# Fichiers runtime du launcher (exe, dll, deps...) + assets embarques
Copy-Item "$source\COTK.Launcher.*" $dest -Force
foreach ($name in 'fonts', 'data') {
    if (Test-Path (Join-Path $source $name)) {
        Copy-Item (Join-Path $source $name) $dest -Recurse -Force
    }
}
foreach ($name in 'JOUE.bat', 'ClientConfig.example.ini', 'README.md') {
    if (Test-Path (Join-Path $source $name)) {
        Copy-Item (Join-Path $source $name) $dest -Force
    }
}
if (Test-Path "$source\uninstall.ps1") {
    Copy-Item "$source\uninstall.ps1" $dest -Force
}

# Raccourci Bureau + Menu Demarrer
$shell = New-Object -ComObject WScript.Shell
$targets = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'COTK.lnk'),
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\COTK.lnk')
)
foreach ($lnk in $targets) {
    $shortcut = $shell.CreateShortcut($lnk)
    $shortcut.TargetPath = Join-Path $dest 'COTK.Launcher.exe'
    $shortcut.WorkingDirectory = $dest
    $shortcut.IconLocation = Join-Path $dest 'COTK.Launcher.exe,0'
    $shortcut.Description = 'COTK - Crown of the King'
    $shortcut.Save()
}

# Entree Ajout/Suppression de programmes (per-user)
$uninstall = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\COTK Launcher'
$version = (Get-Item "$dest\COTK.Launcher.exe").VersionInfo.ProductVersion -split '\+' | Select-Object -First 1
New-Item -Force -Path $uninstall | Out-Null
Set-ItemProperty $uninstall -Name DisplayName -Value 'COTK Launcher'
Set-ItemProperty $uninstall -Name DisplayVersion -Value $version
Set-ItemProperty $uninstall -Name DisplayIcon -Value (Join-Path $dest 'COTK.Launcher.exe')
Set-ItemProperty $uninstall -Name Publisher -Value 'COTK'
Set-ItemProperty $uninstall -Name InstallLocation -Value $dest
Set-ItemProperty $uninstall -Name NoModify -Value 1 -Type DWord
Set-ItemProperty $uninstall -Name NoRepair -Value 1 -Type DWord
$uninstCmd = 'powershell -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $dest 'uninstall.ps1') + '"'
Set-ItemProperty $uninstall -Name UninstallString -Value $uninstCmd

Write-Host "Installe dans $dest" -ForegroundColor Green
Write-Host 'Raccourcis : Bureau + Menu Demarrer. Desinstallation via Parametres Windows.'
$launch = Read-Host 'Lancer le launcher maintenant ? [O/n]'
if ($launch -notmatch '^[nN]') {
    Start-Process (Join-Path $dest 'COTK.Launcher.exe') -WorkingDirectory $dest
}
