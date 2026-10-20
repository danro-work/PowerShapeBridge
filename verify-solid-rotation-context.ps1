param(
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\solid-rotation-context'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\solid-rotation-context')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh own internal rotation adapter stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-rotation-plans.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --solid-rotation-context-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Unmarked internal adapter fixture must refuse.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked adapter fixture created work files.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.solid-rotation-context-fixture') -Value 'Own fake callbacks only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --solid-rotation-context-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'focused-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Internal adapter checks failed; retain original evidence.'}
[ordered]@{state='passed_internal_fake_callbacks_only';checks=19;cadConnectionAttempted=$false;liveRotateItemQualified=$false;runtimeDeploymentPerformed=$false;sourceFiles=@('src/SolidRotationContext.cs','src/SolidRotationContextTests.cs','src/DocumentBindings.cs','src/Bridge.cs','build.ps1') | ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_)).Hash.ToLowerInvariant()}}} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'focused-result.json') -Encoding utf8
