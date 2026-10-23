# Tests du noyau du launcher (machine sans SDK : Roslyn + exécution directe).
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1
param([switch]$ApiSmoke)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path ([IO.Path]::GetTempPath()) 'cotk-next-tests-build'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$csc = 'C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
function Latest-Shared($name) {
    (Get-ChildItem "C:\Program Files\dotnet\shared\$name" -Directory | Where-Object { $_.Name -match '^10\.' } |
        Sort-Object { [version]($_.Name -replace '-.*$', '') } -Descending | Select-Object -First 1).FullName
}
$refs = @()
foreach ($dir in @((Latest-Shared 'Microsoft.WindowsDesktop.App'), (Latest-Shared 'Microsoft.NETCore.App'))) {
    foreach ($f in Get-ChildItem $dir -Filter '*.dll') {
        try { [void][Reflection.AssemblyName]::GetAssemblyName($f.FullName); $refs += $f.FullName } catch { }
    }
}
$skip = 'System.Windows.Forms', 'System.Drawing', 'System.Windows.Forms.Design', 'System.Windows.Forms.Primitives', 'System.Drawing.Design', 'Accessibility', 'UIAutomationClientsideProviders', 'System.Windows.Forms.Design.Editors'
$refs = $refs | Where-Object { $skip -notcontains [IO.Path]::GetFileNameWithoutExtension($_) } |
    Group-Object { [IO.Path]::GetFileName($_) } | ForEach-Object { $_.Group[0] }

$usings = Join-Path $out 'GlobalUsings.g.cs'
[IO.File]::WriteAllLines($usings, @('global using global::System;', 'global using global::System.Collections.Generic;',
    'global using global::System.IO;', 'global using global::System.Linq;', 'global using global::System.Net.Http;',
    'global using global::System.Threading;', 'global using global::System.Threading.Tasks;'))

$sources = @(Get-ChildItem (Join-Path $root 'src\Core') -Filter '*.cs' -Recurse -File | ForEach-Object { $_.FullName }) + @((Join-Path $PSScriptRoot 'CoreTests.cs'))
$sources += Join-Path $PSScriptRoot 'UninstallTests.cs'
$sources += Join-Path $PSScriptRoot 'ClientPatchTests.cs'
$lines = @('/nologo', '/t:exe', '/nullable:enable', '/langversion:latest', '/nostdlib', '/codepage:65001', '/debug:portable', "/out:$out\CoreTests.dll", '/nowarn:CS1591', "`"$usings`"")
foreach ($r in $refs) { $lines += "/r:`"$r`"" }
foreach ($s in $sources) { $lines += "`"$s`"" }
$rsp = Join-Path $out 'tests.rsp'
[IO.File]::WriteAllLines($rsp, $lines)

& $csc /noconfig "@$rsp"
if ($LASTEXITCODE -ne 0) { Write-Host 'COMPILATION DES TESTS ECHOUEE' -ForegroundColor Red; exit $LASTEXITCODE }

[IO.File]::WriteAllText("$out\CoreTests.runtimeconfig.json", '{"runtimeOptions":{"tfm":"net10.0","frameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"},{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}]}}')
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$arguments = @("$out\CoreTests.dll")
if ($ApiSmoke) { $arguments += '--api-smoke' }
& 'C:\Program Files\dotnet\dotnet.exe' @arguments
exit $LASTEXITCODE

