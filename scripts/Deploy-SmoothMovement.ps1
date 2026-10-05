[CmdletBinding()]
param([ValidateSet('FFVI','FFIV')][string]$Game='FFVI')
$ErrorActionPreference='Stop'
& "$PSScriptRoot/Deploy-ComparisonExperiment.ps1" -Game $Game -Integrated
