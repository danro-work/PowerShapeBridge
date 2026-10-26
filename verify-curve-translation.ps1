param([string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\curve-translation-ci'),[string]$DotnetPath)
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Output must remain in this project.'}
$taskStage=Join-Path $PSScriptRoot ('work\curve-translation-ci-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-curve-translations.json')
if($LASTEXITCODE -ne 0){throw 'Curve fixture build failed.'}
$taskRefusal=& (Join-Path $taskStage 'PSBridge.Worker.exe') --curve-translation-test 2>&1
$taskCode=$LASTEXITCODE
$taskRefusal | Set-Content -LiteralPath (Join-Path $taskEvidence 'marker-refusal.txt') -Encoding utf8
if($taskCode -ne 3){throw 'Unmarked fixture must refuse without CAD.'}
Set-Content -LiteralPath (Join-Path $taskStage '.curve-translation-fixture') -Value 'Fake retained context only; never deploy.'
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --curve-translation-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if($taskCode -ne 0){throw "Curve fixture failed: $taskOutput"}
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\curve-translation-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
if($DotnetPath){
    & $DotnetPath run --project (Join-Path $PSScriptRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj') --no-restore --configuration Release -- --curve-translation (Join-Path $taskEvidence 'native-tests.json') (Join-Path $taskEvidence 'framework-sdk-proofs.json')
    if($LASTEXITCODE -ne 0){throw 'Framework/SDK curve translation proof checks failed.'}
}
# Marked fake contexts only. Does not contact PowerShape, a panel or runtime.
