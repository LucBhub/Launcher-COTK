param([switch]$InstallTask)
$ErrorActionPreference = 'Stop'
$runner = Join-Path $env:LOCALAPPDATA 'COTK-ReleaseRunner'
if (-not (Test-Path -LiteralPath (Join-Path $runner '.runner'))) { throw 'Register the GitHub runner first.' }
if ($InstallTask) {
    $script = $PSCommandPath
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`"" -WorkingDirectory $runner
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    Register-ScheduledTask -TaskName 'COTK GitHub Release Runner' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'GitHub release worker using this Windows user and its non-exportable CNG signing key.' -Force | Out-Null
    Start-ScheduledTask -TaskName 'COTK GitHub Release Runner'
    return
}
& (Join-Path $runner 'run.cmd')
exit $LASTEXITCODE
