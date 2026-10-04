[CmdletBinding()]
param([string]$GameDirectory, [ValidateSet('FFVI','FFIV')][string]$Game = 'FFVI', [string]$CapturePath)
. "$PSScriptRoot/Environment.ps1"
. "$PSScriptRoot/Get-GameProfile.ps1"
$profile = Get-PrGameProfile -Game $Game -GameDirectory $GameDirectory
Assert-PrGameBuild $profile
$GameDirectory = $profile.Directory
Push-Location $projectRoot
try {
    & $dotnet build src/PRStutter.GridExperiment/PRStutter.GridExperiment.csproj -c Release --nologo "-p:GameDirectory=$GameDirectory" "-p:PrGame=$Game"
    if ($LASTEXITCODE -ne 0) { throw 'Grid experiment build failed.' }
    $checkArgs = @((Join-Path $projectRoot "src/PRStutter.GridExperiment/$($profile.Output)/PRStutter.GridExperiment.dll"))
    if ($CapturePath) { $checkArgs += (Resolve-Path -LiteralPath $CapturePath).Path }
    & $dotnet run --project tests/RenderChecks/RenderChecks.csproj -c Release -- @checkArgs
    if ($LASTEXITCODE -ne 0) { throw 'Grid experiment checks failed.' }
} finally { Pop-Location }
