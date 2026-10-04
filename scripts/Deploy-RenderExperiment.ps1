[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue) { throw 'Close the game before deploying the experiment.' }
& "$PSScriptRoot/Test-RenderExperiment.ps1" -GameDirectory $GameDirectory
$source = Join-Path $projectRoot 'src/PRStutter.RenderExperiment/bin/Release/net6.0/PRStutter.RenderExperiment.dll'
$plugins = Join-Path $GameDirectory 'BepInEx/plugins'
if (Get-ChildItem -LiteralPath $plugins -Filter 'FFPR_Fix.dll' -Recurse -File) { throw 'FFPR Fix is enabled; disable it for a controlled experiment first.' }
$destination = Join-Path $plugins 'PRStutter.RenderExperiment/PRStutter.RenderExperiment.dll'
if (Test-Path -LiteralPath $destination) {
    $backup = Join-Path $projectRoot ('artifacts/plugin-backups/render-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Copy-Item -LiteralPath $destination -Destination (Join-Path $backup 'PRStutter.RenderExperiment.dll')
}
if (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue) { throw 'Game started during build; close it before deploying.' }
New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hash) { throw 'Deployment hash mismatch.' }
[ordered]@{
    PluginVersion='0.2.1'; Destination=$destination; Sha256=$hash
    DeployedUtc=[DateTime]::UtcNow.ToString('o'); EnabledByDefault=$false
    ToggleKeys=@{F9='background-only';F10='larger field target plus player stabilization'}; MaximumDurationSeconds=15; RuntimeVerified=$false
    Scope='Opt-in ordinary walking comparison; F10 temporarily redirects field rendering and offsets body/head/shadow visuals, leaving gameplay movement unchanged'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/render-experiment-deployment.json')
Write-Host "Opt-in render experiment deployed: $destination"
Write-Host 'OFF by default; F9 background-only, F10 player stabilization. Either key stops an active test; maximum 15 seconds. Live checks remain required.'
