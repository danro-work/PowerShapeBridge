param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'evidence\owned-surface-selection-ci'))
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if (!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Surface selection output must remain in this project.' }
$taskStage=Join-Path $PSScriptRoot ('work\surface-selection-ci-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-surface-selection.json')
if ($LASTEXITCODE -ne 0) { throw 'Surface selection build failed.' }
New-Item -ItemType File -Path (Join-Path $taskStage '.surface-selection-fixture') | Out-Null
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --surface-selection-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if ($taskCode -ne 0) { throw "Surface selection fixture failed: $taskOutput" }
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\surface-selection-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
# This explicit marker reaches only fake retained contexts; no COM/panel/deploy.
