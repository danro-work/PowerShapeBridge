param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\open-access-restore-ci'),[string]$DotnetPath)
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence must remain in project.'}
$taskStage=Join-Path $PSScriptRoot ('work\open-access-restore-ci-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-open-access-restore.json')
if($LASTEXITCODE -ne 0){throw 'Restore fixture build failed.'}
& (Join-Path $taskStage 'PSBridge.Worker.exe') --open-access-restore-test 2>&1 | Set-Content -LiteralPath (Join-Path $taskEvidence 'marker-refusal.txt') -Encoding utf8
if($LASTEXITCODE -ne 3){throw 'Unmarked restore fixture must refuse without COM/CAD.'}
Set-Content -LiteralPath (Join-Path $taskStage '.open-access-restore-fixture') -Value 'Synthetic successful OPEN/current context only; never deploy.'
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --open-access-restore-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if($taskCode -ne 0){throw "Restore fixture failed: $taskOutput"}
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\open-access-restore-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
if($DotnetPath){
 & $DotnetPath run --project (Join-Path $PSScriptRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj') --no-restore --configuration Release -- --open-access-history (Join-Path $taskEvidence 'native-tests.json') (Join-Path $taskEvidence 'framework-sdk-history.json')
 if($LASTEXITCODE -ne 0){throw 'Actual Framework successful OPEN history rejected by SDK.'}
}
