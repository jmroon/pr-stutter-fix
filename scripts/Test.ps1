[CmdletBinding()]
param(
    [string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR',
    [string]$PythonExe = 'python'
)
. "$PSScriptRoot/Environment.ps1"
& "$PSScriptRoot/Build.ps1" -GameDirectory $GameDirectory
Push-Location $projectRoot
try {
    & $dotnet run --project 'tests/CaptureChecks/CaptureChecks.csproj' --configuration Release -- (Join-Path $projectRoot 'artifacts/tests') (Join-Path $projectRoot 'src/PRStutter.Diagnostics/bin/Release/net6.0/PRStutter.Diagnostics.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Capture data checks failed.' }
    & $PythonExe -m unittest discover -s tests -p 'test_*.py' -v
    if ($LASTEXITCODE -ne 0) { throw 'Capture analysis checks failed.' }
} finally { Pop-Location }
