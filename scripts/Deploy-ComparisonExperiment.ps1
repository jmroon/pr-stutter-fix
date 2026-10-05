[CmdletBinding()]
param([ValidateSet('FFVI','FFIV')][string]$Game='FFVI')
$ErrorActionPreference='Stop'
$comparisonRoot=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Get-GameProfile.ps1"
. "$PSScriptRoot/BaselineConfiguration.ps1"
$comparisonProfile=Get-PrGameProfile -Game $Game
Assert-PrGameBuild $comparisonProfile
if (Get-Process -Name $comparisonProfile.Process -ErrorAction SilentlyContinue) { throw "Close $Game before deployment." }
$plugins=Join-Path $comparisonProfile.Directory 'BepInEx/plugins'
foreach ($conflict in @('FFPR_Fix.dll','PRStutter.RenderExperiment.dll','PRStutter.NativeScrollExperiment.dll')) {
    if (Get-ChildItem -LiteralPath $plugins -Filter $conflict -Recurse -File) { throw "Disable conflicting plugin: $conflict" }
}
& "$PSScriptRoot/Test-TimingExperiment.ps1" -Game $Game
& "$PSScriptRoot/Test-UnroundedExperiment.ps1" -Game $Game
& "$PSScriptRoot/Test-PresentationAudit.ps1" -Game $Game
$backup=Join-Path $comparisonRoot ('artifacts/plugin-backups/comparison-'+$Game+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$outputs=@()
foreach ($name in @('GridExperiment','TimingExperiment','UnroundedExperiment','PresentationAudit')) {
    $outputs += @{Path=(Join-Path $plugins "PRStutter.$name/PRStutter.$name.dll"); Source=(Join-Path $comparisonRoot "src/PRStutter.$name/$($comparisonProfile.Output)/PRStutter.$name.dll"); Text=$null}
}
foreach ($item in @(@('timing','Corrections'),@('playthrough','Debug'))) {
    $path=Join-Path $comparisonProfile.Directory "BepInEx/config/local.prstutter.$($item[0]).cfg"
    $outputs += @{Path=$path; Source=$null; Text=(Disable-IniKey $path $item[1])}
}
$entries=@()
foreach ($output in $outputs) {
    $previous=Test-Path -LiteralPath $output.Path
    $saved=Join-Path $backup (Split-Path $output.Path -Leaf)
    if ($previous) { Copy-Item -LiteralPath $output.Path -Destination $saved }
    $entries += [ordered]@{Path=$output.Path; Previous=$previous; Backup=$saved; PreviousHash=$(if($previous){(Get-FileHash -LiteralPath $saved).Hash}else{$null}); InstalledHash=$null}
}
$manifest=Join-Path $backup 'manifest.json'
$record=[ordered]@{Game=$Game; Kind='coordinated-comparison'; GitCommit=(git -C $comparisonRoot rev-parse HEAD); Files=$entries; Complete=$false; RuntimeVerified=$false}
$record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest
foreach ($output in $outputs) {
    if (Get-Process -Name $comparisonProfile.Process -ErrorAction SilentlyContinue) { throw "Game started; inspect incomplete install: $manifest" }
    $entry=$entries | Where-Object { $_.Path -eq $output.Path }
    if ((Test-Path -LiteralPath $output.Path) -ne $entry.Previous -or ($entry.Previous -and (Get-FileHash -LiteralPath $output.Path).Hash -ne $entry.PreviousHash)) { throw "File changed during build: $($output.Path)" }
    New-Item -ItemType Directory -Path (Split-Path $output.Path) -Force | Out-Null
    if ($output.Source) { Copy-Item -LiteralPath $output.Source -Destination $output.Path -Force }
    else { [IO.File]::WriteAllText($output.Path,$output.Text,[Text.UTF8Encoding]::new($false)) }
    $entry.InstalledHash=(Get-FileHash -LiteralPath $output.Path).Hash
    $record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest
    if ($output.Source -and $entry.InstalledHash -ne (Get-FileHash -LiteralPath $output.Source).Hash) { throw 'Installed DLL hash mismatch.' }
    if (!$output.Source -and [IO.File]::ReadAllText($output.Path) -ne $output.Text) { throw 'Installed configuration mismatch.' }
}
$record.Complete=$true
$record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest
Write-Host "$Game stock movement installed OFF. Smooth walking button/Shift+F11 toggles; Ctrl+F11 records. No 8x mode; control changes still stop this preview. Restore manifest: $manifest"
