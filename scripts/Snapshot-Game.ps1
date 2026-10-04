[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$GameDirectory = (Resolve-Path -LiteralPath $GameDirectory).Path
$snapshotRoot = Join-Path $projectRoot ('artifacts/baselines/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $snapshotRoot -Force | Out-Null
$files = @(
    Get-Item -LiteralPath (Join-Path $GameDirectory 'GameAssembly.dll')
    Get-Item -LiteralPath (Join-Path $GameDirectory 'UnityPlayer.dll')
    Get-Item -LiteralPath (Join-Path $GameDirectory 'FINAL FANTASY VI_Data/il2cpp_data/Metadata/global-metadata.dat')
    Get-ChildItem -LiteralPath (Join-Path $GameDirectory 'BepInEx/core') -File
    Get-ChildItem -LiteralPath (Join-Path $GameDirectory 'BepInEx/interop') -File
    Get-ChildItem -LiteralPath (Join-Path $GameDirectory 'BepInEx/plugins') -File -Recurse
    Get-ChildItem -LiteralPath (Join-Path $GameDirectory 'BepInEx/config') -File -Recurse
)
$records = @($files | ForEach-Object {
    [ordered]@{
        RelativePath = [System.IO.Path]::GetRelativePath($GameDirectory, $_.FullName)
        Bytes = $_.Length
        Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
[ordered]@{GameDirectory=$GameDirectory;CapturedUtc=[DateTime]::UtcNow.ToString('o');Files=$records} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $snapshotRoot 'manifest.json')
Copy-Item -LiteralPath (Join-Path $GameDirectory 'BepInEx/config') -Destination (Join-Path $snapshotRoot 'config') -Recurse
Copy-Item -LiteralPath (Join-Path $GameDirectory 'BepInEx/LogOutput.log') -Destination (Join-Path $snapshotRoot 'BepInEx.log')
Write-Host "Recorded $($records.Count) file hashes and existing configuration in $snapshotRoot"
