[CmdletBinding()]
param([string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR')
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/Setup-Tools.ps1"
& "$PSScriptRoot/Restore-Tools.ps1"
& "$PSScriptRoot/Snapshot-Game.ps1" -GameDirectory $GameDirectory
& "$PSScriptRoot/Build.ps1" -GameDirectory $GameDirectory
& "$PSScriptRoot/Dump-Game.ps1" -GameDirectory $GameDirectory
& "$PSScriptRoot/Import-Ghidra.ps1" -GameDirectory $GameDirectory
& "$PSScriptRoot/Verify-Setup.ps1"
