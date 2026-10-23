param(
 [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\saved-reopen-window-policy'),
 [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\saved-reopen-window-policy')
)
$ErrorActionPreference='Stop'
if((Test-Path -LiteralPath $StageDirectory) -or (Test-Path -LiteralPath $EvidenceDirectory)){throw 'Fresh isolated quota stage and evidence required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-saved-result-restore.json')
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --saved-reopen-window-policy-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.json') -Encoding utf8
if($taskExit -ne 3){throw 'Unmarked quota fixture must refuse.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture wrote test files.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.saved-reopen-window-policy-fixture') -Value 'Own isolated quota fixtures only; never deploy.'
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --saved-reopen-window-policy-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'targeted.json') -Encoding utf8
if($taskExit -ne 0){throw 'Saved reopen window quota guards failed.'}
$taskResult=$taskOutput | ConvertFrom-Json
if($taskResult.state -ne 'passed_saved_reopen_window_policy' -or $taskResult.checks -ne 24 -or $taskResult.cadCommands -ne 0){throw 'Incomplete quota checks.'}
Write-Output 'PASS: 24 saved-reopen quota checks; no CAD.'
