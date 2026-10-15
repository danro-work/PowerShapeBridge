param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [Parameter(Mandatory=$true)][string]$DotnetPath,
    [Parameter(Mandatory=$true)][string]$HostPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\owned-level-move-wire'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-level-move-wire')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh isolated level wire stage required.'}
if(Test-Path -LiteralPath $EvidenceDirectory){throw 'Fresh level wire evidence required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-level-moves.json')
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --level-move-wire-seed control 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Level wire must refuse without isolated marker.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Marker refusal created fixtures.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-level-move-wire-fixture') -Value 'Isolated synthetic callbacks only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --level-move-admission-test | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'admission.json')
if($LASTEXITCODE -ne 0){throw 'Level recipe/config admission failed.'}
$taskDisabled=$StageDirectory+'-admission-disabled'
if(Test-Path -LiteralPath $taskDisabled){throw 'Fresh disabled-admission stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskDisabled -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-levels-read.json')
Set-Content -LiteralPath (Join-Path $taskDisabled '.owned-level-move-wire-fixture') -Value 'Isolated disabled-admission fixture only.'
& (Join-Path $taskDisabled 'PSBridge.Worker.exe') --level-move-admission-test | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'admission-disabled.json')
if($LASTEXITCODE -ne 0){throw 'Disabled native level admission failed.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\test_sdk_owned_level_move_wire_stdio.py') --dotnet $DotnetPath --host $HostPath --stage $StageDirectory --evidence $EvidenceDirectory
if($LASTEXITCODE -ne 0){throw 'SDK/native level wire failed; preserve artifacts.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_owned_level_move_wire_stdio.py') --stage $StageDirectory --evidence $EvidenceDirectory --output (Join-Path $EvidenceDirectory 'wire-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent level wire audit failed.'}
