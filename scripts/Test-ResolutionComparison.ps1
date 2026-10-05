[CmdletBinding()]
param([ValidateSet('FFVI','FFIV')][string]$Game='FFVI')
. "$PSScriptRoot/Environment.ps1"
. "$PSScriptRoot/Get-GameProfile.ps1"
$resolutionProfile=Get-PrGameProfile -Game $Game
Assert-PrGameBuild $resolutionProfile
Push-Location $projectRoot
try {
    & $dotnet build src/PRStutter.ResolutionComparison/PRStutter.ResolutionComparison.csproj -c Release --nologo "-p:GameDirectory=$($resolutionProfile.Directory)" "-p:PrGame=$Game"
    if ($LASTEXITCODE -ne 0) { throw 'Resolution comparison build failed.' }
    & $dotnet run --project tests/ResolutionComparisonChecks/ResolutionComparisonChecks.csproj -c Release -- (Join-Path $projectRoot "src/PRStutter.ResolutionComparison/$($resolutionProfile.Output)/PRStutter.ResolutionComparison.dll")
    if ($LASTEXITCODE -ne 0) { throw 'Resolution comparison checks failed.' }
    # Existing draw-order, partial-write and restoration tests; also prove that
    # the stock DLL still excludes the linked resolution implementation.
    & "$PSScriptRoot/Test-UnroundedExperiment.ps1" -Game $Game
} finally { Pop-Location }
