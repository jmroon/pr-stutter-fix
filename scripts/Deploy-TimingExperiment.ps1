[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue) { throw 'Close the game before deploying.' }
$plugins = Join-Path $GameDirectory 'BepInEx/plugins'
foreach ($conflict in @('FFPR_Fix.dll','PRStutter.RenderExperiment.dll','PRStutter.NativeScrollExperiment.dll')) {
    if (Get-ChildItem -LiteralPath $plugins -Filter $conflict -Recurse -File) { throw "Conflicting mod/experiment: $conflict" }
}
& "$PSScriptRoot/Test-TimingExperiment.ps1" -GameDirectory $GameDirectory
$gridBuilt = Join-Path $projectRoot 'src/PRStutter.GridExperiment/bin/Release/net6.0/PRStutter.GridExperiment.dll'
$gridInstalled = Join-Path $plugins 'PRStutter.GridExperiment/PRStutter.GridExperiment.dll'
if (!(Test-Path -LiteralPath $gridInstalled) -or
    (Get-FileHash -LiteralPath $gridBuilt -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $gridInstalled -Algorithm SHA256).Hash) {
    throw 'Deploy the matching grid plugin first (Deploy-GridExperiment.ps1), then retry timing deployment.'
}
$source = Join-Path $projectRoot 'src/PRStutter.TimingExperiment/bin/Release/net6.0/PRStutter.TimingExperiment.dll'
$destination = Join-Path $plugins 'PRStutter.TimingExperiment/PRStutter.TimingExperiment.dll'
if (Test-Path -LiteralPath $destination) {
    $backup = Join-Path $projectRoot ('artifacts/plugin-backups/timing-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    Copy-Item -LiteralPath $destination -Destination $backup
}
if (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue) { throw 'Game started during build; close it before deploying.' }
New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hash) { throw 'Deployment hash mismatch.' }
[ordered]@{
    PluginVersion='0.6.0'; Destination=$destination; Sha256=$hash; Installed=$true
    DeployedUtc=[DateTime]::UtcNow.ToString('o'); EnabledByDefault=$true; HooksInstalledByDefault=$false
    ToggleKey='F9'; MaximumDurationSeconds=$null; RuntimeVerified=$false
    Automatic=$true; PanelButton=$true; LegacyTimingCsvByDefault=$false; StatusPanel=$true; RequiredGridVersion='0.9.0'
    Scope='One early step of the exact newly queued player arrival task, admitted to the native scheduler for normal resumption/cleanup. Same-frame leftover time during cardinal 0.2s or diagonal 0.2*sqrt(2)s same-direction manual walks only after native approval and before camera update. Fresh input/collision checks retained; no carry across frames. Stops remove hooks; committed movement and admitted tasks remain game-owned.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/timing-experiment-deployment.json')
Write-Host "Automatic timing runtime deployed, enabled by default; F9 toggles: $destination"
