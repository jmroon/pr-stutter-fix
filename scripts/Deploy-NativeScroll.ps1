[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue) { throw 'Close the game before deploying.' }
$plugins = Join-Path $GameDirectory 'BepInEx/plugins'
foreach ($conflict in @('FFPR_Fix.dll','PRStutter.RenderExperiment.dll')) {
    if (Get-ChildItem -LiteralPath $plugins -Filter $conflict -Recurse -File) { throw "Conflicting experiment/mod enabled: $conflict" }
}
& "$PSScriptRoot/Test-NativeScroll.ps1" -GameDirectory $GameDirectory
$source = Join-Path $projectRoot 'src/PRStutter.NativeScrollExperiment/bin/Release/net6.0/PRStutter.NativeScrollExperiment.dll'
$destination = Join-Path $plugins 'PRStutter.NativeScrollExperiment/PRStutter.NativeScrollExperiment.dll'
if (Test-Path -LiteralPath $destination) {
    $backup = Join-Path $projectRoot ('artifacts/plugin-backups/native-scroll-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    Copy-Item -LiteralPath $destination -Destination $backup
}
if (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue) { throw 'Game started during build; close it before deploying.' }
New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hash) { throw 'Deployment hash mismatch.' }
[ordered]@{
    PluginVersion='0.1.0'; Destination=$destination; Sha256=$hash
    DeployedUtc=[DateTime]::UtcNow.ToString('o'); EnabledByDefault=$false; ToggleKey='F10'
    MaximumDurationSeconds=15; RuntimeVerified=$false
    Scope='Fractional preCameraPosition via native map-scroll routine; original rendering pipeline, background-only diagnostic'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/native-scroll-deployment.json')
Write-Host "Native scroll diagnostic deployed, OFF by default: $destination"
