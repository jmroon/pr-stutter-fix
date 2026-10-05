[CmdletBinding()]
param([ValidateSet('FFVI','FFIV')][string]$Game='FFVI')
$ErrorActionPreference='Stop'
$resolutionRoot=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Get-GameProfile.ps1"
$resolutionProfile=Get-PrGameProfile -Game $Game
Assert-PrGameBuild $resolutionProfile
if (Get-Process -Name $resolutionProfile.Process -ErrorAction SilentlyContinue) { throw "Close $Game before installation." }
$plugins=Join-Path $resolutionProfile.Directory 'BepInEx/plugins'
$core=@()
foreach ($name in @('GridExperiment','TimingExperiment','UnroundedExperiment')) {
    $path=Join-Path $plugins "PRStutter.$name/PRStutter.$name.dll"
    $core += @{Path=$path; Hash=(Get-FileHash -LiteralPath $path).Hash}
}
& "$PSScriptRoot/Test-ResolutionComparison.ps1" -Game $Game
& "$PSScriptRoot/Test-PresentationAudit.ps1" -Game $Game
$backup=Join-Path $resolutionRoot ('artifacts/plugin-backups/resolution-'+$Game+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$entries=@()
foreach ($name in @('ResolutionComparison','PresentationAudit')) {
    $path=Join-Path $plugins "PRStutter.$name/PRStutter.$name.dll"
    $exists=Test-Path -LiteralPath $path
    $saved=Join-Path $backup "PRStutter.$name.dll"
    if ($exists) { Copy-Item -LiteralPath $path -Destination $saved }
    $entries += [ordered]@{Path=$path; Source=(Join-Path $resolutionRoot "src/PRStutter.$name/$($resolutionProfile.Output)/PRStutter.$name.dll"); Previous=$exists; Backup=$saved; PreviousHash=$(if($exists){(Get-FileHash -LiteralPath $saved).Hash}else{$null}); InstalledHash=$null}
}
$manifest=Join-Path $backup 'manifest.json'
$record=[ordered]@{Kind='resolution-comparison';Game=$Game;GitCommit=(git -C $resolutionRoot rev-parse HEAD);Files=$entries;UnchangedCore=$core;Complete=$false;RuntimeVerified=$false}
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifest
foreach ($entry in $entries) {
    if (Get-Process -Name $resolutionProfile.Process -ErrorAction SilentlyContinue) { throw "Game started; incomplete install: $manifest" }
    if ((Test-Path -LiteralPath $entry.Path) -ne $entry.Previous -or ($entry.Previous -and (Get-FileHash -LiteralPath $entry.Path).Hash -ne $entry.PreviousHash)) { throw 'Destination changed during build.' }
    New-Item -ItemType Directory -Path (Split-Path $entry.Path) -Force | Out-Null
    Copy-Item -LiteralPath $entry.Source -Destination $entry.Path -Force
    $entry.InstalledHash=(Get-FileHash -LiteralPath $entry.Path).Hash
    $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifest
    if ($entry.InstalledHash -ne (Get-FileHash -LiteralPath $entry.Source).Hash) { throw 'Installed DLL hash mismatch.' }
}
foreach ($item in $core) { if ((Get-FileHash -LiteralPath $item.Path).Hash -ne $item.Hash) { throw 'Core movement bundle changed during installation.' } }
$record.Complete=$true
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifest
Write-Host "$Game resolution add-on installed at A stock. Core movement DLLs unchanged. CRT OFF; enable smooth movement, then Alt+F11 cycles A stock / B 4x / C 8x. Restore: $manifest"
