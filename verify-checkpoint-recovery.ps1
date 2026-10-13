param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\checkpoint-recovery'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K7-checkpoint-recovery')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Checkpoint recovery qualification requires a fresh stage.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-checkpoint-recovery.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --checkpoint-recovery-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Checkpoint recovery CLI did not refuse without its marker.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Marker refusal created fixtures.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.checkpoint-recovery-fixture') -Value 'Owned filesystem fixtures only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --checkpoint-recovery-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-checkpoint-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Native checkpoint tests failed; preserve the stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_checkpoint_recovery.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-checkpoint-audit.json')
if($LASTEXITCODE -ne 0){throw 'Checkpoint artifact audit failed.'}
