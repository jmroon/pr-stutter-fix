[CmdletBinding()]
param(
    [string]$TypeName = 'Last.Entity.Field.FieldEntity',
    [string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR',
    [switch]$Interop
)
. "$PSScriptRoot/Environment.ps1"
if ($Interop) {
    $assemblyPath = Join-Path $GameDirectory 'BepInEx/interop/Assembly-CSharp.dll'
} else {
    $dump = Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts/latest-dump.json') -Raw | ConvertFrom-Json
    $assemblyPath = Join-Path $dump.OutputDirectory 'DummyDll/Assembly-CSharp.dll'
}
Push-Location $projectRoot
try {
    & $dotnet tool run ilspycmd -- -t $TypeName $assemblyPath
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect $TypeName ($LASTEXITCODE)." }
} finally { Pop-Location }
