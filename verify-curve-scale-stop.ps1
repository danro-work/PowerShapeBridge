param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\curve-scale-stop-ci'),[string]$DotnetPath)
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Stop-inspection evidence must remain in this project.'}
$taskStage=Join-Path $PSScriptRoot ('work\curve-scale-stop-ci-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-curve-scale-stop.json')
if($LASTEXITCODE -ne 0){throw 'Stop-inspection build failed.'}
& (Join-Path $taskStage 'PSBridge.Worker.exe') --curve-scale-stop-test 2>&1 | Set-Content -LiteralPath (Join-Path $taskEvidence 'marker-refusal.txt') -Encoding utf8
if($LASTEXITCODE -ne 3){throw 'Unmarked stop-inspection fixture must refuse without CAD.'}
Set-Content -LiteralPath (Join-Path $taskStage '.curve-scale-stop-fixture') -Value 'Fake getters only; never deploy.'
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --curve-scale-stop-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if($taskCode -ne 0){throw "Stop-inspection fixture failed: $taskOutput"}
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\curve-scale-stop-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
if($DotnetPath){
 & $DotnetPath run --project (Join-Path $PSScriptRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj') --no-restore --configuration Release -- --curve-scale-stop (Join-Path $taskEvidence 'native-tests.json') (Join-Path $taskEvidence 'framework-sdk-proofs.json')
 if($LASTEXITCODE -ne 0){throw 'Actual Framework/SDK stop-inspection proof checks failed.'}
}
