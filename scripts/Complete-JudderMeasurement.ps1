[CmdletBinding()]
param([string]$Directory)
. "$PSScriptRoot/Environment.ps1"
if (-not $Directory) {
    $latest = Get-Content (Join-Path $projectRoot 'artifacts/latest-measurement.json') -Raw | ConvertFrom-Json
    $Directory = $latest.Directory
    if (Get-Process -Id $latest.CaptureProcessId -ErrorAction SilentlyContinue) { throw 'Capture process is still running.' }
}
$metadata = Get-Content -LiteralPath (Join-Path $Directory 'capture.json') -Raw | ConvertFrom-Json
if (Test-Path -LiteralPath $metadata.GridLogPath) {
    Copy-Item -LiteralPath $metadata.GridLogPath -Destination (Join-Path $Directory 'grid-test.log')
}
python (Join-Path $PSScriptRoot 'analyze_presentmon.py') $Directory
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
