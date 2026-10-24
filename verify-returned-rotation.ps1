param([string]$StageDirectory=(Join-Path $PSScriptRoot 'work\returned-rotation'),[string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\returned-rotation'))
$ErrorActionPreference='Stop'
if((Test-Path -LiteralPath $StageDirectory) -or (Test-Path -LiteralPath $EvidenceDirectory)){throw 'Fresh isolated returned-rotation stage/evidence required.'}
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$taskFixtureConfig=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'config\general-owned-saved-finalization.json') -Raw | ConvertFrom-Json
$taskFixtureConfig.ownedSavedFinalizationExperimental=$false
$taskFixtureConfigPath=Join-Path $EvidenceDirectory 'fixture.config.json'
$taskFixtureConfig | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskFixtureConfigPath -Encoding utf8
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath $taskFixtureConfigPath
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --returned-rotation-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.json') -Encoding utf8
if($taskExit -ne 3){throw 'Unmarked returned-rotation fixture must refuse.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.returned-rotation-fixture') -Value 'Own isolated returned-rotation fixtures only; never deploy.'
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --returned-rotation-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'targeted.json') -Encoding utf8
if($taskExit -ne 0){throw 'Returned-rotation guards failed.'}
$taskResult=$taskOutput | ConvertFrom-Json
if($taskResult.state -ne 'passed_returned_rotation_evidence' -or $taskResult.checks -lt 15 -or $taskResult.cadCommands -ne 0){throw 'Incomplete returned-rotation guards.'}
Write-Output 'PASS: single returned-rotation evidence, unknown-journal guards and lease reserve; no CAD.'
