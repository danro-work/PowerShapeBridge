param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\rotation-verification'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-rotation-verification')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh independent verifier stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-rotation-plans.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --rotation-verification-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Unmarked verifier fixture must refuse.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked verifier created files.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.rotation-verification-fixture') -Value 'Own analytic contexts only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --rotation-verification-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Independent sample verifier tests failed.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_rotation_verification.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent verifier audit failed.'}
