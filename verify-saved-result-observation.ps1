param(
 [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\saved-result-observation'),
 [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\saved-result-observation')
)
$ErrorActionPreference='Stop'
if((Test-Path -LiteralPath $StageDirectory) -or (Test-Path -LiteralPath $EvidenceDirectory)){throw 'Fresh isolated observation stage and evidence required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-open-adoption.json')
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --saved-result-observation-test | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.json') -Encoding utf8
if($LASTEXITCODE -ne 3){throw 'Unmarked own fixture must refuse.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture wrote test files.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.saved-result-observation-fixture') -Value 'Own opaque files and fake contexts only; never deploy.'
$taskOutput=& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --saved-result-observation-test
$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'targeted.json') -Encoding utf8
if($taskExit -ne 0){throw 'Passive saved-result guards failed.'}
$taskResult=$taskOutput | ConvertFrom-Json
if($taskResult.state -ne 'passed_saved_result_observation_guards' -or $taskResult.checks -ne 78 -or $taskResult.cadCommands -ne 0){throw 'Incomplete passive observation checks.'}
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') /nologo /platform:x64 /r:System.Web.Extensions.dll "/out:$(Join-Path $StageDirectory 'ObserveSavedResult.exe')" (Join-Path $PSScriptRoot 'tools\ObserveSavedResult.cs')
if($LASTEXITCODE -ne 0){throw 'Qualification caller compilation failed.'}
& (Join-Path $StageDirectory 'ObserveSavedResult.exe')
if($LASTEXITCODE -ne 2){throw 'Unconfigured caller must refuse before COM.'}
Write-Output 'PASS: 78 saved-result observation guards and unconfigured getter caller; no CAD.'
