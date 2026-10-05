$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ComparisonOutput.ps1"
$testRoot = Join-Path (Split-Path $PSScriptRoot) ('artifacts/tests/comparison-output-' + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $testRoot | Out-Null
$source = Join-Path $testRoot 'source.bin'
$destination = Join-Path $testRoot 'installed.bin'
[IO.File]::WriteAllBytes($source, [byte[]](0,1,255,128,10))
$entry = @{ InstalledHash = $null }
Install-ComparisonOutput @{Path=$destination; Source=$source; Text=$null} $entry
if (!(Test-Path -LiteralPath $destination) -or $entry.InstalledHash -ne (Get-FileHash $source).Hash) { throw 'Ordinary DLL was removed or corrupted.' }
$config = "[Debug]`nEnabled = false`n"
Install-ComparisonOutput @{Path=$destination; Source=$null; Text=$config} $entry
if ([IO.File]::ReadAllText($destination) -ne $config -or $entry.InstalledHash -eq 'ABSENT') { throw 'Configuration was removed or corrupted.' }
Install-ComparisonOutput @{Path=$destination; RemoveFile=$true} $entry
if ((Test-Path -LiteralPath $destination) -or $entry.InstalledHash -ne 'ABSENT') { throw 'Explicit removal failed.' }
Install-ComparisonOutput @{Path=$destination; RemoveFile=$true} $entry
if ((Test-Path -LiteralPath $destination) -or $entry.InstalledHash -ne 'ABSENT') { throw 'Already absent DLL failed.' }
Write-Host 'PASS: binary copy, exact configuration, explicit removal and absent-file installation; Hashtable methods cannot select removal.'
