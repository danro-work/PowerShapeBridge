param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [Parameter(Mandatory=$true)][string]$DotnetPath,
    [Parameter(Mandatory=$true)][string]$HostPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\owned-uniform-scale-wire'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-owned-scale-sdk\wire')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh isolated scale wire stage required.'}
if(Test-Path -LiteralPath $EvidenceDirectory){throw 'Fresh scale wire evidence required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-uniform-scale-plans.json')
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --uniform-scale-wire-seed control 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Scale wire CLI must refuse without marker.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture created artifacts.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-uniform-scale-fixture') -Value 'Own isolated fake contexts only; never deploy.'
& $PythonPath (Join-Path $PSScriptRoot 'src\test_sdk_owned_uniform_scale_wire_stdio.py') --dotnet $DotnetPath --host $HostPath --stage $StageDirectory --evidence $EvidenceDirectory
if($LASTEXITCODE -ne 0){throw 'Scoped native scale STDIO failed; preserve stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_owned_uniform_scale_wire_stdio.py') --stage $StageDirectory --evidence $EvidenceDirectory --output (Join-Path $EvidenceDirectory 'wire-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent scale wire artifact audit failed.'}
