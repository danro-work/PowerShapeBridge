param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\surface-set-partial-stop-ci'),[string]$DotnetPath)
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence must remain in project.'}
$taskStage=Join-Path $PSScriptRoot ('work\ssp-'+[guid]::NewGuid().ToString('N').Substring(0,12))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-surface-set-partial-stop.json')
if($LASTEXITCODE -ne 0){throw 'Partial-stop fixture build failed.'}
& (Join-Path $taskStage 'PSBridge.Worker.exe') --surface-set-partial-stop-test 2>&1 | Set-Content -LiteralPath (Join-Path $taskEvidence 'marker-refusal.txt') -Encoding utf8
if($LASTEXITCODE -ne 3){throw 'Unmarked passive fixture must refuse without CAD.'}
Set-Content -LiteralPath (Join-Path $taskStage '.surface-set-partial-stop-fixture') -Value 'Synthetic partial history and sampler only; never deploy.'
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --surface-set-partial-stop-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if($taskCode -ne 0){throw "Partial-stop fixture failed: $taskOutput"}
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\surface-set-partial-stop-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
if($DotnetPath){
 & $DotnetPath run --project (Join-Path $PSScriptRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj') --no-restore --configuration Release -- --surface-set-partial-stop (Join-Path $taskEvidence 'native-tests.json') (Join-Path $taskEvidence 'framework-sdk-proofs.json')
 if($LASTEXITCODE -ne 0){throw 'Actual Framework partial-stop proof failed SDK checks.'}
}
