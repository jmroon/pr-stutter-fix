[CmdletBinding()]
param([ValidateRange(1, 300)][int]$Seconds = 15)
. "$PSScriptRoot/Environment.ps1"
if (-not (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue)) {
    throw 'Start FINAL FANTASY VI and load a repeatable test location first.'
}
$captureDirectory = Join-Path $projectRoot 'artifacts/captures'
New-Item -ItemType Directory -Force -Path $captureDirectory | Out-Null
$output = Join-Path $captureDirectory ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-presentmon.csv')
$sessionName = 'PRStutter-' + [Guid]::NewGuid().ToString('N')
& $presentMon --process_name 'FINAL FANTASY VI.exe' --output_file $output --timed $Seconds --terminate_after_timed --no_console_stats --session_name $sessionName --no_track_input
if ($LASTEXITCODE -ne 0) { throw "PresentMon capture failed ($LASTEXITCODE). It may need an elevated terminal for ETW access." }
if (-not (Test-Path -LiteralPath $output)) { throw 'PresentMon did not produce a capture.' }
if (@(Import-Csv -LiteralPath $output).Count -eq 0) { throw 'PresentMon captured no frame records.' }
Write-Host "Frame timing capture: $output"
