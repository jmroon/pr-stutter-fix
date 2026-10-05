. "$PSScriptRoot/Environment.ps1"
Push-Location $projectRoot
try {
    & $dotnet run --project tests/SettingsChecks/SettingsChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Persistent settings checks failed.' }
} finally { Pop-Location }
