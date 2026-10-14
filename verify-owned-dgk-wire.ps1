param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [Parameter(Mandatory=$true)][string]$DotnetPath,
    [Parameter(Mandatory=$true)][string]$HostPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\owned-dgk-wire'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K9-owned-dgk-wire')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh isolated DGK wire stage required.'}
if(Test-Path -LiteralPath $EvidenceDirectory){throw 'Fresh DGK wire evidence required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-dgk-targets.json')
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --dgk-target-wire-seed control 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'DGK wire CLI must refuse without marker.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture created artifacts.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-dgk-wire-fixture') -Value 'Own isolated fake reads only; never deploy.'
& $PythonPath (Join-Path $PSScriptRoot 'src\test_sdk_owned_dgk_wire_stdio.py') --dotnet $DotnetPath --host $HostPath --stage $StageDirectory --evidence $EvidenceDirectory
if($LASTEXITCODE -ne 0){throw 'Scoped native DGK STDIO failed; preserve stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_owned_dgk_wire_stdio.py') --stage $StageDirectory --evidence $EvidenceDirectory --output (Join-Path $EvidenceDirectory 'wire-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent DGK wire artifact audit failed.'}
