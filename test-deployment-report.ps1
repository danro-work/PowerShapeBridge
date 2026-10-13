param([string]$EvidencePath=(Join-Path $PSScriptRoot 'evidence\K6-directory-fix\deployment-report-test.json'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'deployment-report.ps1')
$taskFolder=Join-Path $PSScriptRoot ('work\deployment-report-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskFolder | Out-Null
$taskPath=Join-Path $taskFolder 'status.json'
$checks=0
if ($null -ne (Read-BridgeDeploymentReport $taskPath)) { throw 'Missing report must be unavailable.' };$checks++
[IO.File]::WriteAllText($taskPath,'{"version":"fixture","processId":17}')
$before=(Get-FileHash -LiteralPath $taskPath).Hash
$writer=[IO.File]::Open($taskPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::ReadWrite)
try { if ((Read-BridgeDeploymentReport $taskPath).processId -ne 17) { throw 'Shared writer report not readable.' };$checks++ } finally { $writer.Dispose() }
$exclusive=[IO.File]::Open($taskPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try { if ($null -ne (Read-BridgeDeploymentReport $taskPath)) { throw 'Exclusive writer must be unavailable.' };$checks++ } finally { $exclusive.Dispose() }
if ((Read-BridgeDeploymentReport $taskPath).version -ne 'fixture') { throw 'Report unavailable after writer released.' };$checks++
if ((Get-FileHash -LiteralPath $taskPath).Hash -ne $before) { throw 'Reader changed report.' };$checks++
[IO.File]::WriteAllText($taskPath,'{broken')
$rejected=$false
try { Read-BridgeDeploymentReport $taskPath | Out-Null } catch { $rejected=$true }
if (!$rejected) { throw 'Malformed report must fail explicitly.' };$checks++
New-Item -ItemType Directory -Force -Path (Split-Path $EvidencePath -Parent) | Out-Null
@{state='passed';checks=$checks;fixture=$taskFolder;cadConnectionAttempted=$false;runtimeDeploymentPerformed=$false;retryScope='report observation only'} | ConvertTo-Json | Set-Content -LiteralPath $EvidencePath -Encoding utf8
Write-Output "PASS deployment report reader: $checks checks; no CAD or restart."
