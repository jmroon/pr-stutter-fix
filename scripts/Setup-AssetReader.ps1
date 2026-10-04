[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$environment = Join-Path $projectRoot '.tools/asset-python'
$readerPython = Join-Path $environment 'Scripts/python.exe'
if (-not (Test-Path -LiteralPath $readerPython)) {
    python -m venv $environment
    if ($LASTEXITCODE -ne 0) { throw 'Asset-reader environment creation failed.' }
}
& $readerPython -m pip install --disable-pip-version-check --only-binary=:all: --cache-dir (Join-Path $projectRoot '.cache/pip') -r (Join-Path $PSScriptRoot 'asset-reader-requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Asset-reader installation failed.' }
