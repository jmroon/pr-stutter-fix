[CmdletBinding()]
param()
. "$PSScriptRoot/Environment.ps1"
Push-Location $projectRoot
try {
    & $dotnet tool restore --configfile (Join-Path $projectRoot 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw "Tool restore failed ($LASTEXITCODE)." }
    & $dotnet tool run ilspycmd -- --version
    if ($LASTEXITCODE -ne 0) { throw "ILSpy verification failed ($LASTEXITCODE)." }
} finally { Pop-Location }
