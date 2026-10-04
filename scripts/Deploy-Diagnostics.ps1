[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue) { throw 'Close the game before deploying the logger.' }
& "$PSScriptRoot/Build.ps1" -GameDirectory $GameDirectory
$source = Join-Path $projectRoot 'src/PRStutter.Diagnostics/bin/Release/net6.0/PRStutter.Diagnostics.dll'
$plugins = Join-Path $GameDirectory 'BepInEx/plugins'
if (Get-ChildItem -LiteralPath $plugins -Filter 'FFPR_Fix.dll' -Recurse -File) { throw 'FFPR Fix is enabled; disable it for a controlled baseline first.' }
$destination = Join-Path $plugins 'PRStutter.Diagnostics/PRStutter.Diagnostics.dll'
if (Test-Path -LiteralPath $destination) {
    $backup = Join-Path $projectRoot ('artifacts/plugin-backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Copy-Item -LiteralPath $destination -Destination (Join-Path $backup 'PRStutter.Diagnostics.dll')
}
New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hash) { throw 'Deployment hash mismatch.' }
[ordered]@{PluginVersion='0.3.1';ObservationMode='LateUpdatePolling';Destination=$destination;Sha256=$hash;DeployedUtc=[DateTime]::UtcNow.ToString('o');RuntimeVerified=$false} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/diagnostics-deployment.json')
Write-Host "Logging-only plugin deployed: $destination"
Write-Host 'Next: first verify normal camera/movement while idle, then press F8 and check recording behavior. Stop immediately if either stage changes gameplay.'
