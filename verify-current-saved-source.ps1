param(
 [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\current-saved-source'),
 [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\current-saved-source')
)
$ErrorActionPreference='Stop'
if((Test-Path -LiteralPath $StageDirectory) -or (Test-Path -LiteralPath $EvidenceDirectory)){throw 'Fresh isolated current-source stage and evidence required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-open-adoption.json')
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --current-saved-source-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.json') -Encoding utf8
if($taskExit -ne 3){throw 'Unmarked current-source fixture must refuse.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture wrote test files.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.current-saved-source-fixture') -Value 'Own opaque parser fixtures only; never deploy.'
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --current-saved-source-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'targeted.json') -Encoding utf8
if($taskExit -ne 0){throw 'Current saved-result provenance guards failed.'}
$taskResult=$taskOutput | ConvertFrom-Json
if($taskResult.state -ne 'passed_current_saved_source_guards' -or $taskResult.checks -ne 68 -or $taskResult.cadCommands -ne 0){throw 'Incomplete current-source checks.'}
Write-Output 'PASS: 68 current saved-result provenance guards; no CAD, SDK lease, or deployment.'
