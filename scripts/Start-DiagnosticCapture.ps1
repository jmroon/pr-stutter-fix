[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
$ErrorActionPreference = 'Stop'
if (-not (Get-Process -Name 'FINAL FANTASY VI' -ErrorAction SilentlyContinue)) { throw 'Start the game and load a field map first.' }
$diagnosticsDirectory = Join-Path $GameDirectory 'BepInEx/diagnostics/PRStutter'
if (-not (Test-Path -LiteralPath $diagnosticsDirectory)) { throw 'The diagnostic plugin has not initialized. Check BepInEx/LogOutput.log.' }
Set-Content -LiteralPath (Join-Path $diagnosticsDirectory 'start.request') -Value 'start'
Write-Host 'Capture requested. Walk a repeatable route; the default capture stops after 15 seconds.'
