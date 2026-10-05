[CmdletBinding()]
param([ValidateSet('FFVI','FFIV')][string]$Game = 'FFIV', [switch]$PassThru)
$ErrorActionPreference = 'Stop'
$auditRoot = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Get-GameProfile.ps1"
$auditProfile = Get-PrGameProfile -Game $Game
Assert-PrGameBuild $auditProfile
if (Get-Process -Name $auditProfile.Process -ErrorAction SilentlyContinue) { throw "Close $Game before installing the audit." }
$pluginRoot = Join-Path $auditProfile.Directory 'BepInEx/plugins'
foreach ($conflict in @('FFPR_Fix.dll','PRStutter.RenderExperiment.dll','PRStutter.NativeScrollExperiment.dll')) {
    if (Get-ChildItem -LiteralPath $pluginRoot -Filter $conflict -Recurse -File) { throw "Disable conflicting plugin before baseline audit: $conflict" }
}
& "$PSScriptRoot/Test-PresentationAudit.ps1" -Game $Game
$backup = Join-Path $auditRoot ('artifacts/plugin-backups/audit-' + $Game + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$dll = Join-Path $pluginRoot 'PRStutter.PresentationAudit/PRStutter.PresentationAudit.dll'
$timing = Join-Path $auditProfile.Directory 'BepInEx/config/local.prstutter.timing.cfg'
$debug = Join-Path $auditProfile.Directory 'BepInEx/config/local.prstutter.playthrough.cfg'
$source = Join-Path $auditRoot "src/PRStutter.PresentationAudit/$($auditProfile.Output)/PRStutter.PresentationAudit.dll"

. "$PSScriptRoot/BaselineConfiguration.ps1"
# Prepare every output and backup before changing installed state.
$timingText = Disable-IniKey $timing 'Corrections'
$debugText = Disable-IniKey $debug 'Debug'
if (Get-Process -Name $auditProfile.Process -ErrorAction SilentlyContinue) { throw 'Game started during verification.' }
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$entries = @()
foreach ($path in @($dll,$timing,$debug)) {
    $exists = Test-Path -LiteralPath $path
    $saved = Join-Path $backup (Split-Path $path -Leaf)
    if ($exists) { Copy-Item -LiteralPath $path -Destination $saved }
    $entries += [ordered]@{Path=$path; Previous=$exists; Backup=$saved; PreviousHash=$(if($exists){(Get-FileHash -LiteralPath $path).Hash}else{$null}); InstalledHash=$null}
}
$manifest = Join-Path $backup 'manifest.json'
$deployment = [ordered]@{Game=$Game; Version='0.1.4'; GitCommit=(git -C $auditRoot rev-parse HEAD); Files=$entries; Complete=$false; RuntimeVerified=$false}
$deployment | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest
foreach ($path in @($dll,$timing,$debug)) {
    if (Get-Process -Name $auditProfile.Process -ErrorAction SilentlyContinue) { throw "Game started; installation incomplete. Backups: $backup" }
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    if ($path -eq $dll) { Copy-Item -LiteralPath $source -Destination $dll -Force }
    else { [System.IO.File]::WriteAllText($path, $(if($path -eq $timing){$timingText}else{$debugText}), [System.Text.UTF8Encoding]::new($false)) }
    ($entries | Where-Object { $_.Path -eq $path }).InstalledHash = (Get-FileHash -LiteralPath $path).Hash
    $deployment | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest
}
if ((Get-FileHash -LiteralPath $dll).Hash -ne (Get-FileHash -LiteralPath $source).Hash) { throw 'Installed audit hash mismatch.' }
if ([System.IO.File]::ReadAllText($timing) -ne $timingText -or [System.IO.File]::ReadAllText($debug) -ne $debugText) { throw 'Baseline configuration verification failed.' }
$deployment.Complete=$true
$deployment | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest
Write-Host "$Game audit installed; corrections and normal diagnostics OFF. Ctrl+F11 starts/stops 60 seconds. Restore manifest: $manifest"
if ($PassThru) { [pscustomobject]@{Manifest=$manifest; Game=$Game} }
