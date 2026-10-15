# Compile le launcher COTK (refonte WPF) avec Roslyn seul - machine SANS SDK .NET.
# Meme arbre que COTK.Launcher.Next.csproj (le build CI utilise le SDK).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1            # build
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Run       # build + lance
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Run -Preview   # + donnees fictives
#
# Sortie : out\COTK.Launcher.Next.dll (+ runtimeconfig). Lancement : dotnet out\COTK.Launcher.Next.dll
param([switch]$Run, [switch]$Preview, [string]$PreviewScreen = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'out'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$csc = 'C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe introuvable : $csc" }

function Latest-Shared($name) {
    $base = "C:\Program Files\dotnet\shared\$name"
    $dir = Get-ChildItem $base -Directory | Where-Object { $_.Name -match '^10\.' } | Sort-Object { [version]($_.Name -replace '-.*$', '') } -Descending | Select-Object -First 1
    if (-not $dir) { throw "runtime net10 introuvable : $base" }
    $dir.FullName
}
$core = Latest-Shared 'Microsoft.NETCore.App'
$desktop = Latest-Shared 'Microsoft.WindowsDesktop.App'
"Runtime : $core"
"Desktop : $desktop"

$refs = New-Object System.Collections.Generic.List[string]
foreach ($dir in @($desktop, $core)) {
    foreach ($f in Get-ChildItem $dir -Filter '*.dll') {
        try { [void][Reflection.AssemblyName]::GetAssemblyName($f.FullName); $refs.Add($f.FullName) } catch { }
    }
}
# System.Windows.Forms / Drawing / Design ne servent pas (et creent des ambiguites).
$skip = 'System.Windows.Forms', 'System.Drawing', 'System.Windows.Forms.Design', 'System.Windows.Forms.Primitives', 'System.Drawing.Design', 'Accessibility', 'UIAutomationClientsideProviders', 'System.Windows.Forms.Design.Editors'
$refs = $refs | Where-Object { $skip -notcontains [IO.Path]::GetFileNameWithoutExtension($_) }
$refs = $refs | Group-Object { [IO.Path]::GetFileName($_) } | ForEach-Object { $_.Group[0] }

$usings = Join-Path $out 'GlobalUsings.g.cs'
[IO.File]::WriteAllLines($usings, @(
    'global using global::System;', 'global using global::System.Collections.Generic;',
    'global using global::System.IO;', 'global using global::System.Linq;',
    'global using global::System.Net.Http;', 'global using global::System.Threading;',
    'global using global::System.Threading.Tasks;'))

$sources = Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' -Recurse -File | ForEach-Object { $_.FullName }
$rsp = Join-Path $out 'build.rsp'
$lines = @('/nologo', '/t:winexe', '/nullable:enable', '/langversion:latest', '/nostdlib', '/codepage:65001', '/optimize+', '/debug:portable',
    "/out:$out\COTK.Launcher.Next.dll", "/win32manifest:$root\app.manifest", "/win32icon:$root\assets\cotk.ico",
    '/warn:4', '/nowarn:CS1591', "`"$usings`"")
foreach ($r in $refs) { $lines += "/r:`"$r`"" }
foreach ($s in $sources) { $lines += "`"$s`"" }
[IO.File]::WriteAllLines($rsp, $lines)
"Sources : $($sources.Count)"

& $csc /noconfig "@$rsp"
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD ECHEC" -ForegroundColor Red; exit $LASTEXITCODE }

[IO.File]::WriteAllText("$out\COTK.Launcher.Next.runtimeconfig.json", @'
{
  "runtimeOptions": {
    "tfm": "net10.0",
    "frameworks": [
      { "name": "Microsoft.NETCore.App", "version": "10.0.0" },
      { "name": "Microsoft.WindowsDesktop.App", "version": "10.0.0" }
    ],
    "configProperties": {
      "System.Runtime.TieredPGO": true,
      "System.GC.Concurrent": true
    }
  }
}
'@)
New-Item -ItemType Directory -Force -Path "$out\fonts" | Out-Null
Copy-Item "$root\assets\fonts\*.ttf" "$out\fonts\" -Force
Copy-Item "$root\assets\fonts\OFL-Oswald.txt" "$out\fonts\" -Force
Copy-Item "$root\assets\ClientConfig.example.ini" "$out\" -Force
Copy-Item "$root\assets\cotk.ico" "$out\" -Force
New-Item -ItemType Directory -Force -Path "$out\ranks" | Out-Null
Copy-Item "$root\assets\ranks\*.png" "$out\ranks\" -Force

# Le SDK n'est pas installé ici : petit apphost WinExe (.NET Framework livré avec Windows).
# Un publish SDK standard produit directement un exe WPF self-contained.
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (Test-Path -LiteralPath (Join-Path $framework 'mscorlib.dll')) {
    & $csc /nologo /target:winexe /nostdlib /optimize+ /codepage:65001 "/out:$out\COTK.Launcher.Next.exe" "/win32icon:$root\assets\cotk.ico" "/win32manifest:$root\app.manifest" "/r:$framework\mscorlib.dll" "/r:$framework\System.dll" "/r:$framework\System.Windows.Forms.dll" "$root\bootstrap\StartLauncher.cs"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Write-Host "BUILD OK -> $out\COTK.Launcher.Next.dll" -ForegroundColor Green

if ($Run) {
    $argList = @("`"$out\COTK.Launcher.Next.dll`"")
    if ($Preview) { $argList += '--preview' }
    if ($PreviewScreen) { $argList += "--screen=$PreviewScreen" }
    Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList $argList -WorkingDirectory $out -WindowStyle Hidden
}


