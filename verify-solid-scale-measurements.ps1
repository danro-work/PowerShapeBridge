param(
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\solid-scale-measurements'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\solid-scale-measurements')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh own Native scale measurement stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-rotation-plans.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
$taskReader=Join-Path $StageDirectory 'ReadNativeScaleSource.exe'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /reference:System.Core.dll /reference:System.Web.Extensions.dll "/out:$taskReader" (Join-Path $PSScriptRoot 'tools\ReadNativeScaleSource.cs')
if($LASTEXITCODE -ne 0){throw 'Passive scale source qualification reader compile failed.'}
& $taskReader
if($LASTEXITCODE -ne 2){throw 'Reader must refuse absent proof inputs before any CAD access.'}
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --solid-scale-measurement-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Unmarked scale measurement fixture must refuse.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture created work files.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.solid-scale-measurement-fixture') -Value 'Own fake getters only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --solid-scale-measurement-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'targeted.txt')
if($LASTEXITCODE -ne 0){throw 'Native measurement adapter checks failed.'}
[ordered]@{state='passed_native_scale_measurement_fake_getters';cadConnectionAttempted=$false;runtimeDeploymentPerformed=$false;publicScaleRouteQualified=$false;files=@('src/SolidScaleMembershipMeasurements.cs','src/SolidScaleMembershipMeasurementTests.cs','src/SolidScaleMembershipVerifier.cs','tools/ReadNativeScaleSource.cs','build.ps1','src/Bridge.cs')|ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_)).Hash.ToLowerInvariant()}};nativeAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $StageDirectory 'PSBridge.Worker.exe')).Hash.ToLowerInvariant();readerSha256=(Get-FileHash -LiteralPath $taskReader).Hash.ToLowerInvariant()}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'targeted.json') -Encoding utf8
