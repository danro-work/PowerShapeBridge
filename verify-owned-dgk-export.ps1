param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\K9-owned-dgk-export'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K9-owned-dgk-export')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh isolated DGK executor stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-translations.json')
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-dgk-export-test 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 3){throw 'DGK executor must refuse without marker.'}
if(Test-Path -LiteralPath (Join-Path $StageDirectory 'work')){throw 'Marker refusal created fixtures.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.owned-dgk-export-fixture') -Value 'Own fake callbacks only; no CAD; never deploy.'
& (Join-Path $StageDirectory 'PSBridge.Worker.exe') --owned-dgk-export-test | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'native-tests.txt')
if($LASTEXITCODE -ne 0){throw 'DGK executor matrix failed; preserve stage.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\audit_owned_dgk_export.py') --stage $StageDirectory --output (Join-Path $EvidenceDirectory 'native-audit.json')
if($LASTEXITCODE -ne 0){throw 'Independent DGK executor audit failed.'}
