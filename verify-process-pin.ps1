param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\process-pin-ci'))
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Process pin evidence must remain in project.'}
$taskStage=Join-Path $PSScriptRoot ('work\process-pin-ci-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-core.json')
if($LASTEXITCODE -ne 0){throw 'Process pin fixture build failed.'}
& (Join-Path $taskStage 'PSBridge.Worker.exe') --process-pin-test 2>&1 | Set-Content -LiteralPath (Join-Path $taskEvidence 'marker-refusal.txt') -Encoding utf8
if($LASTEXITCODE -ne 3){throw 'Unmarked process pin fixture must refuse without COM/CAD.'}
Set-Content -LiteralPath (Join-Path $taskStage '.process-pin-fixture') -Value 'Synthetic identity checks only; never deploy.'
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --process-pin-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if($taskCode -ne 0){throw "Process pin fixture failed: $taskOutput"}
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\process-pin-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
