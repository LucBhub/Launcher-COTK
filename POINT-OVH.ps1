# Repointe le launcher et le serveur de jeu vers le backend OVH.
# Usage :
#   powershell -ExecutionPolicy Bypass -File launcher\POINT-OVH.ps1 -Url https://api.ton-domaine.fr -Key <INTERNAL_API_KEY du .env OVH>
# Revenir en local :
#   powershell -ExecutionPolicy Bypass -File launcher\POINT-OVH.ps1 -Local
param(
    [string]$Url,
    [string]$Key,
    [switch]$Local
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..").Path

if ($Local) {
    [Environment]::SetEnvironmentVariable('COTK_API_URL', $null, 'User')
    [Environment]::SetEnvironmentVariable('H1Z1_WEB_API', 'http://localhost:8080', 'User')
    Write-Host 'Retour en local : launcher + jeu pointent sur http://localhost:8080 (COTK.API.exe).' -ForegroundColor Green
    exit 0
}

if (-not $Url) { throw 'Fournis -Url https://... (ou -Local pour revenir au local)' }
if ($Url -notmatch '^https://') { throw "L'URL doit etre en HTTPS : $Url" }

# 1. Launcher + serveur de jeu (variables utilisateur, heritees par tout nouveau process)
[Environment]::SetEnvironmentVariable('COTK_API_URL', $Url.TrimEnd('/'), 'User')
[Environment]::SetEnvironmentVariable('H1Z1_WEB_API', $Url.TrimEnd('/'), 'User')
if ($Key) {
    [Environment]::SetEnvironmentVariable('H1Z1_SERVER_KEY', $Key.Trim(), 'User')
}

# 2. Start-H1Z1.cmd : la ligne H1Z1_WEB_API gravee est mise a jour aussi
$cmd = Join-Path $repo 'server\H1Z1-2017-CSharp-Server\Start-H1Z1.cmd'
if (Test-Path $cmd) {
    $c = [System.IO.File]::ReadAllText($cmd)
    $c = [regex]::Replace($c, 'set "H1Z1_WEB_API=[^"]*"', "set `"H1Z1_WEB_API=$($Url.TrimEnd('/'))`"")
    if ($Key) { $c = [regex]::Replace($c, 'set "H1Z1_SERVER_KEY=[^"]*"', "set `"H1Z1_SERVER_KEY=$($Key.Trim())`"") }
    [System.IO.File]::WriteAllText($cmd, $c, (New-Object System.Text.UTF8Encoding($false)))
}

Write-Host "Local pointe vers $Url" -ForegroundColor Green
Write-Host 'Ferme et relance le launcher + JOUE.bat pour prendre en compte.'
Write-Host 'Panneau admin : $Url/admin (navigateur, session du site).'
