param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\surface-set-numerical-ci'),[string]$DotnetPath)
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Set evidence must remain in project.'}
$taskStage=Join-Path $PSScriptRoot ('work\ssn-'+[guid]::NewGuid().ToString('N').Substring(0,12))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-surface-set-numerical.json')
if($LASTEXITCODE -ne 0){throw 'Set fixture build failed.'}
& (Join-Path $taskStage 'PSBridge.Worker.exe') --surface-set-numerical-test 2>&1 | Set-Content -LiteralPath (Join-Path $taskEvidence 'marker-refusal.txt') -Encoding utf8
if($LASTEXITCODE -ne 3){throw 'Unmarked set fixture must refuse without CAD.'}
Set-Content -LiteralPath (Join-Path $taskStage '.surface-set-numerical-fixture') -Value 'Synthetic set callbacks only; never deploy.'
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --surface-set-numerical-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if($taskCode -ne 0){throw "Set fixture failed: $taskOutput"}
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\surface-set-numerical-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
if($DotnetPath){
 & $DotnetPath run --project (Join-Path $PSScriptRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj') --no-restore --configuration Release -- --surface-set-numerical (Join-Path $taskEvidence 'native-tests.json') (Join-Path $taskEvidence 'framework-sdk-proofs.json')
 if($LASTEXITCODE -ne 0){throw 'Actual Framework/SDK set proof checks failed.'}
}
