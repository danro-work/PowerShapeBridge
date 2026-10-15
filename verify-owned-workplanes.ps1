param(
 [Parameter(Mandatory=$true)][string]$PythonPath,
 [Parameter(Mandatory=$true)][string]$DotnetPath,
 [Parameter(Mandatory=$true)][string]$HostPath,
 [string]$StageDirectory=(Join-Path $PSScriptRoot ('work\wp-'+[Guid]::NewGuid().ToString('N').Substring(0,8))),
 [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-owned-workplanes')
)
$ErrorActionPreference='Stop'
if((Test-Path -LiteralPath $StageDirectory) -or (Test-Path -LiteralPath $EvidenceDirectory)){throw 'Fresh isolated workplane stage/evidence required.'}
& (Join-Path $PSScriptRoot build.ps1) -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot config\general-owned-workplanes.json)
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory PSBridge.Worker.exe) --owned-workplane-test 2>&1|Set-Content (Join-Path $EvidenceDirectory marker-refusal.txt)
if($LASTEXITCODE -ne 3){throw 'Unmarked native workplane test must refuse.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory work)){throw 'Unmarked test created artifacts.'}
Set-Content (Join-Path $StageDirectory .owned-workplane-fixture) 'Synthetic callbacks/owned files only; never deploy.'
& (Join-Path $StageDirectory PSBridge.Worker.exe) --owned-workplane-test|Tee-Object (Join-Path $EvidenceDirectory native-tests.txt)
if($LASTEXITCODE -ne 0){throw 'Native workplane controls failed.'}
& $PythonPath (Join-Path $PSScriptRoot src\audit_owned_workplanes.py) --stage $StageDirectory --output (Join-Path $EvidenceDirectory native-audit.json)
if($LASTEXITCODE -ne 0){throw 'Native workplane audit failed.'}
& (Join-Path $StageDirectory PSBridge.Worker.exe) --owned-workplane-admission-test|Set-Content (Join-Path $EvidenceDirectory admission.json)
if($LASTEXITCODE -ne 0){throw 'Native workplane admission failed.'}
$taskDisabled=$StageDirectory+'-off';New-Item -ItemType Directory -Path $taskDisabled | Out-Null
Copy-Item -LiteralPath (Join-Path $StageDirectory PSBridge.Worker.exe) -Destination $taskDisabled
$taskConfig=Get-Content (Join-Path $StageDirectory bridge.config.json) -Raw|ConvertFrom-Json;$taskConfig.ownedWorkplanesExperimental=$false
$taskConfig|ConvertTo-Json -Depth 6|Set-Content (Join-Path $taskDisabled bridge.config.json) -Encoding utf8
& (Join-Path $taskDisabled PSBridge.Worker.exe) --owned-workplane-admission-test|Set-Content (Join-Path $EvidenceDirectory admission-disabled.json)
if($LASTEXITCODE -ne 0){throw 'Disabled native workplane admission failed.'}
Set-Content (Join-Path $StageDirectory .owned-workplane-wire-fixture) 'Isolated workplane wire; simulated CAD only.'
$taskWire=Join-Path $EvidenceDirectory wire
& $PythonPath (Join-Path $PSScriptRoot src\test_sdk_owned_workplane_wire_stdio.py) --stage $StageDirectory --host $HostPath --dotnet $DotnetPath --evidence $taskWire
if($LASTEXITCODE -ne 0){throw 'Workplane SDK/native wire failed.'}
& $PythonPath (Join-Path $PSScriptRoot src\audit_owned_workplane_wire_stdio.py) --evidence $taskWire --output (Join-Path $taskWire wire-audit.json)
if($LASTEXITCODE -ne 0){throw 'Independent workplane wire audit failed.'}
$taskFaults=Join-Path $EvidenceDirectory transport
& $PythonPath (Join-Path $PSScriptRoot src\test_sdk_owned_workplane_transport.py) --stage $StageDirectory --host $HostPath --dotnet $DotnetPath --evidence $taskFaults
if($LASTEXITCODE -ne 0){throw 'Workplane SDK transport faults failed.'}
& $PythonPath (Join-Path $PSScriptRoot src\audit_owned_workplane_transport.py) --evidence $taskFaults --output (Join-Path $taskFaults transport-audit.json)
if($LASTEXITCODE -ne 0){throw 'Independent workplane transport audit failed.'}
