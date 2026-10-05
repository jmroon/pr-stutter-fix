[CmdletBinding()]
param(
    [string]$GameDirectory,
    [ValidateSet('FFVI','FFIV')][string]$Game = 'FFVI',
    [switch]$WithoutDiagnostics
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Get-GameProfile.ps1"
$profile = Get-PrGameProfile -Game $Game -GameDirectory $GameDirectory
Assert-PrGameBuild $profile
$GameDirectory = $profile.Directory
if (Get-Process -Name $profile.Process -ErrorAction SilentlyContinue) { throw "Close $Game before deploying." }
$plugins = Join-Path $GameDirectory 'BepInEx/plugins'
foreach ($conflict in @('FFPR_Fix.dll','PRStutter.RenderExperiment.dll','PRStutter.NativeScrollExperiment.dll')) {
    if (Get-ChildItem -LiteralPath $plugins -Filter $conflict -Recurse -File) { throw "Conflicting experiment/mod: $conflict" }
}
& "$PSScriptRoot/Test-TimingExperiment.ps1" -GameDirectory $GameDirectory -Game $Game
& "$PSScriptRoot/Test-GridExperiment.ps1" -GameDirectory $GameDirectory -Game $Game
if (!$WithoutDiagnostics) { & "$PSScriptRoot/Test-PlaythroughDiagnostics.ps1" -GameDirectory $GameDirectory -Game $Game }
$names = @('PRStutter.GridExperiment','PRStutter.TimingExperiment')
if (!$WithoutDiagnostics) { $names += 'PRStutter.PlaythroughDiagnostics' }
if (Get-Process -Name $profile.Process -ErrorAction SilentlyContinue) { throw 'Game started during build; close it before deploying.' }
$backup = Join-Path $projectRoot ('artifacts/plugin-backups/runtime-' + $Game + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$entries = @()
foreach ($name in $names) {
    $destination = Join-Path $plugins "$name/$name.dll"
    $previous = Test-Path -LiteralPath $destination
    if ($previous) { Copy-Item -LiteralPath $destination -Destination (Join-Path $backup "$name.dll") }
    $source = Join-Path $projectRoot "src/$name/$($profile.Output)/$name.dll"
    $entries += [ordered]@{Name=$name; Source=$source; Destination=$destination; Previous=$previous; Sha256=(Get-FileHash -LiteralPath $source).Hash}
}
$entries | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'manifest.json')
foreach ($entry in $entries) {
    if (Get-Process -Name $profile.Process -ErrorAction SilentlyContinue) { throw "Game started; deployment incomplete. Previous DLLs are in $backup" }
    New-Item -ItemType Directory -Force -Path (Split-Path $entry.Destination) | Out-Null
    Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force
    if ((Get-FileHash -LiteralPath $entry.Destination).Hash -ne $entry.Sha256) { throw 'Installed DLL hash mismatch.' }
}
[ordered]@{
    DeployedUtc=[DateTime]::UtcNow.ToString('o'); Game=$Game; TimingVersion='0.6.3'; GridVersion='0.9.2'
    PlaythroughDiagnosticsVersion=$(if ($WithoutDiagnostics) { $null } else { '0.2.2' })
    CorrectionDefaultEnabled=$true; DebugDefaultEnabled=$false; RuntimeVerified=$false
    Backup=$backup; Files=$entries; GitCommit=(git -C $projectRoot rev-parse HEAD)
    Note='Offline verification passed; live automatic transitions and debug overhead remain to be measured. WithoutDiagnostics skips installation; it does not remove an existing diagnostics plugin.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $projectRoot $(if ($Game -eq "FFVI") { "artifacts/runtime-deployment.json" } else { "artifacts/runtime-deployment-FFIV.json" }))
Write-Host "$Game automatic runtime installed; F9 toggles corrections, F10 toggles optional debug, F11 marks an incident. Backups: $backup"
