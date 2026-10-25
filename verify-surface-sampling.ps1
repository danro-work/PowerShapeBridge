param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'evidence\surface-sampling-ci'))
$ErrorActionPreference = 'Stop'
$taskEvidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!$taskEvidence.StartsWith($PSScriptRoot+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Surface verification output must remain in this project.' }
$taskStage = Join-Path $PSScriptRoot ('work\surface-sampling-ci-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-surface-sampling.json')
if ($LASTEXITCODE -ne 0) { throw 'Surface sampling fixture build failed.' }
New-Item -ItemType File -Path (Join-Path $taskStage '.surface-sampling-fixture') | Out-Null
$taskOutput = & (Join-Path $taskStage 'PSBridge.Worker.exe') --surface-sampling-test 2>&1
$taskExit = $LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-tests.txt') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $taskStage 'reports\surface-sampling-tests.json') -Destination (Join-Path $taskEvidence 'native-tests.json')
if ($taskExit -ne 0) { throw "Surface sampling fixture failed: $taskOutput" }
Write-Output $taskOutput
# Marker reaches only fake retained contexts. No COM, panel or installed runtime.
