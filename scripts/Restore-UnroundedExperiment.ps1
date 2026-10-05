[CmdletBinding()]
param([Parameter(Mandatory)][string]$Manifest)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Get-GameProfile.ps1"
$savedState = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$restoreProfile = Get-PrGameProfile -Game $savedState.Game
if (Get-Process -Name $restoreProfile.Process -ErrorAction SilentlyContinue) { throw 'Close the game before restoring.' }
$allowed = [IO.Path]::GetFullPath((Join-Path $restoreProfile.Directory 'BepInEx/plugins/PRStutter.UnroundedExperiment/PRStutter.UnroundedExperiment.dll'))
if ([IO.Path]::GetFullPath($savedState.Path) -ne $allowed) { throw 'Unexpected restore target.' }
if (!$savedState.Complete -or !$savedState.InstalledHash) { throw 'Incomplete deployment; inspect its backup before restoring.' }
if (!(Test-Path -LiteralPath $allowed) -or (Get-FileHash -LiteralPath $allowed).Hash -ne $savedState.InstalledHash) { throw 'Installed experiment changed; refusing overwrite.' }
if ($savedState.Previous) {
    if ((Get-FileHash -LiteralPath $savedState.Backup).Hash -ne $savedState.PreviousHash) { throw 'Backup hash mismatch.' }
    Copy-Item -LiteralPath $savedState.Backup -Destination $allowed -Force
    if ((Get-FileHash -LiteralPath $allowed).Hash -ne $savedState.PreviousHash) { throw 'Restoration hash mismatch.' }
} else { Remove-Item -LiteralPath $allowed }
Write-Host "Experiment restored. The independent audit remains installed and corrections remain OFF. To restore their previous state separately, review and use: $($savedState.AuditRestoreManifest)"
