param(
 [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\owned-saved-copy-close'),
 [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\owned-saved-copy-close')
)
$ErrorActionPreference='Stop'
if((Test-Path -LiteralPath $StageDirectory) -or (Test-Path -LiteralPath $EvidenceDirectory)){throw 'Fresh isolated close stage and evidence required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-saved-result-restore.json')
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-saved-copy-close-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.json') -Encoding utf8
if($taskExit -ne 3){throw 'Unmarked close fixture must refuse.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture wrote test files.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-saved-copy-close-fixture') -Value 'Own isolated close fixtures only; never deploy.'
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-saved-copy-close-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'targeted.json') -Encoding utf8
if($taskExit -ne 0){throw 'Saved copy close guards failed.'}
$taskResult=$taskOutput | ConvertFrom-Json
if($taskResult.state -ne 'passed_owned_saved_copy_close_guards' -or $taskResult.checks -ne 28 -or $taskResult.cadCommands -ne 0){throw 'Incomplete close checks.'}
Write-Output 'PASS: 28 disposable saved-copy close/window/hash guards; no CAD.'
