param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\geometry-export-executor'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-geometry-export-executor')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh own export executor stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-translations.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --geometry-export-executor-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Export executor marker refusal failed.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Marker refusal created fixtures.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.geometry-export-executor-fixture') -Value 'Own files/fake contexts only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --geometry-export-executor-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Native export executor matrix failed; preserve stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_geometry_export_executor.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent executor audit failed.'}
