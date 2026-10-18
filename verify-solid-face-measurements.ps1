param(
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\solid-face-measurements'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\solid-face-measurements')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh own solid-face fixture stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-rotation-plans.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --solid-face-measurement-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Unmarked solid-face fixture must refuse.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture created work files.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.solid-face-measurement-fixture') -Value 'Own synthetic getter fixture; no CAD connection. Never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --solid-face-measurement-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'focused-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Pinned solid-face provider checks failed; preserve artifacts.'}
[ordered]@{state='passed';checks=21;cadConnectionAttempted=$false;runtimeDeploymentPerformed=$false;sourceFiles=@('src/SolidFaceMeasurements.cs','src/SolidFaceMeasurementTests.cs','src/DocumentBindings.cs','src/Bridge.cs','build.ps1') | ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_)).Hash.ToLowerInvariant()}}} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'focused-result.json') -Encoding utf8
