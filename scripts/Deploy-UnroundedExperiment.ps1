[CmdletBinding()]
param([ValidateSet('FFVI','FFIV')][string]$Game = 'FFVI')
$ErrorActionPreference = 'Stop'
$testRoot = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Get-GameProfile.ps1"
$installProfile = Get-PrGameProfile -Game $Game
Assert-PrGameBuild $installProfile
if (Get-Process -Name $installProfile.Process -ErrorAction SilentlyContinue) { throw "Close $Game before installation." }
& "$PSScriptRoot/Test-UnroundedExperiment.ps1" -Game $Game
$source = Join-Path $testRoot "src/PRStutter.UnroundedExperiment/$($installProfile.Output)/PRStutter.UnroundedExperiment.dll"
$target = Join-Path $installProfile.Directory 'BepInEx/plugins/PRStutter.UnroundedExperiment/PRStutter.UnroundedExperiment.dll'
$backup = Join-Path $testRoot ('artifacts/plugin-backups/unrounded-' + $Game + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$saved = Join-Path $backup 'PRStutter.UnroundedExperiment.dll'
$previous = Test-Path -LiteralPath $target
if ($previous) { Copy-Item -LiteralPath $target -Destination $saved }
$entry = [ordered]@{Game=$Game; Version='0.4.0'; GitCommit=(git -C $testRoot rev-parse HEAD); Path=$target; Previous=$previous; Backup=$saved; PreviousHash=$(if($previous){(Get-FileHash -LiteralPath $target).Hash}else{$null}); InstalledHash=$null; AuditRestoreManifest=$null; Complete=$false; RuntimeVerified=$false}
$manifest = Join-Path $backup 'manifest.json'
$entry | ConvertTo-Json | Set-Content -LiteralPath $manifest
# Reuse the audited installer for the independently versioned observer and baseline configs.
$auditInstall = & "$PSScriptRoot/Deploy-PresentationAudit.ps1" -Game $Game -PassThru | Where-Object { $_ -isnot [string] -and $_.Manifest }
if (@($auditInstall).Count -ne 1) { throw "Audit installation did not return one manifest. Experiment not installed. Backup: $manifest" }
$entry.AuditRestoreManifest = $auditInstall.Manifest
$entry | ConvertTo-Json | Set-Content -LiteralPath $manifest
if (Get-Process -Name $installProfile.Process -ErrorAction SilentlyContinue) { throw "Game started; experiment not installed. Backup: $manifest" }
if ((Test-Path -LiteralPath $target) -ne $previous -or ($previous -and (Get-FileHash -LiteralPath $target).Hash -ne $entry.PreviousHash)) { throw 'Experiment file changed during installation.' }
New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
Copy-Item -LiteralPath $source -Destination $target -Force
$entry.InstalledHash = (Get-FileHash -LiteralPath $target).Hash
$entry | ConvertTo-Json | Set-Content -LiteralPath $manifest
if ($entry.InstalledHash -ne (Get-FileHash -LiteralPath $source).Hash) { throw 'Installed experiment hash mismatch.' }
$entry.Complete=$true
$entry | ConvertTo-Json | Set-Content -LiteralPath $manifest
Write-Host "$Game unrounded experiment 0.4.0 installed OFF. Requires timing 0.6.3/grid 0.9.2: use Deploy-ComparisonExperiment.ps1 for the complete comparison. Restore manifest: $manifest"
