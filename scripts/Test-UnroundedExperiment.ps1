[CmdletBinding()]
param([ValidateSet('FFVI','FFIV')][string]$Game = 'FFVI')
. "$PSScriptRoot/Environment.ps1"
. "$PSScriptRoot/Get-GameProfile.ps1"
$testProfile = Get-PrGameProfile -Game $Game
Assert-PrGameBuild $testProfile
Push-Location $projectRoot
try {
    & $dotnet build src/PRStutter.UnroundedExperiment/PRStutter.UnroundedExperiment.csproj -c Release --nologo "-p:GameDirectory=$($testProfile.Directory)" "-p:PrGame=$Game"
    if ($LASTEXITCODE -ne 0) { throw 'Unrounded experiment build failed.' }
    & $dotnet run --project tests/UnroundedChecks/UnroundedChecks.csproj -c Release -- $Game (Join-Path $testProfile.Directory 'GameAssembly.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Unrounded experiment checks failed.' }
} finally { Pop-Location }
