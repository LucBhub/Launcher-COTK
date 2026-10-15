$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'out'
if (!(Test-Path -LiteralPath (Join-Path $out 'build.rsp'))) { throw 'Exécuter build.ps1 avant ce test.' }
$base = [IO.File]::ReadAllLines((Join-Path $out 'build.rsp')) | Where-Object { $_ -notmatch '^/out:|^/t:' }
$lines = @($base) + @('/t:exe', '/main:COTK.Next.Tests.UiTests', ('/out:"' + $out + '\UiTests.dll"'), ('"' + $PSScriptRoot + '\UiTests.cs"'))
$rsp = Join-Path $out 'ui-tests.rsp'
[IO.File]::WriteAllLines($rsp, $lines)
& 'C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe' /noconfig "@$rsp"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath (Join-Path $out 'COTK.Launcher.Next.runtimeconfig.json') -Destination (Join-Path $out 'UiTests.runtimeconfig.json') -Force
& 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $out 'UiTests.dll')
exit $LASTEXITCODE
