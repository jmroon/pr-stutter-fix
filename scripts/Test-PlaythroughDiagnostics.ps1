[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
. "$PSScriptRoot/Environment.ps1"
Push-Location $projectRoot
try {
    & $dotnet build src/PRStutter.PlaythroughDiagnostics/PRStutter.PlaythroughDiagnostics.csproj -c Release --nologo "-p:GameDirectory=$GameDirectory"
    if ($LASTEXITCODE -ne 0) { throw 'Playthrough diagnostics build failed.' }
    & $dotnet run --project tests/PlaythroughChecks/PlaythroughChecks.csproj -c Release -- (Join-Path $projectRoot 'src/PRStutter.PlaythroughDiagnostics/bin/Release/net6.0/PRStutter.PlaythroughDiagnostics.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Playthrough diagnostics checks failed.' }
} finally { Pop-Location }
