# Desinstalle le launcher COTK (fichiers + raccourcis + entree registre).
# Les donnees joueur (client, sessions) sont conservees sauf -Purge.

param([switch]$Purge)

$ErrorActionPreference = 'SilentlyContinue'
$dest = Join-Path $env:LOCALAPPDATA 'COTK'

Get-Process COTK.Launcher -ErrorAction SilentlyContinue | Stop-Process -Force

Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'COTK.lnk') -Force
Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\COTK.lnk" -Force
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\COTK Launcher' -Recurse -Force

if ($Purge) {
    Remove-Item $dest -Recurse -Force
    Write-Host 'COTK desinstalle : fichiers et donnees joueur supprimes.'
} else {
    # Tout supprimer sauf les donnees joueur (client/, launcher/data/)
    Get-ChildItem $dest -Force | Where-Object { $_.Name -notin @('client', 'data') } |
        Remove-Item -Recurse -Force
    Write-Host 'COTK desinstalle. Le client et vos donnees locales sont conserves dans' $dest
}

if ($MyInvocation.InvocationName -ne 'uninstall.ps1') { }
Read-Host 'Appuyez sur Entree pour fermer'
