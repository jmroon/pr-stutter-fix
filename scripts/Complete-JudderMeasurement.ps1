[CmdletBinding()]
param([string]$Directory)
. "$PSScriptRoot/Environment.ps1"
if (-not $Directory) {
    $latest = Get-Content (Join-Path $projectRoot 'artifacts/latest-measurement.json') -Raw | ConvertFrom-Json
    $Directory = $latest.Directory
    if (Get-Process -Id $latest.CaptureProcessId -ErrorAction SilentlyContinue) { throw 'Capture process is still running.' }
}
$metadata = Get-Content -LiteralPath (Join-Path $Directory 'capture.json') -Raw | ConvertFrom-Json
if ($metadata.CaptureProcessId -and (Get-Process -Id $metadata.CaptureProcessId -ErrorAction SilentlyContinue)) { throw 'Capture process is still running.' }
if (Test-Path -LiteralPath $metadata.GridLogPath) {
    Copy-Item -LiteralPath $metadata.GridLogPath -Destination (Join-Path $Directory 'grid-test.log')
}
if ($metadata.TimingLogPath -and (Test-Path -LiteralPath $metadata.TimingLogPath)) {
    Copy-Item -LiteralPath $metadata.TimingLogPath -Destination (Join-Path $Directory 'timing-test.log')
}
if ($metadata.MotionDirectory) {
    # Select completed F8 captures by monotonic timestamps, not file modification
    # time. Copy only overlapping recordings; missing motion is explicit.
    $startQpc = [long]$metadata.QpcAnchor
    $endQpc = $startQpc + [long](($metadata.DelaySeconds + $metadata.DurationSeconds + 5) * $metadata.QpcFrequency)
    $motion = @()
    foreach ($file in Get-ChildItem -LiteralPath $metadata.MotionDirectory -Filter '*.json' -File) {
        if ($file.Name -like '*.render.json') { continue }
        try { $capture = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json } catch { continue }
        if (!$capture.StartQpc -or !$capture.EndQpc -or $capture.QpcFrequency -ne $metadata.QpcFrequency -or
            [long]$capture.EndQpc -lt $startQpc -or [long]$capture.StartQpc -gt $endQpc) { continue }
        $csv = [IO.Path]::ChangeExtension($file.FullName, '.csv')
        if (!(Test-Path -LiteralPath $csv)) { continue }
        $destination = Join-Path $Directory 'motion'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName,$csv -Destination $destination
        $motion += $file.Name
    }
    $metadata | Add-Member -NotePropertyName MotionCaptures -NotePropertyValue $motion -Force
    $metadata | Add-Member -NotePropertyName MotionEvidence -NotePropertyValue $(if ($motion.Count) { 'copied; overlap and sample validity require analysis' } else { 'missing; presentation-only evidence' }) -Force
}
& (Join-Path $projectRoot '.tools/plot-python/Scripts/python.exe') (Join-Path $PSScriptRoot 'analyze_presentmon.py') $Directory
if ($LASTEXITCODE -ne 0) { throw 'Measurement analysis failed; preserve the raw output.' }
$reportPath = Join-Path $Directory 'presentation-analysis.json'
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$metadata.Status = 'analyzed'
$metadata | Add-Member -NotePropertyName SourceRows -NotePropertyValue $report.source_rows -Force
$metadata | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Directory 'capture.json') -Encoding utf8
$latestPath = Join-Path $projectRoot 'artifacts/latest-measurement.json'
if (Test-Path -LiteralPath $latestPath) {
    $latest = Get-Content -LiteralPath $latestPath -Raw | ConvertFrom-Json
    if ($latest.Directory -eq $Directory) {
        $latest.Status = 'analyzed'
        $latest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $latestPath -Encoding utf8
    }
}
