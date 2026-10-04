[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
. "$PSScriptRoot/Environment.ps1"
$assembly = Join-Path $GameDirectory 'GameAssembly.dll'
$assemblyHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant()
$projectDirectory = Join-Path $projectRoot 'artifacts/ghidra'
$projectName = "FF6-$($assemblyHash.Substring(0, 12))"
New-Item -ItemType Directory -Force -Path $projectDirectory | Out-Null
$oldJavaOptions = $env:JAVA_TOOL_OPTIONS
# Scope preferences and caches to this workspace; leave the user's Java configuration alone.
$env:JAVA_TOOL_OPTIONS = "-Dapplication.settingsdir=`"$projectRoot/.state/ghidra`" -Dapplication.cachedir=`"$projectRoot/.cache/ghidra`" -Dapplication.tempdir=`"$projectRoot/.cache/ghidra-tmp`""
$logPath = Join-Path $projectDirectory "$projectName-setup.log"
$scriptLogPath = Join-Path $projectDirectory "$projectName-script.log"
$runId = [Guid]::NewGuid().ToString('N')
$modeArguments = @('-import', $assembly)
if (Test-Path -LiteralPath (Join-Path $projectDirectory "$projectName.gpr")) {
    $modeArguments = @('-process', 'GameAssembly.dll')
}
try {
    & (Join-Path $ghidra 'support/analyzeHeadless.bat') $projectDirectory $projectName @modeArguments `
        -noanalysis -max-cpu 4 -scriptPath (Join-Path $PSScriptRoot 'ghidra') `
        -postScript VerifyImport.java $runId -log $logPath -scriptlog $scriptLogPath
    if ($LASTEXITCODE -ne 0) { throw "Ghidra import failed ($LASTEXITCODE)." }
    if (-not (Select-String -LiteralPath $scriptLogPath -Pattern "PRSTUTTER_SETUP_OK_$runId" -SimpleMatch -Quiet)) {
        throw 'Ghidra did not report successful verification. Inspect its logs.'
    }
    Write-Host "Ghidra project ready: $projectDirectory/$projectName.gpr"
} finally { $env:JAVA_TOOL_OPTIONS = $oldJavaOptions }
