param([int]$SampleSeconds = 12)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'out'
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('cotk-perf-' + [Guid]::NewGuid().ToString('N'))
if (!(Test-Path -LiteralPath ([IO.Path]::GetTempPath()))) { throw 'TEMP absent' }
New-Item -ItemType Directory -Path $sandbox | Out-Null
$env:COTK_SETTINGS_DIR = $sandbox
$env:COTK_LOG_DIR = Join-Path $sandbox 'logs'
[IO.File]::WriteAllText((Join-Path $sandbox 'settings.json'), '{"reduceMotion":false}')
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class WindowBench { [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int state); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h); }'
$process = Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @(('"' + (Join-Path $out 'COTK.Launcher.Next.dll') + '"'), '--preview') -WorkingDirectory $out -PassThru -WindowStyle Hidden
$rows = @()
function Measure-Launcher($name) {
    $process.Refresh()
    $cpu = $process.TotalProcessorTime.TotalMilliseconds
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds $SampleSeconds
    $sw.Stop()
    $process.Refresh()
    $corePercent = 100 * ($process.TotalProcessorTime.TotalMilliseconds - $cpu) / $sw.Elapsed.TotalMilliseconds
    [pscustomobject]@{state=$name; seconds=[Math]::Round($sw.Elapsed.TotalSeconds,2); cpuOneCorePercent=[Math]::Round($corePercent,2); cpuHostPercent=[Math]::Round($corePercent/[Environment]::ProcessorCount,2); workingSetMiB=[Math]::Round($process.WorkingSet64/1MB,1); privateMiB=[Math]::Round($process.PrivateMemorySize64/1MB,1)}
}
try {
    Start-Sleep -Seconds 3
    $process.Refresh()
    "Window: $($process.MainWindowHandle) $($process.MainWindowTitle)"
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Fenêtre WPF non trouvée' }
    $handle = $process.MainWindowHandle
    [void][WindowBench]::ShowWindow($process.MainWindowHandle, 9)
    [void][WindowBench]::SetForegroundWindow($process.MainWindowHandle)
    Start-Sleep -Seconds 2
    $rows += Measure-Launcher 'home-animated-foreground'
    [void][WindowBench]::ShowWindow($handle, 6)
    Start-Sleep -Seconds 2
    if (![WindowBench]::IsIconic($handle)) { throw 'La fenêtre ne s’est pas réduite' }
    $rows += Measure-Launcher 'minimized'
    $rows | Format-Table -AutoSize
    $report = Join-Path $root 'docs\performance.json'
    [IO.File]::WriteAllText($report, (@{date=[DateTimeOffset]::Now.ToString('o'); logicalProcessors=[Environment]::ProcessorCount; samples=$rows} | ConvertTo-Json -Depth 4))
    "Rapport : $report"
    "Journal : $env:COTK_LOG_DIR"
} finally {
    if (!$process.HasExited) { $process.CloseMainWindow() | Out-Null; if (!$process.WaitForExit(4000)) { $process.Kill() } }
    Remove-Item Env:COTK_SETTINGS_DIR, Env:COTK_LOG_DIR
}
