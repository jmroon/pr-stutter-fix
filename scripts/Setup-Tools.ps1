[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$toolRoot = Join-Path $projectRoot '.tools'
$downloadRoot = Join-Path $projectRoot '.cache/downloads'
New-Item -ItemType Directory -Force -Path $toolRoot, $downloadRoot | Out-Null

# Official release URLs and publisher checksums, resolved on 2026-10-02.
$packages = @(
    @{
        Name = 'dotnet'; Version = '10.0.401'; Archive = 'dotnet-sdk-10.0.401-win-x64.zip'
        Url = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.zip'
        Algorithm = 'SHA512'; Hash = '24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430'
    },
    @{
        Name = 'jdk'; Version = '25.0.4.1+1'; Archive = 'OpenJDK25U-jdk_x64_windows_hotspot_25.0.4.1_1.zip'
        Url = 'https://github.com/adoptium/temurin25-binaries/releases/download/jdk-25.0.4.1%2B1/OpenJDK25U-jdk_x64_windows_hotspot_25.0.4.1_1.zip'
        Algorithm = 'SHA256'; Hash = '00c847d804f4a78e9f04f2683faf14fed898535b177b7fc704486cb0284e9283'
    },
    @{
        Name = 'ghidra'; Version = '12.1.4'; Archive = 'ghidra_12.1.4_PUBLIC_20260921.zip'
        Url = 'https://github.com/NationalSecurityAgency/ghidra/releases/download/Ghidra_12.1.4_build/ghidra_12.1.4_PUBLIC_20260921.zip'
        Algorithm = 'SHA256'; Hash = 'ddac49f903da9d5bac833e5cc79395098b9c33cfd3279be5f31bd00387d2d4db'
    },
    @{
        Name = 'il2cppdumper'; Version = '6.7.46'; Archive = 'Il2CppDumper-net6-v6.7.46.zip'
        Url = 'https://github.com/Perfare/Il2CppDumper/releases/download/v6.7.46/Il2CppDumper-net6-v6.7.46.zip'
        Algorithm = 'SHA256'; Hash = $null # This older upstream release publishes no asset digest.
    },
    @{
        Name = 'presentmon'; Version = '2.6.0'; Archive = 'PresentMon-2.6.0-x64.exe'
        Url = 'https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe'
        Algorithm = 'SHA256'; Hash = 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af'
    }
)

$receipts = @()
foreach ($package in $packages) {
    $archivePath = Join-Path $downloadRoot $package.Archive
    if (-not (Test-Path -LiteralPath $archivePath)) {
        Write-Host "Downloading $($package.Name) $($package.Version)..."
        $partialPath = "$archivePath.partial"
        Invoke-WebRequest -Uri $package.Url -OutFile $partialPath
        Move-Item -LiteralPath $partialPath -Destination $archivePath -Force
    }
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm $package.Algorithm).Hash.ToLowerInvariant()
    if ($package.Hash -and $actualHash -ne $package.Hash) {
        throw "Publisher checksum mismatch for $archivePath. Refusing to extract or execute."
    }
    $destination = Join-Path $toolRoot "$($package.Name)-$($package.Version)"
    $markerPath = Join-Path $destination '.installed.json'
    if (-not (Test-Path -LiteralPath $markerPath)) {
        Write-Host "Extracting $($package.Name)..."
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        if ($package.Archive.EndsWith('.zip')) {
            [System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $destination, $true)
        } else {
            Copy-Item -LiteralPath $archivePath -Destination (Join-Path $destination $package.Archive)
        }
        @{ Version = $package.Version; Hash = $actualHash } | ConvertTo-Json | Set-Content -LiteralPath $markerPath
    }
    $receipts += [ordered]@{
        Name = $package.Name; Version = $package.Version; Url = $package.Url
        Algorithm = $package.Algorithm; DownloadHash = $actualHash
        PublisherChecksumVerified = [bool]$package.Hash; Directory = $destination
    }
}
$receipts | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $projectRoot 'tools.local.json')
Write-Host 'Portable tools installed. No system PATH or game files changed.'
