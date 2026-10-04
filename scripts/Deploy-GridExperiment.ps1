[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue) { throw 'Close the game before deploying.' }
$plugins = Join-Path $GameDirectory 'BepInEx/plugins'
foreach ($conflict in @('FFPR_Fix.dll', 'PRStutter.RenderExperiment.dll', 'PRStutter.NativeScrollExperiment.dll')) {
    if (Get-ChildItem -LiteralPath $plugins -Filter $conflict -Recurse -File) { throw "Conflicting mod/experiment: $conflict" }
}
& "$PSScriptRoot/Test-GridExperiment.ps1" -GameDirectory $GameDirectory
$source = Join-Path $projectRoot 'src/PRStutter.GridExperiment/bin/Release/net6.0/PRStutter.GridExperiment.dll'
$destination = Join-Path $plugins 'PRStutter.GridExperiment/PRStutter.GridExperiment.dll'
if (Test-Path -LiteralPath $destination) {
    $backup = Join-Path $projectRoot ('artifacts/plugin-backups/grid-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    Copy-Item -LiteralPath $destination -Destination $backup
}
if (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue) { throw 'Game started during build; close it before deploying.' }
New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hash) { throw 'Deployment hash mismatch.' }
[ordered]@{
    PluginVersion='0.7.0'; Destination=$destination; Sha256=$hash; Installed=$true
    DeployedUtc=[DateTime]::UtcNow.ToString('o'); EnabledByDefault=$false
    ToggleKeys=@{F4='Combined 15-second test via timing 0.4.0';F6='6-second cropped pixel diagnostic, blocked during combined test';F7='VSync-paced field walking, 90s';F9='4x grid plus fractional motion';F10='8x grid plus fractional motion'}; CrtMustBeOff=$true
    PixelCaptureScope='Two 128x64 RGBA field patches; synchronous readback overhead recorded; 0.5s warmup plus 6s, maximum 1024 frames / 64 MiB managed pixel buffer. Not final composition or physical scanout.'
    MaximumDurationSeconds=15; RuntimeVerified=$false; TargetSizes=@{F9='1280x720';F10='2560x1440'}; TargetCount=3
    Scope='Compare 4x and 8x fractional motion and VSync pacing during ordinary cardinal and diagonal walking. F6 measures field-target pixel motion with a temporary scoped active render target; synchronous readback affects timing. Gameplay positions/speed and logical follow camera unchanged.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/grid-experiment-deployment.json')
Write-Host "Grid comparison deployed, OFF by default: $destination"
