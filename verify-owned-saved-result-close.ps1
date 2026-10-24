param([string]$StageDirectory=(Join-Path $PSScriptRoot 'work\owned-saved-result-close'),[string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\owned-saved-result-close'))
$ErrorActionPreference='Stop'
if((Test-Path -LiteralPath $StageDirectory) -or (Test-Path -LiteralPath $EvidenceDirectory)){throw 'Fresh isolated saved-Result close stage/evidence required.'}
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$taskFixtureConfig=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'config\general-owned-saved-result-close.json') -Raw | ConvertFrom-Json
$taskFixtureConfig.ownedSavedResultCloseExperimental=$true
$taskFixtureConfigPath=Join-Path $EvidenceDirectory 'fixture.config.json'
$taskFixtureConfig | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskFixtureConfigPath -Encoding utf8
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath $taskFixtureConfigPath
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-saved-result-close-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.json') -Encoding utf8
if($taskExit -ne 3){throw 'Unmarked saved-Result close fixture must refuse.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-saved-result-close-fixture') -Value 'Own isolated saved Result fixtures only; never deploy.'
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-saved-result-close-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'targeted.json') -Encoding utf8
if($taskExit -ne 0){throw 'Saved Result close guards failed.'}
$taskResult=$taskOutput | ConvertFrom-Json
if($taskResult.state -ne 'passed_owned_saved_result_close_guards' -or $taskResult.checks -ne 26 -or $taskResult.cadCommands -ne 0){throw 'Incomplete saved Result close guards.'}
Write-Output 'PASS: 26 saved Result close/profile/flag/wire/pending-fence guards; no CAD.'
