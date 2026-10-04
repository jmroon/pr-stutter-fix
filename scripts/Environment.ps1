$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT = Join-Path $projectRoot '.tools/dotnet-10.0.401'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.state/dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.cache/nuget/packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.cache/nuget/http'
$dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$jdkRoot = Join-Path $projectRoot '.tools/jdk-25.0.4.1+1'
$jdkDirectory = Get-ChildItem -LiteralPath $jdkRoot -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'bin/java.exe') } | Select-Object -First 1
if (-not $jdkDirectory) { throw 'JDK missing. Run scripts/Setup-Tools.ps1 first.' }
$env:JAVA_HOME = $jdkDirectory.FullName
$ghidra = Join-Path $projectRoot '.tools/ghidra-12.1.4/ghidra_12.1.4_PUBLIC'
$il2cppDumper = Join-Path $projectRoot '.tools/il2cppdumper-6.7.46/Il2CppDumper.dll'
$presentMon = Join-Path $projectRoot '.tools/presentmon-2.6.0/PresentMon-2.6.0-x64.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'SDK missing. Run scripts/Setup-Tools.ps1 first.' }
