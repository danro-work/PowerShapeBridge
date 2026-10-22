param(
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\owned-open-adoption'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\owned-open-adoption')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh own Native adoption stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-open-adoption.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --component-info | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'component-info.json')
if($LASTEXITCODE -ne 0){throw 'Public adoption configuration or dependencies invalid.'}
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-open-adoption-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'Unmarked adoption fixture must refuse without CAD access.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Unmarked adoption created work files.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-open-adoption-fixture') -Value 'Own fake observations only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-open-adoption-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'targeted.txt')
if($LASTEXITCODE -ne 0){throw 'Native interrupted OPEN adoption guards failed.'}
[ordered]@{state='passed_owned_open_adoption_fake_guards';cadConnectionAttempted=$false;runtimeDeploymentPerformed=$false;nativeAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $StageDirectory 'PSBridge.Worker.exe')).Hash.ToLowerInvariant()} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'targeted.json') -Encoding utf8
