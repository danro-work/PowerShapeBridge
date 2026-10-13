param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\rotation-measurements'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-rotation-measurements')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh passive measurement stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-rotation-plans.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --rotation-measurement-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Unmarked measurement fixture must refuse.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture created files.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.rotation-measurement-fixture') -Value 'Own fake getter arrays only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --rotation-measurement-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Passive measurement matrix failed; preserve stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_rotation_measurements.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent measurement audit failed.'}
