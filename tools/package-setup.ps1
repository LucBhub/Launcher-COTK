# Empaquette le launcher publie (dotnet publish ... --self-contained) en setup Inno + SHA-256.
# Usage : powershell -File tools\package-setup.ps1 -PublishDir publish -Version 1.0.0
#         powershell -File tools\package-setup.ps1 -PublishDir publish -Version 1.0.0 -CheckOnly
# prerequis : Inno Setup 6 (ISCC.exe) ; par defaut installe pour l'utilisateur dans
# %LOCALAPPDATA%\Programs\InnoSetup6, ou iscc dans le PATH.
param(
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$Version,
    [string]$OutputDir = '',
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputDir) { $OutputDir = Join-Path $root 'release' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version attendue : x.y.z (recu '$Version')" }
$PublishDir = (Resolve-Path -LiteralPath $PublishDir).Path
# Le setup doit poser le meme executable racine que le zip d'auto-update.
if (-not (Test-Path -LiteralPath (Join-Path $PublishDir 'COTK.Launcher.exe'))) { throw 'COTK.Launcher.exe absent : publier avec -p:AssemblyName=COTK.Launcher.' }
# Un paquet destine aux joueurs ne doit pas exiger .NET : le runtime doit etre embarque.
if (-not ((Test-Path -LiteralPath (Join-Path $PublishDir 'coreclr.dll')) -or (Test-Path -LiteralPath (Join-Path $PublishDir 'hostfxr.dll')))) { throw 'Runtime .NET absent du dossier publie : utiliser dotnet publish --self-contained.' }
# Garde-fou console F8 admin (jamais dans le paquet public).
$leak = Get-ChildItem -LiteralPath $PublishDir -Recurse -File | Where-Object { $_.Name -like 'ToggleConsole*' }
if ($leak) { $leak.FullName; throw 'ToggleConsole present dans le dossier publie : refus de fabriquer le setup.' }

$name = "COTK-Launcher-Next-Setup-$Version.exe"
$exe = Join-Path $OutputDir $name
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
if (Test-Path -LiteralPath $exe) { throw "$name existe deja : une version publiee ne s'ecrase pas." }

$iss = Join-Path $root 'installer\cotk-next.iss'
if (-not (Test-Path -LiteralPath $iss)) { throw "Script Inno introuvable : $iss" }
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\InnoSetup6\ISCC.exe'
if (-not (Test-Path -LiteralPath $iscc)) { $found = Get-Command iscc -ErrorAction SilentlyContinue; if ($found) { $iscc = $found.Source } }

if ($CheckOnly) {
    Write-Host "Plan setup : $exe"
    Write-Host "ISS : $iss"
    Write-Host "ISCC : $(if (Test-Path -LiteralPath $iscc) { $iscc } else { 'INTROUVABLE (installation Inno Setup 6 requise pour compiler)' })"
    exit 0
}
if (-not (Test-Path -LiteralPath $iscc)) { throw 'ISCC.exe introuvable : installer Inno Setup 6 (mode utilisateur accepte).' }

& $iscc "/DAppVersion=$Version" "/DStageDir=$PublishDir" "/DOutputPath=$OutputDir" $iss
if ($LASTEXITCODE -ne 0) { throw "Compilation Inno Setup echouee (exit $LASTEXITCODE)." }
if (-not (Test-Path -LiteralPath $exe)) { throw "Setup attendu introuvable apres compilation : $exe" }

$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
# Format sha256sum : verifiable avec `certutil -hashfile` ou `Get-FileHash`.
Set-Content -LiteralPath "$exe.sha256" -Value "$hash *$name" -Encoding ascii
Write-Host "Setup : $exe"
Write-Host "Taille : $([math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)) Mio"
Write-Host "SHA-256 : $hash"
Write-Host 'A publier a cote du zip : le fichier .sha256, puis pointer launcher.setupUrl du manifeste vers ce setup.'
