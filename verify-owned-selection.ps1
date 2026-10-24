param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'evidence\owned-selection-ci'))
$ErrorActionPreference = 'Stop'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (!$outputRoot.StartsWith($PSScriptRoot+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Selection verification output must be in this project.' }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$stage = Join-Path $PSScriptRoot ('work\owned-selection-ci-'+[guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $stage -ConfigurationPath (Join-Path $PSScriptRoot 'config\general-owned-selection.json')
if ($LASTEXITCODE -ne 0) { throw 'Selection fixture build failed.' }
New-Item -ItemType File -Path (Join-Path $stage '.owned-selection-fixture') | Out-Null
$result = & (Join-Path $stage 'PSBridge.Worker.exe') --owned-selection-test 2>&1
$exitCode = $LASTEXITCODE
$result | Set-Content -LiteralPath (Join-Path $outputRoot 'native-selection-tests.txt') -Encoding utf8
if ($exitCode -ne 0) { throw "Selection fixture failed: $result" }
Write-Output $result
# This marker path reaches a fake retained context; it never attaches COM or starts a panel.
