param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [Parameter(Mandatory=$true)][string]$DotnetPath,
    [Parameter(Mandatory=$true)][string]$HostPath,
    [Parameter(Mandatory=$true)][string]$StageDirectory,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory
)
$ErrorActionPreference='Stop'
if ((Test-Path -LiteralPath $StageDirectory) -or (Test-Path -LiteralPath $EvidenceDirectory)) { throw 'Fresh isolated stage/evidence required.' }
& (Join-Path $PSScriptRoot build.ps1) -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot config\general-workplane-activation.json)
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$taskReaderCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $taskReaderCompiler /nologo /target:exe /platform:x64 /optimize+ /reference:System.Core.dll /reference:System.Web.Extensions.dll "/out:$(Join-Path $StageDirectory ReadOwnedWorkplaneFrames.exe)" (Join-Path $PSScriptRoot tools\ReadOwnedWorkplaneFrames.cs)
if ($LASTEXITCODE -ne 0) { throw 'Passive multi-frame reader compile failed.' }
& (Join-Path $StageDirectory ReadOwnedWorkplaneFrames.exe)
if ($LASTEXITCODE -ne 2) { throw 'Passive reader must refuse missing explicit arguments.' }
& (Join-Path $StageDirectory PSBridge.Worker.exe) --workplane-activation-test 2>&1 | Set-Content (Join-Path $EvidenceDirectory marker-refusal.txt)
if ($LASTEXITCODE -ne 3) { throw 'Unmarked test must refuse.' }
if (Test-Path -LiteralPath (Join-Path $StageDirectory work)) { throw 'Unmarked test created artifacts.' }
Set-Content -LiteralPath (Join-Path $StageDirectory .workplane-activation-fixture) 'Fake callbacks only; no CAD.'
& (Join-Path $StageDirectory PSBridge.Worker.exe) --workplane-activation-test | Set-Content (Join-Path $EvidenceDirectory native-controls.txt)
if ($LASTEXITCODE -ne 0) { throw 'Native activation controls failed.' }
Set-Content -LiteralPath (Join-Path $StageDirectory .workplane-activation-wire-fixture) 'SDK/native file path; fake CAD only.'
& $PythonPath (Join-Path $PSScriptRoot src\test_sdk_workplane_activation_stdio.py) --stage $StageDirectory --host $HostPath --dotnet $DotnetPath --evidence (Join-Path $EvidenceDirectory wire)
if ($LASTEXITCODE -ne 0) { throw 'SDK/native activation wire failed.' }
& $PythonPath (Join-Path $PSScriptRoot src\audit_workplane_activation_stdio.py) --evidence (Join-Path $EvidenceDirectory wire) --output (Join-Path $EvidenceDirectory wire\independent-audit.json)
if ($LASTEXITCODE -ne 0) { throw 'Independent wire audit failed.' }
& $PythonPath (Join-Path $PSScriptRoot tools\verify_live_activation_audit.py) --evidence (Join-Path $EvidenceDirectory wire) --output (Join-Path $EvidenceDirectory live-audit-controls.json)
if ($LASTEXITCODE -ne 0) { throw 'Live file auditor controls failed.' }
& $PythonPath (Join-Path $PSScriptRoot src\test_sdk_workplane_activation_stdio.py) --stage $StageDirectory --host $HostPath --dotnet $DotnetPath --evidence (Join-Path $EvidenceDirectory multiple-wire) --multiple
if ($LASTEXITCODE -ne 0) { throw 'Multiple-frame SDK/native activation failed.' }
& $PythonPath (Join-Path $PSScriptRoot src\audit_workplane_activation_stdio.py) --evidence (Join-Path $EvidenceDirectory multiple-wire) --output (Join-Path $EvidenceDirectory multiple-wire\independent-audit.json)
if ($LASTEXITCODE -ne 0) { throw 'Multiple-frame independent audit failed.' }
& $PythonPath (Join-Path $PSScriptRoot tools\verify_live_activation_audit.py) --evidence (Join-Path $EvidenceDirectory multiple-wire) --output (Join-Path $EvidenceDirectory multiple-live-audit-controls.json)
if ($LASTEXITCODE -ne 0) { throw 'Multiple-frame live auditor controls failed.' }
& $PythonPath (Join-Path $PSScriptRoot src\test_sdk_workplane_activation_transport.py) --stage $StageDirectory --host $HostPath --dotnet $DotnetPath --evidence (Join-Path $EvidenceDirectory transport)
if ($LASTEXITCODE -ne 0) { throw 'Transport fault checks failed.' }
& $PythonPath (Join-Path $PSScriptRoot src\audit_workplane_activation_transport.py) --evidence (Join-Path $EvidenceDirectory transport) --output (Join-Path $EvidenceDirectory transport\independent-audit.json)
if ($LASTEXITCODE -ne 0) { throw 'Independent transport audit failed.' }
& $PythonPath (Join-Path $PSScriptRoot src\test_sdk_workplane_activation_transport.py) --stage $StageDirectory --host $HostPath --dotnet $DotnetPath --evidence (Join-Path $EvidenceDirectory multiple-transport) --multiple
if ($LASTEXITCODE -ne 0) { throw 'Multiple-frame transport checks failed.' }
& $PythonPath (Join-Path $PSScriptRoot src\audit_workplane_activation_transport.py) --evidence (Join-Path $EvidenceDirectory multiple-transport) --output (Join-Path $EvidenceDirectory multiple-transport\independent-audit.json)
if ($LASTEXITCODE -ne 0) { throw 'Multiple-frame transport audit failed.' }
Write-Output 'PASS: single activation/WORLD intents, reconnect, lost reply and cancellation; no CAD.'
