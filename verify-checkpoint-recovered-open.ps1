param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [Parameter(Mandatory=$true)][string]$DotnetPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\checkpoint-recovered-open'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K7-recovered-open')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Recovered OPEN qualification requires a fresh stage.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-checkpoint-recovery.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --checkpoint-recovered-open-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Recovered OPEN CLI did not refuse without marker.'}
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --checkpoint-wire-seed control 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'wire-seed-marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Wire fixture seed did not refuse without marker.'}
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --checkpoint-wire-request aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'wire-request-marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Wire request did not refuse without own marker.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Marker refusal created fixtures.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.checkpoint-recovery-fixture') -Value 'Own files and fake CAD only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --checkpoint-recovered-open-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-open-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Recovered OPEN tests failed; preserve stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_checkpoint_recovered_open.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-open-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent recovered OPEN audit failed.'}
& $DotnetPath run --no-restore --configuration Release --project (Join-Path $PSScriptRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj') -- --checkpoint-recovered-open $StageDirectory (Join-Path $EvidenceDirectory 'sdk-proof.json')
if($LASTEXITCODE -ne 0){throw 'Recovered OPEN SDK proof compatibility failed.'}
$taskWireEvidence=Join-Path $EvidenceDirectory 'wire'
$taskSdkHost=Join-Path $PSScriptRoot 'src\PowerShapeMcp.Host\bin\Release\net10.0\PowerShapeMcp.Host.dll'
& $PythonPath (Join-Path $PSScriptRoot 'src\test_sdk_checkpoint_wire_stdio.py') --dotnet $DotnetPath --host $taskSdkHost --stage $StageDirectory --evidence $taskWireEvidence
if($LASTEXITCODE -ne 0){throw 'Full SDK/native checkpoint wire workflow failed; preserve artifacts.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_checkpoint_wire_stdio.py') --stage $StageDirectory --evidence $taskWireEvidence --output (Join-Path $EvidenceDirectory 'wire-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent checkpoint wire artifact audit failed.'}
