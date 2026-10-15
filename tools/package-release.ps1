# Empaquette le launcher publie (dotnet publish ... --self-contained) en zip + SHA-256.
# Usage : powershell -File tools\package-release.ps1 -PublishDir publish -Version 1.0.0
param(
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$Version,
    [string]$OutputDir = ''
)
$ErrorActionPreference = 'Stop'
if (-not $OutputDir) { $OutputDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'release' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version attendue : x.y.z (recu '$Version')" }
$PublishDir = (Resolve-Path -LiteralPath $PublishDir).Path
# Les anciens launchers (0.x) exigent COTK.Launcher.exe a la racine du zip et relancent ce nom apres la copie.
if (-not (Test-Path -LiteralPath (Join-Path $PublishDir 'COTK.Launcher.exe'))) { throw 'COTK.Launcher.exe absent : publier avec -p:AssemblyName=COTK.Launcher (les launchers deja installes ne migrent pas sans lui).' }

# Un paquet destine aux joueurs ne doit pas exiger .NET : le runtime doit etre embarque.
$selfContained = (Test-Path -LiteralPath (Join-Path $PublishDir 'coreclr.dll')) -or (Test-Path -LiteralPath (Join-Path $PublishDir 'hostfxr.dll'))
if (-not $selfContained) {
    Write-Warning 'Ce dossier n''embarque PAS le runtime .NET : les joueurs devraient installer .NET Desktop Runtime 10. Utilisez dotnet publish --self-contained.'
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$name = "COTK-Launcher-Next-$Version-win-x64.zip"
$zip = Join-Path $OutputDir $name
if (Test-Path -LiteralPath $zip) { throw "$name existe deja : une version publiee ne s'ecrase pas." }
Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
# Format sha256sum : verifiable avec `certutil -hashfile` ou `Get-FileHash`.
Set-Content -LiteralPath "$zip.sha256" -Value "$hash *$name" -Encoding ascii
Write-Host "Paquet : $zip"
Write-Host "Taille : $([math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)) Mio"
Write-Host "SHA-256 : $hash"
Write-Host "Runtime embarque : $selfContained"
Write-Host 'A publier a cote du zip : le fichier .sha256 et la FAQ (docs\download-faq.md) avec cette empreinte.'

