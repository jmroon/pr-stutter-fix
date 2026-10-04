[CmdletBinding()]
param([string]$GameDirectory, [ValidateSet('FFVI','FFIV')][string]$Game = 'FFVI')
. "$PSScriptRoot/Environment.ps1"
. "$PSScriptRoot/Get-GameProfile.ps1"
$profile = Get-PrGameProfile -Game $Game -GameDirectory $GameDirectory
Assert-PrGameBuild $profile
$GameDirectory = $profile.Directory
Push-Location $projectRoot
try {
    & $dotnet build src/PRStutter.TimingExperiment/PRStutter.TimingExperiment.csproj -c Release --nologo "-p:GameDirectory=$GameDirectory" "-p:PrGame=$Game"
    if ($LASTEXITCODE -ne 0) { throw 'Timing experiment build failed.' }
    & $dotnet run --project tests/TimingChecks/TimingChecks.csproj -c Release -- (Join-Path $projectRoot "src/PRStutter.TimingExperiment/$($profile.Output)/PRStutter.TimingExperiment.dll") $Game
    if ($LASTEXITCODE -ne 0) { throw 'Timing experiment checks failed.' }
} finally { Pop-Location }
