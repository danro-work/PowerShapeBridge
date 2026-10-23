param(
 [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\saved-result-restore'),
 [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\saved-result-restore')
)
$ErrorActionPreference='Stop'
if((Test-Path -LiteralPath $StageDirectory) -or (Test-Path -LiteralPath $EvidenceDirectory)){throw 'Fresh isolated restoration stage and evidence required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-saved-result-restore.json')
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --saved-result-restore-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.json') -Encoding utf8
if($taskExit -ne 3){throw 'Unmarked restoration fixture must refuse.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture wrote test files.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.saved-result-restore-fixture') -Value 'Own opaque parser fixtures only; never deploy.'
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --saved-result-restore-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'targeted.json') -Encoding utf8
if($taskExit -ne 0){throw 'Saved-result restoration wire guards failed.'}
$taskResult=$taskOutput | ConvertFrom-Json
if($taskResult.state -ne 'passed_saved_result_restore_wire_guards' -or $taskResult.checks -ne 21 -or $taskResult.cadCommands -ne 0){throw 'Incomplete restoration wire checks.'}
Write-Output 'PASS: 21 restoration config/provenance wire checks; no CAD.'
