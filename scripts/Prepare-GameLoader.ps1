[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [string]$LoaderSource = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$destinationRoot = (Resolve-Path -LiteralPath $GameDirectory).Path
$sourceRoot = (Resolve-Path -LiteralPath $LoaderSource).Path
if ($destinationRoot -eq $sourceRoot) { throw 'Source and destination must differ.' }
$games = @(Get-ChildItem -LiteralPath $destinationRoot -Filter '*.exe' -File)
if ($games.Count -ne 1) { throw 'Expected exactly one game executable.' }
$processName = $games[0].BaseName
if (Get-Process -Name $processName -ErrorAction SilentlyContinue) { throw 'Close the target game before preparing its loader.' }
# Only generic loader/runtime files. No plugins, patchers, game-specific interop or config.
$parts = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'dotnet', 'BepInEx/core')
foreach ($part in $parts) {
    if (!(Test-Path -LiteralPath (Join-Path $sourceRoot $part))) { throw "Missing source: $part" }
    if (Test-Path -LiteralPath (Join-Path $destinationRoot $part)) { throw "Existing destination must be reviewed first: $part" }
}
if (Test-Path -LiteralPath (Join-Path $destinationRoot 'BepInEx')) { throw 'Target already has BepInEx; refusing to merge installations.' }
$manifestDirectory = Join-Path $projectRoot ('artifacts/loader-installs/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $manifestDirectory | Out-Null
$records = @()
foreach ($part in $parts) {
    $source = Get-Item -LiteralPath (Join-Path $sourceRoot $part)
    $files = @($source)
    if ($source.PSIsContainer) { $files = @(Get-ChildItem -LiteralPath $source.FullName -File -Recurse) }
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
        $records += [ordered]@{RelativePath=$relative; Sha256=(Get-FileHash -LiteralPath $file.FullName).Hash}
    }
}
$manifest = [ordered]@{GameDirectory=$destinationRoot; LoaderSource=$sourceRoot; CreatedUtc=[DateTime]::UtcNow.ToString('o'); Status='planned'; Files=$records; Note='Generic loader only. No correction plugins or generated game bindings installed.'}
$manifestPath = Join-Path $manifestDirectory 'manifest.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
foreach ($record in $records) {
    if (Get-Process -Name $processName -ErrorAction SilentlyContinue) { throw "Game started; loader installation incomplete. See $manifestPath" }
    $destination = Join-Path $destinationRoot $record.RelativePath
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot $record.RelativePath) -Destination $destination
    if ((Get-FileHash -LiteralPath $destination).Hash -ne $record.Sha256) { throw "Hash mismatch: $destination" }
}
$manifest.Status = 'installed'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
Write-Host "Generic loader installed and verified. Launch $processName once to generate its own bindings. Manifest: $manifestPath"
