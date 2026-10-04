[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
. "$PSScriptRoot/Environment.ps1"
Push-Location $projectRoot
try {
    & $dotnet build src/PRStutter.TimingExperiment/PRStutter.TimingExperiment.csproj -c Release --nologo "-p:GameDirectory=$GameDirectory"
    if ($LASTEXITCODE -ne 0) { throw 'Timing experiment build failed.' }
    & $dotnet run --project tests/TimingChecks/TimingChecks.csproj -c Release -- (Join-Path $projectRoot 'src/PRStutter.TimingExperiment/bin/Release/net6.0/PRStutter.TimingExperiment.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Timing experiment checks failed.' }
} finally { Pop-Location }
