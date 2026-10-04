[CmdletBinding()]
param(
    [string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR',
    [switch]$WithoutDiagnostics
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue) { throw 'Close FFVI before deploying.' }
$plugins = Join-Path $GameDirectory 'BepInEx/plugins'
foreach ($conflict in @('FFPR_Fix.dll','PRStutter.RenderExperiment.dll','PRStutter.NativeScrollExperiment.dll')) {
    if (Get-ChildItem -LiteralPath $plugins -Filter $conflict -Recurse -File) { throw "Conflicting experiment/mod: $conflict" }
}
& "$PSScriptRoot/Test-TimingExperiment.ps1" -GameDirectory $GameDirectory
& "$PSScriptRoot/Test-GridExperiment.ps1" -GameDirectory $GameDirectory
if (!$WithoutDiagnostics) { & "$PSScriptRoot/Test-PlaythroughDiagnostics.ps1" -GameDirectory $GameDirectory }
$names = @('PRStutter.GridExperiment','PRStutter.TimingExperiment')
if (!$WithoutDiagnostics) { $names += 'PRStutter.PlaythroughDiagnostics' }
if (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue) { throw 'Game started during build; close it before deploying.' }
$backup = Join-Path $projectRoot ('artifacts/plugin-backups/runtime-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$entries = @()
foreach ($name in $names) {
    $destination = Join-Path $plugins "$name/$name.dll"
    $previous = Test-Path -LiteralPath $destination
    if ($previous) { Copy-Item -LiteralPath $destination -Destination (Join-Path $backup "$name.dll") }
    $source = Join-Path $projectRoot "src/$name/bin/Release/net6.0/$name.dll"
    $entries += [ordered]@{Name=$name; Source=$source; Destination=$destination; Previous=$previous; Sha256=(Get-FileHash -LiteralPath $source).Hash}
}
$entries | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'manifest.json')
foreach ($entry in $entries) {
    if (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue) { throw "Game started; deployment incomplete. Previous DLLs are in $backup" }
    New-Item -ItemType Directory -Force -Path (Split-Path $entry.Destination) | Out-Null
    Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force
    if ((Get-FileHash -LiteralPath $entry.Destination).Hash -ne $entry.Sha256) { throw 'Installed DLL hash mismatch.' }
}
[ordered]@{
    DeployedUtc=[DateTime]::UtcNow.ToString('o'); TimingVersion='0.5.0'; GridVersion='0.8.0'
    PlaythroughDiagnosticsVersion=$(if ($WithoutDiagnostics) { $null } else { '0.1.0' })
    CorrectionDefaultEnabled=$true; DebugDefaultEnabled=$false; RuntimeVerified=$false
    Backup=$backup; Files=$entries; GitCommit=(git -C $projectRoot rev-parse HEAD)
    Note='Offline verification passed; live automatic transitions and debug overhead remain to be measured. WithoutDiagnostics skips installation; it does not remove an existing diagnostics plugin.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/runtime-deployment.json')
Write-Host "Automatic runtime installed; F4 toggles corrections, F2 toggles optional debug, F3 marks an incident. Backups: $backup"
