param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\curve-layout-ci'))
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Output must remain in this project.'}
$taskStage=Join-Path $PSScriptRoot ('work\curve-layout-ci-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-curve-layout.json')
if($LASTEXITCODE -ne 0){throw 'Curve fixture build failed.'}
$taskRefusal=& (Join-Path $taskStage 'PSBridge.Worker.exe') --curve-layout-test 2>&1
$taskCode=$LASTEXITCODE
$taskRefusal | Set-Content -LiteralPath (Join-Path $taskEvidence 'marker-refusal.txt') -Encoding utf8
if($taskCode -ne 3){throw 'Unmarked fixture must refuse without CAD.'}
Set-Content -LiteralPath (Join-Path $taskStage '.curve-layout-fixture') -Value 'Fake retained context only; never deploy.'
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --curve-layout-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if($taskCode -ne 0){throw "Curve fixture failed: $taskOutput"}
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\curve-layout-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
# Marked fake contexts only. Does not contact PowerShape, a panel or runtime.
