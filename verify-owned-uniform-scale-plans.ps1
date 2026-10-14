param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\owned-uniform-scale-plans'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-owned-uniform-scale-plans')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh scale fixture stage required.'}
if(Test-Path -LiteralPath $EvidenceDirectory){throw 'Fresh scale evidence required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-translations.json')
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-uniform-scale-plan-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Scale fixture must refuse without marker.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Marker refusal created fixtures.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-uniform-scale-fixture') -Value 'Own files/fake retained contexts only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-uniform-scale-plan-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Scale plan guards failed; preserve stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_owned_uniform_scale_plans.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent scale plan audit failed.'}
