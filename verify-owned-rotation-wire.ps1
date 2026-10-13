param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [Parameter(Mandatory=$true)][string]$DotnetPath,
    [Parameter(Mandatory=$true)][string]$HostPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\owned-rotation-wire'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-owned-rotation-wire')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh isolated rotation wire stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-rotation-plans.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --rotation-wire-seed control 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Rotation wire CLI must refuse without marker.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture created artifacts.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-rotation-fixture') -Value 'Own isolated fake contexts only; never deploy.'
& $PythonPath (Join-Path $PSScriptRoot 'src\test_sdk_owned_rotation_wire_stdio.py') --dotnet $DotnetPath --host $HostPath --stage $StageDirectory --evidence $EvidenceDirectory
if($LASTEXITCODE -ne 0){throw 'Scoped native rotation STDIO failed; preserve stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_owned_rotation_wire_stdio.py') --stage $StageDirectory --evidence $EvidenceDirectory --output (Join-Path $EvidenceDirectory 'wire-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent scoped rotation artifact audit failed.'}
