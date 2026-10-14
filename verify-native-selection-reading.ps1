param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\native-selection-reading'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-native-selection')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh own selection reader stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-translations.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --native-selection-reading-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Passive selection marker refusal failed.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Marker refusal created fixtures.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.native-selection-fixture') -Value 'Own arrays/fake read context only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --native-selection-reading-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Passive native selection tests failed.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_native_selection_reading.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent native selection audit failed.'}
