[CmdletBinding()]
param([string]$CapturePath, [string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
. "$PSScriptRoot/Environment.ps1"
Push-Location $projectRoot
try {
    & $dotnet build src/PRStutter.RenderExperiment/PRStutter.RenderExperiment.csproj -c Release --nologo "-p:GameDirectory=$GameDirectory"
    if ($LASTEXITCODE -ne 0) { throw 'Render experiment build failed.' }
    $checkArgs = @((Join-Path $projectRoot 'src/PRStutter.RenderExperiment/bin/Release/net6.0/PRStutter.RenderExperiment.dll'))
    if ($CapturePath) { $checkArgs += (Resolve-Path -LiteralPath $CapturePath).Path }
    & $dotnet run --project tests/RenderChecks/RenderChecks.csproj -c Release -- @checkArgs
    if ($LASTEXITCODE -ne 0) { throw 'Render experiment checks failed.' }
} finally { Pop-Location }
