[CmdletBinding()]
param([switch]$CheckBaseline, [string]$BaselinePath)
. "$PSScriptRoot/Environment.ps1"
$checks = [ordered]@{}
foreach ($script in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -Recurse) {
    $parseErrors = $null
    $tokens = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw "PowerShell syntax errors in $($script.Name): $parseErrors" }
}
$checks.PowerShellSyntax = 'passed'
Push-Location $projectRoot
try {
    $sdkVersion = & $dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -ne '10.0.401') { throw 'Pinned SDK verification failed.' }
    $checks.DotnetSdk = $sdkVersion
    $ilspyVersion = & $dotnet tool run ilspycmd -- --version
    if ($LASTEXITCODE -ne 0) { throw 'ILSpy verification failed.' }
    $checks.Ilspy = $ilspyVersion -join '; '
    $presentMonHelp = (& $presentMon --help 2>&1) -join "`n"
    # This pinned release prints help on stderr and exits 1 even for --help.
    if ($LASTEXITCODE -notin @(0, 1) -or $presentMonHelp -notmatch '^PresentMon 2\.6\.0' -or $presentMonHelp -notmatch '--process_name') {
        throw 'PresentMon help check failed.'
    }
    $checks.PresentMon = '2.6.0 CLI runs; live capture not yet tested'
    $pluginPath = Join-Path $projectRoot 'src/PRStutter.Diagnostics/bin/Release/net6.0/PRStutter.Diagnostics.dll'
    if (-not (Test-Path -LiteralPath $pluginPath)) { throw 'Plugin output missing. Run Build.ps1.' }
    $checks.PluginSha256 = (Get-FileHash -LiteralPath $pluginPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $dump = Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts/latest-dump.json') -Raw | ConvertFrom-Json
    foreach ($file in @('dump.cs', 'script.json', 'il2cpp.h', 'DummyDll/Assembly-CSharp.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $dump.OutputDirectory $file))) { throw "Dump output missing: $file" }
    }
    $checks.Il2CppDump = $dump.OutputDirectory
    $ghidraScriptLog = Join-Path $projectRoot "artifacts/ghidra/FF6-$($dump.AssemblySha256.Substring(0,12))-script.log"
    if (-not (Select-String -LiteralPath $ghidraScriptLog -Pattern 'PRSTUTTER_SETUP_OK_' -SimpleMatch -Quiet)) { throw 'Ghidra verification record missing.' }
    $checks.Ghidra = '12.1.4 native import and Java script execution passed'
    if ($CheckBaseline) {
        $baselineDirectory = if ($BaselinePath) {
            Get-Item -LiteralPath $BaselinePath
        } else {
            Get-ChildItem -LiteralPath (Join-Path $projectRoot 'artifacts/baselines') -Directory | Sort-Object Name -Descending | Select-Object -First 1
        }
        if (-not $baselineDirectory) { throw 'No baseline exists.' }
        $baseline = Get-Content -LiteralPath (Join-Path $baselineDirectory.FullName 'manifest.json') -Raw | ConvertFrom-Json
        foreach ($file in $baseline.Files) {
            $current = (Get-FileHash -LiteralPath (Join-Path $baseline.GameDirectory $file.RelativePath) -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($current -ne $file.Sha256) { throw "Game baseline changed: $($file.RelativePath)" }
        }
        $checks.Baseline = "All $($baseline.Files.Count) recorded files unchanged"
    }
    $checks.VerifiedUtc = [DateTime]::UtcNow.ToString('o')
    $checks | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/setup-verification.json')
    $checks | ConvertTo-Json -Depth 4
} finally { Pop-Location }
