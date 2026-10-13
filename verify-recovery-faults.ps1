param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\recovery-faults'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K7-fault-matrix')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fault verification requires a new owned staging directory.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-saved-reopen.json')
Set-Content -LiteralPath (Join-Path $StageDirectory '.recovery-fault-fixture') -Value 'Owned offline fault fixtures only; never deploy.'
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --recovery-fault-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-core.txt')
if($LASTEXITCODE -ne 0){throw 'Native core fault fixtures failed; preserve the stage for inspection.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_recovery_fault_matrix.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-core-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent native fault artifact audit failed.'}
Write-Output 'PASS: native core fault matrix; SDK transport matrix and checkpoint recovery still required.'
