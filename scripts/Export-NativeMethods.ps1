[CmdletBinding()]
param(
    [string]$MethodPattern = '^(Last\.Entity\.Field\.Field(Entity|SpriteEntity)\$\$(MoveTo|UpdateEntity|UpdateMovingSetPosition|UpdateMoveFinishedSetPosition)|CameraFollowing\$\$UpdateController)$',
    [ValidatePattern('^0x[0-9a-fA-F]{1,8}$')][string[]]$ExtraRva = @()
)
. "$PSScriptRoot/Environment.ps1"
$dump = Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts/latest-dump.json') -Raw | ConvertFrom-Json
$projectDirectory = Join-Path $projectRoot 'artifacts/ghidra'
$projectName = "FF6-$($dump.AssemblySha256.Substring(0, 12))"
$output = Join-Path $projectRoot 'artifacts/native'
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
        -postScript ExportMethods.java (Join-Path $dump.OutputDirectory 'script.json') $output $patternFile $extraFile -scriptlog $log
    if ($LASTEXITCODE -ne 0 -or -not (Select-String -LiteralPath $log -Pattern 'PRSTUTTER_EXPORT_OK' -Quiet)) {
        throw "Native export failed. Inspect $log"
    }
} finally { $env:JAVA_TOOL_OPTIONS = $oldJavaOptions }
