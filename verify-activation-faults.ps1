param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\activation-faults'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K7-activation')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Activation fault verification requires a fresh owned stage.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-saved-reopen.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --activation-fault-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Activation CLI must refuse before creating fixtures or COM without its marker.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Marker refusal created fixtures.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.activation-fault-fixture') -Value 'Owned offline activation fixtures only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --activation-fault-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'activation-core.txt')
if($LASTEXITCODE -ne 0){throw 'Activation fixtures failed; preserve the stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_activation_faults.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'activation-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent activation artifact audit failed.'}
Write-Output 'PASS: isolated activation context/publication fault matrix; not full K7.'
