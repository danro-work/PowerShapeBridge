param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\surface-set-stop-disposition-ci'),[string]$DotnetPath)
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence must remain in project.'}
$taskStage=Join-Path $PSScriptRoot ('work\ssd-'+[guid]::NewGuid().ToString('N').Substring(0,12))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-surface-set-stop-disposition.json')
if($LASTEXITCODE -ne 0){throw 'Disposition fixture build failed.'}
& (Join-Path $taskStage 'PSBridge.Worker.exe') --surface-set-stop-disposition-test 2>&1 | Set-Content -LiteralPath (Join-Path $taskEvidence 'marker-refusal.txt') -Encoding utf8
if($LASTEXITCODE -ne 3){throw 'Unmarked fixture must refuse without CAD.'}
Set-Content -LiteralPath (Join-Path $taskStage '.surface-set-stop-disposition-fixture') -Value 'Synthetic only; never deploy.'
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --surface-set-stop-disposition-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if($taskCode -ne 0){throw "Disposition fixture failed: $taskOutput"}
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\surface-set-stop-disposition-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
if($DotnetPath){
 & $DotnetPath run --project (Join-Path $PSScriptRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj') --no-restore --configuration Release -- --surface-set-stop-disposition (Join-Path $taskEvidence 'native-tests.json') (Join-Path $taskEvidence 'framework-sdk-proofs.json')
 if($LASTEXITCODE -ne 0){throw 'Actual Framework disposition proof failed SDK checks.'}
}
