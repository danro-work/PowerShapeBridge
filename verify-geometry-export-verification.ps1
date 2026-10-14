param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\geometry-export-verification'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-export-verification')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh own verifier stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-translations.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --geometry-export-verification-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Verifier marker refusal failed.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Marker refusal created fixtures.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.geometry-export-verification-fixture') -Value 'Own files and independent fake read context only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --geometry-export-verification-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Independent verifier matrix failed; preserve stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_geometry_export_verification.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent verifier audit failed.'}
