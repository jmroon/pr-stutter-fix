[CmdletBinding()]
param([string]$CapturePath, [string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
. "$PSScriptRoot/Environment.ps1"
Push-Location $projectRoot
try {
    & $dotnet build src/PRStutter.NativeScrollExperiment/PRStutter.NativeScrollExperiment.csproj -c Release --nologo "-p:GameDirectory=$GameDirectory"
    if ($LASTEXITCODE -ne 0) { throw 'Native scroll build failed.' }
    $checkArgs = @((Join-Path $projectRoot 'src/PRStutter.NativeScrollExperiment/bin/Release/net6.0/PRStutter.NativeScrollExperiment.dll'))
    if ($CapturePath) { $checkArgs += (Resolve-Path -LiteralPath $CapturePath).Path }
    & $dotnet run --project tests/RenderChecks/RenderChecks.csproj -c Release -- @checkArgs
    if ($LASTEXITCODE -ne 0) { throw 'Native scroll checks failed.' }
} finally { Pop-Location }
