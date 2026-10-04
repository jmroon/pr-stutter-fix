[CmdletBinding()]
param([Parameter(Mandatory)][string]$Manifest)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Get-GameProfile.ps1"
$record=Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$auditProfile=Get-PrGameProfile -Game $record.Game
if (Get-Process -Name $auditProfile.Process -ErrorAction SilentlyContinue) { throw 'Close the game before restoring.' }
$allowed=@('BepInEx/plugins/PRStutter.PresentationAudit/PRStutter.PresentationAudit.dll','BepInEx/config/local.prstutter.timing.cfg','BepInEx/config/local.prstutter.playthrough.cfg') | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $auditProfile.Directory $_)) }
$seen=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $record.Files) {
    $resolved=[IO.Path]::GetFullPath($entry.Path)
    if ($resolved -notin $allowed -or !$seen.Add($resolved)) { throw "Unexpected/duplicate restore path: $resolved" }
    if (!$entry.InstalledHash -or !(Test-Path -LiteralPath $resolved) -or (Get-FileHash -LiteralPath $resolved).Hash -ne $entry.InstalledHash) { throw "File changed or install incomplete; preserve current work and inspect manually: $resolved" }
    if ($entry.Previous -and (!(Test-Path -LiteralPath $entry.Backup) -or (Get-FileHash -LiteralPath $entry.Backup).Hash -ne $entry.PreviousHash)) { throw "Backup verification failed: $($entry.Backup)" }
}
if ($seen.Count -ne 3) { throw 'Incomplete restore manifest.' }
foreach ($entry in $record.Files) {
    if (Get-Process -Name $auditProfile.Process -ErrorAction SilentlyContinue) { throw 'Game started during restore.' }
    if ($entry.Previous) {
        Copy-Item -LiteralPath $entry.Backup -Destination $entry.Path -Force
        if ((Get-FileHash -LiteralPath $entry.Path).Hash -ne $entry.PreviousHash) { throw 'Restore hash mismatch.' }
    } else { Remove-Item -LiteralPath $entry.Path }
}
Write-Host 'Audit installation and baseline config changes restored. No other plugins changed.'
