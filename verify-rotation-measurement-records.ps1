param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\rotation-measurement-records'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-rotation-measurement-records')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh immutable record stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-rotation-plans.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --rotation-measurement-record-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Unmarked record fixture must refuse.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture created records.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.rotation-measurement-record-fixture') -Value 'Own fake sample records; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --rotation-measurement-record-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Immutable measurement record tests failed.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_rotation_measurement_records.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent record audit failed.'}
