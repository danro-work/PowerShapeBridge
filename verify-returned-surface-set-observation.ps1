param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\returned-set-observation-ci'),[string]$DotnetPath)
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskEvidence)){throw 'Fresh project evidence directory required.'}
New-Item -ItemType Directory -Path $taskEvidence | Out-Null
$taskStage=Join-Path $PSScriptRoot ('work\rso-'+[guid]::NewGuid().ToString('N').Substring(0,12))
$taskConfig=Get-Content (Join-Path $PSScriptRoot 'config\general-returned-surface-set-observation.json') -Raw | ConvertFrom-Json -AsHashtable
foreach($taskKey in @('returnedSurfaceSetObservationExperimental','ownedSurfaceSetRotationsExperimental','ownedOpenAccessRestoreExperimental','ownedSurfaceSetTranslationsExperimental','ownedSurfaceTranslationsExperimental','ownedSurfaceRotationsExperimental','ownedRotationPlansExperimental','rotationPlansExperimental','operationPlansExperimental','workingCopyOpenExperimental','ownedTranslationsExperimental','ownedOperationPlansExperimental','workingCopiesExperimental','documentBindingsExperimental','entitySetsExperimental','surfaceSamplingExperimental')){$taskConfig[$taskKey]=$true}
$taskConfigPath=Join-Path $taskEvidence 'fixture.config.json'
$taskConfig | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskConfigPath -Encoding utf8
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath $taskConfigPath
if($LASTEXITCODE -ne 0){throw 'Returned observation fixture build failed.'}
& (Join-Path $taskStage 'PSBridge.Worker.exe') --returned-set-observation-test 2>&1 | Set-Content -LiteralPath (Join-Path $taskEvidence 'marker-refusal.txt') -Encoding utf8
if($LASTEXITCODE -ne 3){throw 'Unmarked observer fixture must refuse without CAD.'}
Set-Content -LiteralPath (Join-Path $taskStage '.returned-set-observation-fixture') -Value 'Isolated fake retained contexts; never COM or deployment.'
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --returned-set-observation-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if($taskCode -ne 0){throw "Returned-set observer fixture failed: $taskOutput"}
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\returned-set-observation-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
if($DotnetPath){
 & $DotnetPath run --project (Join-Path $PSScriptRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj') --no-restore --configuration Release -- --returned-set-observation (Join-Path $taskEvidence 'native-tests.json') (Join-Path $taskEvidence 'framework-sdk-proofs.json')
 if($LASTEXITCODE -ne 0){throw 'Independent Framework/SDK observer proof checks failed.'}
}
