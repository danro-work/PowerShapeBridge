param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'evidence\owned-surface-rotation-ci'),[string]$DotnetPath)
$ErrorActionPreference='Stop'
$taskEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if (!$taskEvidence.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Surface rotation output must remain in this project.' }
$taskStage=Join-Path $PSScriptRoot ('work\surface-rotation-ci-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-surface-rotations.json')
if ($LASTEXITCODE -ne 0) { throw 'Surface rotation build failed.' }
New-Item -ItemType File -Path (Join-Path $taskStage '.surface-rotation-fixture') | Out-Null
$taskOutput=& (Join-Path $taskStage 'PSBridge.Worker.exe') --surface-rotation-test 2>&1
$taskCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
if ($taskCode -ne 0) { throw "Surface rotation fixture failed: $taskOutput" }
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\surface-rotation-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
Write-Output $taskOutput
if ($DotnetPath) {
    & $DotnetPath run --project (Join-Path $PSScriptRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj') --no-restore --configuration Release -- --surface-rotation (Join-Path $taskEvidence 'native-tests.json') (Join-Path $taskEvidence 'framework-sdk-proofs.json')
    if ($LASTEXITCODE -ne 0) { throw 'Framework/SDK surface rotation proof checks failed.' }
}
# Marked fake contexts only. No COM, panel, deployment or CAD qualification.
