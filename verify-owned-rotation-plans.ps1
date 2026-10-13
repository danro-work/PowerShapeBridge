param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\owned-rotation-plans'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-owned-rotation-plans')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh owned rotation stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-translations.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-rotation-plan-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Rotation fixture CLI must refuse without marker.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Marker refusal created fixtures.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-rotation-fixture') -Value 'Own files and fake contexts only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-rotation-plan-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Native owned rotation guard tests failed; preserve stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_owned_rotation_plans.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent owned rotation preparation audit failed.'}
