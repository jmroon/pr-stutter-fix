[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR', [string]$CapturePath)
. "$PSScriptRoot/Environment.ps1"
Push-Location $projectRoot
try {
    & $dotnet build src/PRStutter.GridExperiment/PRStutter.GridExperiment.csproj -c Release --nologo "-p:GameDirectory=$GameDirectory"
    if ($LASTEXITCODE -ne 0) { throw 'Grid experiment build failed.' }
    $checkArgs = @((Join-Path $projectRoot 'src/PRStutter.GridExperiment/bin/Release/net6.0/PRStutter.GridExperiment.dll'))
    if ($CapturePath) { $checkArgs += (Resolve-Path -LiteralPath $CapturePath).Path }
    & $dotnet run --project tests/RenderChecks/RenderChecks.csproj -c Release -- @checkArgs
    if ($LASTEXITCODE -ne 0) { throw 'Grid experiment checks failed.' }
} finally { Pop-Location }
