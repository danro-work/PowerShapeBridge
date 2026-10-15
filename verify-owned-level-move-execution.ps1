param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\owned-level-move-execution'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-owned-level-move-execution')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh isolated level execution stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-levels-read.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-level-move-execution-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Level execution marker refusal failed.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Marker refusal created fixtures.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-level-move-execution-fixture') -Value 'Synthetic files/fake callbacks only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-level-move-execution-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Level executor/independent verifier tests failed.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_owned_level_move_execution.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent level execution evidence audit failed.'}
