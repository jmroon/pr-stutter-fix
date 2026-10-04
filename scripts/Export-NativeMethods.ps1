[CmdletBinding()]
param(
    [string]$MethodPattern = '^(Last\.Entity\.Field\.Field(Entity|SpriteEntity)\$\$(MoveTo|UpdateEntity|UpdateMovingSetPosition|UpdateMoveFinishedSetPosition)|CameraFollowing\$\$UpdateController)$',
    [ValidatePattern('^0x[0-9a-fA-F]{1,8}$')][string[]]$ExtraRva = @(),
    [ValidateSet('FFVI','FFIV')][string]$Game = 'FFVI'
)
. "$PSScriptRoot/Environment.ps1"
. "$PSScriptRoot/Get-GameProfile.ps1"
$gameProfile = Get-PrGameProfile -Game $Game
Assert-PrGameBuild $gameProfile
if ($Game -eq 'FFIV') {
    $artifactRoot = Join-Path $projectRoot 'artifacts/ff4'
    $dumpDirectory = Join-Path $artifactRoot 'il2cpp'
    $projectName = "FF4-$($gameProfile.AssemblyHash.Substring(0, 12))"
} else {
    $artifactRoot = Join-Path $projectRoot 'artifacts'
    $dump = Get-Content -LiteralPath (Join-Path $artifactRoot 'latest-dump.json') -Raw | ConvertFrom-Json
    if ($dump.AssemblySha256 -ne $gameProfile.AssemblyHash -or $dump.MetadataSha256 -ne $gameProfile.MetadataHash) {
        throw 'FFVI dump does not match the inspected game profile.'
    }
    $dumpDirectory = $dump.OutputDirectory
    $projectName = "FF6-$($gameProfile.AssemblyHash.Substring(0, 12))"
}
$projectDirectory = Join-Path $artifactRoot 'ghidra'
$output = Join-Path $artifactRoot 'native'
foreach ($required in @((Join-Path $dumpDirectory 'script.json'), (Join-Path $projectDirectory "$projectName.gpr"))) {
    if (!(Test-Path -LiteralPath $required)) { throw "Missing inspection input: $required" }
}
New-Item -ItemType Directory -Force -Path $output | Out-Null
$runId = [Guid]::NewGuid().ToString('N')
$log = Join-Path $output "$runId.log"
$patternFile = Join-Path $output "$runId.pattern.txt"
$MethodPattern | Set-Content -LiteralPath $patternFile -Encoding utf8NoBOM
$extraFile = Join-Path $output "$runId.extra-rvas.txt"
[System.IO.File]::WriteAllLines($extraFile, $ExtraRva)
$oldJavaOptions = $env:JAVA_TOOL_OPTIONS
$env:JAVA_TOOL_OPTIONS = "-Dapplication.settingsdir=`"$projectRoot/.state/ghidra`" -Dapplication.cachedir=`"$projectRoot/.cache/ghidra`" -Dapplication.tempdir=`"$projectRoot/.cache/ghidra-tmp`""
try {
    & (Join-Path $ghidra 'support/analyzeHeadless.bat') $projectDirectory $projectName `
        -process GameAssembly.dll -noanalysis -max-cpu 4 -scriptPath (Join-Path $PSScriptRoot 'ghidra') `
        -postScript ExportMethods.java (Join-Path $dumpDirectory 'script.json') $output $patternFile $extraFile -scriptlog $log
    if ($LASTEXITCODE -ne 0 -or -not (Select-String -LiteralPath $log -Pattern 'PRSTUTTER_EXPORT_OK' -Quiet)) {
        throw "Native export failed. Inspect $log"
    }
} finally { $env:JAVA_TOOL_OPTIONS = $oldJavaOptions }
