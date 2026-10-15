param([string]$Operation='key',[string]$InputFile='', [string]$OutputFile='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'out'
if(!(Test-Path -LiteralPath $out)){throw 'Build parent absent'}
$base=[IO.File]::ReadAllLines((Join-Path $out 'build.rsp')) | Where-Object {$_ -notmatch '^/out:|^/t:'}
$lines=@($base)+@('/t:exe','/main:ReleasePublisher',('/out:"'+$out+'\ReleasePublisher.dll"'),('"'+$PSScriptRoot+'\ReleasePublisher.cs"'))
$rsp=Join-Path $out 'publisher.rsp';[IO.File]::WriteAllLines($rsp,$lines)
& 'C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe' /noconfig "@$rsp"
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
Copy-Item -LiteralPath (Join-Path $out 'COTK.Launcher.Next.runtimeconfig.json') -Destination (Join-Path $out 'ReleasePublisher.runtimeconfig.json') -Force
$args=@((Join-Path $out 'ReleasePublisher.dll'),$Operation)
if($InputFile){$args+=$InputFile};if($OutputFile){$args+=$OutputFile}
& 'C:\Program Files\dotnet\dotnet.exe' @args
exit $LASTEXITCODE
