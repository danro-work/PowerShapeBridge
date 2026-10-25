param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'evidence\owned-surface-translation-ci'))
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if (!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Surface translation output must remain in this project.' }
$taskStage=Join-Path $PSScriptRoot ('work\surface-translation-ci-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-surface-translations.json')
if ($LASTEXITCODE -ne 0) { throw 'Surface translation build failed.' }
New-Item -ItemType File -Path (Join-Path $taskStage '.surface-translation-fixture') | Out-Null
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --surface-translation-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if ($taskCode -ne 0) { throw "Surface translation fixture failed: $taskOutput" }
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\surface-translation-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
# Marker reaches only fake retained contexts; no COM/panel/deploy.
