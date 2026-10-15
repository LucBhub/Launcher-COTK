param([string[]]$Screens = @('home', 'library', 'matches', 'leaderboard', 'settings', 'login', 'password', 'installing', 'error', 'note', 'toast'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'out'
$captures = Join-Path $root 'docs\captures'
if (!(Test-Path -LiteralPath (Join-Path $root 'docs'))) { throw 'Dossier docs absent' }
New-Item -ItemType Directory -Force -Path $captures | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $out 'COTK.Launcher.Next.dll'))) { throw 'Compiler avec build.ps1 avant les captures.' }
$env:COTK_LOG_DIR = Join-Path $captures 'logs'
$env:COTK_SETTINGS_DIR = Join-Path $captures 'settings'
foreach ($screen in $Screens) {
    $png = Join-Path $captures ($screen + '.png')
    $args = @(('"' + (Join-Path $out 'COTK.Launcher.Next.dll') + '"'), '--preview', ('--screen=' + $screen), ('"--capture=' + $png + '"'), '--capture-delay=4200')
    $process = Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList $args -WorkingDirectory $out -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(20000)) { $process.Kill(); throw "Capture bloquée : $screen" }
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $png)) { throw "Échec de capture : $screen (exit=$($process.ExitCode))" }
    "OK $screen : $((Get-Item -LiteralPath $png).Length) octets"
}
Remove-Item Env:COTK_LOG_DIR, Env:COTK_SETTINGS_DIR
