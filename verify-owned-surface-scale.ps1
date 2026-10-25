param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\owned-surface-scale-ci'),[string]$DotnetPath)
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Surface scale output must remain in this project.'}
$taskStage=Join-Path $PSScriptRoot ('work\surface-scale-ci-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-surface-scale.json')
if($LASTEXITCODE -ne 0){throw 'Surface scale build failed.'}
& (Join-Path $taskStage 'PSBridge.Worker.exe') --surface-scale-test 2>&1 | Set-Content -LiteralPath (Join-Path $taskEvidence 'marker-refusal.txt') -Encoding utf8
if($LASTEXITCODE -ne 3){throw 'Unmarked surface scale fixture must refuse without CAD.'}
Set-Content -LiteralPath (Join-Path $taskStage '.surface-scale-fixture') -Value 'Fake typed phase callbacks only; never deploy.'
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --surface-scale-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if($taskCode -ne 0){throw "Surface scale fixture failed: $taskOutput"}
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\surface-scale-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
if($DotnetPath){
 & $DotnetPath run --project (Join-Path $PSScriptRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj') --no-restore --configuration Release -- --surface-scale (Join-Path $taskEvidence 'native-tests.json') (Join-Path $taskEvidence 'framework-sdk-proofs.json')
 if($LASTEXITCODE -ne 0){throw 'Actual Framework/SDK surface scale proof checks failed.'}
}
# Marked fake contexts only. No COM, panel, deployment or CAD qualification.
