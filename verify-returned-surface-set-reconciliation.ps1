param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\returned-set-reconciliation-ci'),[string]$DotnetPath)
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskEvidence)){throw 'Fresh project evidence required.'}
New-Item -ItemType Directory -Path $taskEvidence | Out-Null
$taskStage=Join-Path $PSScriptRoot ('work\rsr-'+[guid]::NewGuid().ToString('N').Substring(0,12))
$taskConfig=Get-Content (Join-Path $PSScriptRoot 'config\general-returned-surface-set-reconciliation.json') -Raw | ConvertFrom-Json -AsHashtable
foreach($taskKey in @('returnedSurfaceSetReconciliationExperimental','returnedSurfaceSetObservationExperimental','ownedSurfaceSetRotationsExperimental','ownedOpenAccessRestoreExperimental','ownedSurfaceSetTranslationsExperimental','ownedSurfaceTranslationsExperimental','ownedSurfaceRotationsExperimental','ownedRotationPlansExperimental','rotationPlansExperimental','operationPlansExperimental','workingCopyOpenExperimental','ownedTranslationsExperimental','ownedOperationPlansExperimental','workingCopiesExperimental','documentBindingsExperimental','entitySetsExperimental','surfaceSamplingExperimental')){$taskConfig[$taskKey]=$true}
$taskConfigPath=Join-Path $taskEvidence 'fixture.config.json'
$taskConfig | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskConfigPath -Encoding utf8
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath $taskConfigPath
if($LASTEXITCODE -ne 0){throw 'Reconciliation fixture build failed.'}
& (Join-Path $taskStage 'PSBridge.Worker.exe') --returned-set-reconciliation-test 2>&1 | Set-Content -LiteralPath (Join-Path $taskEvidence 'marker-refusal.txt') -Encoding utf8
if($LASTEXITCODE -ne 3){throw 'Unmarked reconciliation fixture must refuse without CAD.'}
Set-Content -LiteralPath (Join-Path $taskStage '.returned-set-reconciliation-fixture') -Value 'Isolated fake contexts only; never deploy.'
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --returned-set-reconciliation-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if($taskCode -ne 0){throw "Reconciliation fixture failed: $taskOutput"}
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\returned-set-reconciliation-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
if($DotnetPath){
 & $DotnetPath run --project (Join-Path $PSScriptRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj') --no-restore --configuration Release -- --returned-set-reconciliation (Join-Path $taskEvidence 'native-tests.json') (Join-Path $taskEvidence 'framework-sdk-proofs.json')
 if($LASTEXITCODE -ne 0){throw 'Independent reconciliation proof check failed.'}
}
