[CmdletBinding()]
param([ValidateSet('FFVI','FFIV')][string]$Game = 'FFVI', [string]$GameDirectory)
. "$PSScriptRoot/Environment.ps1"
. "$PSScriptRoot/Get-GameProfile.ps1"
$auditProfile = Get-PrGameProfile -Game $Game -GameDirectory $GameDirectory
Assert-PrGameBuild $auditProfile
Push-Location $projectRoot
try {
    & $dotnet build src/PRStutter.PresentationAudit/PRStutter.PresentationAudit.csproj -c Release --nologo "-p:GameDirectory=$($auditProfile.Directory)" "-p:PrGame=$Game"
    if ($LASTEXITCODE -ne 0) { throw 'Presentation audit build failed.' }
    & $dotnet run --project tests/PresentationAuditChecks/PresentationAuditChecks.csproj -c Release -- (Join-Path $projectRoot "src/PRStutter.PresentationAudit/$($auditProfile.Output)/PRStutter.PresentationAudit.dll")
    if ($LASTEXITCODE -ne 0) { throw 'Presentation audit checks failed.' }
} finally { Pop-Location }
