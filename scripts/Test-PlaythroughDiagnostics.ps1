[CmdletBinding()]
param([string]$GameDirectory, [ValidateSet('FFVI','FFIV')][string]$Game = 'FFVI')
. "$PSScriptRoot/Environment.ps1"
. "$PSScriptRoot/Get-GameProfile.ps1"
$profile = Get-PrGameProfile -Game $Game -GameDirectory $GameDirectory
Assert-PrGameBuild $profile
$GameDirectory = $profile.Directory
Push-Location $projectRoot
try {
    & $dotnet build src/PRStutter.PlaythroughDiagnostics/PRStutter.PlaythroughDiagnostics.csproj -c Release --nologo "-p:GameDirectory=$GameDirectory" "-p:PrGame=$Game"
    if ($LASTEXITCODE -ne 0) { throw 'Playthrough diagnostics build failed.' }
    & $dotnet run --project tests/PlaythroughChecks/PlaythroughChecks.csproj -c Release -- (Join-Path $projectRoot "src/PRStutter.PlaythroughDiagnostics/$($profile.Output)/PRStutter.PlaythroughDiagnostics.dll")
    if ($LASTEXITCODE -ne 0) { throw 'Playthrough diagnostics checks failed.' }
} finally { Pop-Location }
