param([string[]]$Screens=@('login','settings','home','error','installing','report','uninstall'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'out'
$shots = Join-Path $root 'docs\captures'
if (!(Test-Path -LiteralPath $shots)) { throw 'Captures parent absent' }
$env:COTK_LOG_DIR = Join-Path $shots 'i18n-logs'
$env:COTK_SETTINGS_DIR = Join-Path $shots 'i18n-settings'
foreach ($lang in @('en','fr','zh-CN')) {
    foreach ($screen in $Screens) {
        $file = Join-Path $shots ($lang + '-' + $screen + '.png')
        $args = @(('"' + (Join-Path $out 'COTK.Launcher.Next.dll') + '"'), '--preview', ('--lang='+$lang), ('--screen='+$screen), ('"--capture='+$file+'"'), '--capture-delay=3600')
        $process = Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList $args -WorkingDirectory $out -WindowStyle Hidden -PassThru
        if (!$process.WaitForExit(18000)) { $process.Kill(); throw "Timeout $lang $screen" }
        if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $file)) { throw "Failed $lang $screen" }
        "OK $lang $screen"
    }
}
Remove-Item Env:COTK_LOG_DIR,Env:COTK_SETTINGS_DIR
