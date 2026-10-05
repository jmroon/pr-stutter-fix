[CmdletBinding()]
param([Parameter(Mandatory)][string]$Manifest)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Get-GameProfile.ps1"
$record=Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
if ($record.Kind -ne 'resolution-comparison' -or !$record.Complete) { throw 'Unexpected/incomplete manifest.' }
$profile=Get-PrGameProfile -Game $record.Game
Assert-PrGameBuild $profile
if (Get-Process -Name $profile.Process -ErrorAction SilentlyContinue) { throw 'Close the game before restoring.' }
$allowed=@('ResolutionComparison','PresentationAudit') | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $profile.Directory "BepInEx/plugins/PRStutter.$_/PRStutter.$_.dll")) }
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $record.Files) {
    $resolved=[IO.Path]::GetFullPath($entry.Path)
    if ($resolved -notin $allowed -or !$seen.Add($resolved)) { throw 'Unexpected/duplicate restore path.' }
    if (!$entry.InstalledHash -or !(Test-Path -LiteralPath $resolved) -or (Get-FileHash -LiteralPath $resolved).Hash -ne $entry.InstalledHash) { throw 'Installed file changed; preserve current work.' }
    if ($entry.Previous -and (!(Test-Path -LiteralPath $entry.Backup) -or (Get-FileHash -LiteralPath $entry.Backup).Hash -ne $entry.PreviousHash)) { throw 'Backup hash mismatch.' }
}
if ($seen.Count -ne 2) { throw 'Incomplete restore path set.' }
foreach ($entry in $record.Files) {
    if (Get-Process -Name $profile.Process -ErrorAction SilentlyContinue) { throw 'Game started during restore.' }
    if ($entry.Previous) {
        Copy-Item -LiteralPath $entry.Backup -Destination $entry.Path -Force
        if ((Get-FileHash -LiteralPath $entry.Path).Hash -ne $entry.PreviousHash) { throw 'Restore hash mismatch.' }
    } else { Remove-Item -LiteralPath $entry.Path }
}
Write-Host 'Resolution comparison add-on and audit restored. Core movement bundle untouched.'
