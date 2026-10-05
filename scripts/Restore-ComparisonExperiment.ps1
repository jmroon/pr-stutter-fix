[CmdletBinding()]
param([Parameter(Mandatory)][string]$Manifest)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Get-GameProfile.ps1"
$record=Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
if ($record.Kind -notin @('coordinated-comparison','scene-aware-stock','integrated-smooth-movement')) { throw 'Unexpected manifest kind.' }
$restoreProfile=Get-PrGameProfile -Game $record.Game
Assert-PrGameBuild $restoreProfile
if (Get-Process -Name $restoreProfile.Process -ErrorAction SilentlyContinue) { throw 'Close the game before restoring.' }
$relative=@('BepInEx/config/local.prstutter.timing.cfg','BepInEx/config/local.prstutter.playthrough.cfg')
foreach ($name in @('GridExperiment','TimingExperiment','UnroundedExperiment','PresentationAudit')) {
    $relative += "BepInEx/plugins/PRStutter.$name/PRStutter.$name.dll"
}
if ($record.Kind -in @('scene-aware-stock','integrated-smooth-movement')) { $relative += 'BepInEx/plugins/PRStutter.PlaythroughDiagnostics/PRStutter.PlaythroughDiagnostics.dll' }
if ($record.Kind -eq 'integrated-smooth-movement') { $relative += 'BepInEx/plugins/PRStutter.ResolutionComparison/PRStutter.ResolutionComparison.dll' }
$allowed=$relative | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $restoreProfile.Directory $_)) }
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $record.Files) {
    $resolved=[IO.Path]::GetFullPath($entry.Path)
    if ($resolved -notin $allowed -or !$seen.Add($resolved)) { throw "Unexpected/duplicate restore path: $resolved" }
    $absent=$entry.InstalledHash -eq 'ABSENT'
    if ($absent -and ($record.Kind -notin @('scene-aware-stock','integrated-smooth-movement') -or $resolved -ne [IO.Path]::GetFullPath((Join-Path $restoreProfile.Directory 'BepInEx/plugins/PRStutter.PlaythroughDiagnostics/PRStutter.PlaythroughDiagnostics.dll')))) { throw 'Unexpected absent-file record.' }
    $changed=if ($absent) { Test-Path -LiteralPath $resolved } else { !$entry.InstalledHash -or !(Test-Path -LiteralPath $resolved) -or (Get-FileHash -LiteralPath $resolved).Hash -ne $entry.InstalledHash }
    if ($changed) { throw "File changed or installation incomplete; inspect manually and preserve current work: $resolved" }
    if ($entry.Previous -and (!(Test-Path -LiteralPath $entry.Backup) -or (Get-FileHash -LiteralPath $entry.Backup).Hash -ne $entry.PreviousHash)) { throw "Backup verification failed: $($entry.Backup)" }
}
if ($seen.Count -ne $allowed.Count) { throw 'Incomplete comparison manifest.' }
foreach ($entry in $record.Files) {
    if (Get-Process -Name $restoreProfile.Process -ErrorAction SilentlyContinue) { throw 'Game started during restore.' }
    if ($entry.Previous) {
        Copy-Item -LiteralPath $entry.Backup -Destination $entry.Path -Force
        if ((Get-FileHash -LiteralPath $entry.Path).Hash -ne $entry.PreviousHash) { throw 'Restore hash mismatch.' }
    } elseif (Test-Path -LiteralPath $entry.Path) { Remove-Item -LiteralPath $entry.Path }
}
Write-Host 'Comparison bundle restored to its pre-install state. Earlier offshoot backups remain available.'
