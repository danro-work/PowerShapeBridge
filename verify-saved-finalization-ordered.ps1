param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\saved-finalization-ordered-ci'),[string]$DotnetPath)
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Set evidence must remain in project.'}
$taskStage=Join-Path $PSScriptRoot ('work\sfo-'+[guid]::NewGuid().ToString('N').Substring(0,12))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-saved-finalization-ordered.json')
if($LASTEXITCODE -ne 0){throw 'Set fixture build failed.'}
& (Join-Path $taskStage 'PSBridge.Worker.exe') --saved-finalization-ordered-test 2>&1 | Set-Content -LiteralPath (Join-Path $taskEvidence 'marker-refusal.txt') -Encoding utf8
if($LASTEXITCODE -ne 3){throw 'Unmarked set fixture must refuse without CAD.'}
Set-Content -LiteralPath (Join-Path $taskStage '.saved-finalization-ordered-fixture') -Value 'Synthetic set callbacks only; never deploy.'
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --saved-finalization-ordered-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if($taskCode -ne 0){throw "Set fixture failed: $taskOutput"}
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\saved-finalization-ordered-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
if($DotnetPath){
 & $DotnetPath run --project (Join-Path $PSScriptRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj') --no-restore --configuration Release -- --saved-finalization-ordered (Join-Path $taskEvidence 'native-tests.json') (Join-Path $taskEvidence 'framework-sdk-proofs.json')
 if($LASTEXITCODE -ne 0){throw 'Actual Framework/SDK set proof checks failed.'}
}
