param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\K9-owned-dgk-targets'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K9-owned-dgk-targets')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh owned DGK target stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-translations.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-dgk-target-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'DGK fixture CLI must refuse without marker.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Marker refusal created fixtures.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-dgk-fixture') -Value 'Own synthetic files only; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-dgk-target-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Owned DGK target matrix failed; preserve stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_owned_dgk_targets.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent owned DGK target audit failed.'}
