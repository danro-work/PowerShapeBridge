param(
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\owned-solid-scale'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\owned-solid-scale')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh own Native scale stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-solid-scale.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-solid-scale-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Unmarked scale fixture must refuse without CAD access.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked fixture created work files.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-solid-scale-fixture') -Value 'Own fake scale callbacks; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-solid-scale-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'targeted.txt')
if($LASTEXITCODE -ne 0){throw 'Native scale once/phase/verifier guards failed.'}
[ordered]@{state='passed_native_scale_fake_phase_callbacks';cadConnectionAttempted=$false;runtimeDeploymentPerformed=$false;liveScaleRouteQualified=$false;nativeAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $StageDirectory 'PSBridge.Worker.exe')).Hash.ToLowerInvariant()}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'targeted.json') -Encoding utf8
