[CmdletBinding()]
param([ValidateRange(30, 180)][int]$Seconds = 75, [ValidateRange(0, 30)][int]$DelaySeconds = 10, [switch]$Elevated, [switch]$PacingComparison)
. "$PSScriptRoot/Environment.ps1"
$gameProcesses = @(Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue)
if ($gameProcesses.Count -ne 1) { throw 'Launch the game and load the test area first; exactly one game process is required.' }
$directory = Join-Path $projectRoot ('artifacts/measurements/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$csv = Join-Path $directory 'presentmon.csv'
$qpcBefore = [Diagnostics.Stopwatch]::GetTimestamp()
$utcAnchor = [DateTime]::UtcNow
$qpcAfter = [Diagnostics.Stopwatch]::GetTimestamp()
$sessionName = 'PRJudder-' + [Guid]::NewGuid().ToString('N')
$arguments = @('--process_id', [string]$gameProcesses[0].Id, '--output_file', ('"' + $csv + '"'),
    '--timed', [string]$Seconds, '--delay', [string]$DelaySeconds, '--terminate_after_timed',
    '--no_console_stats', '--no_track_input', '--qpc_time', '--v1_metrics', '--session_name', $sessionName)
$metadata = [ordered]@{
    SchemaVersion=1; ToolVersion='PresentMon 2.6.0'; Metrics='v1'; ProcessId=$gameProcesses[0].Id
    QpcFrequency=[Diagnostics.Stopwatch]::Frequency
    QpcAnchor=($qpcBefore + [long](($qpcAfter - $qpcBefore) / 2))
    UtcAnchor=$utcAnchor.ToString('o'); AnchorUncertaintyTicks=($qpcAfter - $qpcBefore)
    DelaySeconds=$DelaySeconds; DurationSeconds=$Seconds; SessionName=$sessionName
    GridLogPath='D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR\BepInEx\diagnostics\PRStutter\grid-test.log'
    OutputCsv=$csv; ElevatedLaunch=[bool]$Elevated; Status='launching'
    Protocol='CRT off. After approving UAC, return to the loaded game. During the capture walk straight unmodified, then F9 for its full 15 seconds, then F10 for its full 15 seconds. Stop each mode before starting another; avoid menus, alt-tab and map boundaries.'
    Limitation='ETW presentation timing only; does not measure image displacement or physical panel response. Unmodified periods are unmarked and may include idle time.'
}
$metadataPath = Join-Path $directory 'capture.json'
try {
    $metadata.ReportedVideoModes = @(Get-CimInstance Win32_VideoController |
        Select-Object Name, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate)
    $metadata.VideoModeCaveat = 'WMI reports integer adapter mode values, not measured scanout timing or proof of VRR engagement; compare with in-game pacing log.'
} catch {
    $metadata.VideoModeReadError = $_.Exception.Message
}
if ($PacingComparison) {
    $metadata.Protocol = 'CRT off, all tests initially off. Return after UAC and keep focused. Walk stock 15s, F7 then walk 15s, F9 then walk 20s, F10 then walk 20s, then F7 off. F7 bypasses the software cap using VSync for up to 90s; F9/F10 expire after 15s. Avoid menus and scene changes. Log events distinguish both pacing and grid modes.'
}
$metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $metadataPath -Encoding utf8
try {
    $start = @{ FilePath=$presentMon; ArgumentList=$arguments; WindowStyle='Hidden'; PassThru=$true }
    if ($Elevated) { $start.Verb = 'RunAs' }
    $captureProcess = Start-Process @start
    $metadata.CaptureProcessId = $captureProcess.Id
    $metadata.Status = 'launched'
} catch {
    $metadata.Status = 'launch_failed'; $metadata.Error = $_.Exception.Message
    $metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $metadataPath -Encoding utf8
    throw
}
$metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $metadataPath -Encoding utf8
[ordered]@{Directory=$directory; MetadataPath=$metadataPath; CaptureProcessId=$captureProcess.Id; Status='launched_not_yet_verified'} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/latest-measurement.json') -Encoding utf8
Write-Output "Capture launched: $directory"
Write-Output "Return to the game; capture begins after $DelaySeconds seconds and ends after $Seconds seconds. Launch does not prove that ETW recording succeeded."
