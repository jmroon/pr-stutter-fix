[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
. "$PSScriptRoot/Environment.ps1"
$assembly = Join-Path $GameDirectory 'GameAssembly.dll'
$metadata = Join-Path $GameDirectory 'FINAL FANTASY VI_Data/il2cpp_data/Metadata/global-metadata.dat'
foreach ($file in @($assembly, $metadata)) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing game input: $file" }
}
$assemblyHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant()
$metadataHash = (Get-FileHash -LiteralPath $metadata -Algorithm SHA256).Hash.ToLowerInvariant()
$runName = "$($assemblyHash.Substring(0, 16))-$($metadataHash.Substring(0, 16))-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff')"
$outputDirectory = Join-Path $projectRoot "artifacts/il2cpp/$runName"
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$configPath = Join-Path (Split-Path $il2cppDumper -Parent) 'config.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$config.RequireAnyKey = $false
$config | ConvertTo-Json | Set-Content -LiteralPath $configPath
# This utility targets .NET 6, already installed system-wide. Do not roll it to .NET 10.
$net6Host = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'
Push-Location (Split-Path $il2cppDumper -Parent)
try {
    & $net6Host $il2cppDumper $assembly $metadata $outputDirectory 2>&1 |
        Tee-Object -FilePath (Join-Path $outputDirectory 'dumper.log')
    if ($LASTEXITCODE -ne 0) { throw "Il2CppDumper failed ($LASTEXITCODE)." }
    foreach ($expected in @('dump.cs', 'script.json', 'il2cpp.h', 'DummyDll/Assembly-CSharp.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $outputDirectory $expected))) {
            throw "Il2CppDumper did not produce $expected. Inspect dumper.log."
        }
    }
    [ordered]@{AssemblySha256=$assemblyHash;MetadataSha256=$metadataHash;OutputDirectory=$outputDirectory} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/latest-dump.json')
    Write-Host "IL2CPP metadata and method mappings: $outputDirectory"
} finally { Pop-Location }
