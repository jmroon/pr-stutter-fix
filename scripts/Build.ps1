[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
. "$PSScriptRoot/Environment.ps1"
Push-Location $projectRoot
try {
    & $dotnet build 'src/PRStutter.Diagnostics/PRStutter.Diagnostics.csproj' --configuration Release "-p:GameDirectory=$GameDirectory" --nologo
    if ($LASTEXITCODE -ne 0) { throw "Plugin build failed ($LASTEXITCODE)." }
} finally { Pop-Location }
