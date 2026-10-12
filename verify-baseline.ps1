param(
    [Parameter(Mandatory=$true)][string]$PythonPath
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskRuntime = Join-Path $taskRoot 'releases\P2.6'
# Run in a fresh checkout. Never manufacture a fixture over an installed runtime.
if (Test-Path -LiteralPath (Join-Path $taskRoot 'releases')) { throw 'Baseline requires a clean checkout without releases; existing runtime is protected.' }
if (!(Test-Path -LiteralPath $PythonPath -PathType Leaf)) { throw 'Provide an actual Python executable, not a WindowsApps alias.' }
$taskStage = Join-Path $taskRoot 'work\K1-baseline'
$taskEvidence = Join-Path $taskRoot 'evidence\K1-baseline'
New-Item -ItemType Directory -Path (Join-Path $taskRuntime 'requests'),$taskEvidence | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'config\general-owned-translations.json') -Destination (Join-Path $taskRuntime 'bridge.config.json')
Set-Content -LiteralPath (Join-Path $taskRuntime '.baseline-fixture') -Value 'Offline baseline: no panel or CAD. Never deploy this fixture.'
$taskStarted = [DateTime]::UtcNow.ToString('o')
$taskPassed = $false
try {
    & (Join-Path $taskRoot 'build.ps1') -OutputDirectory $taskStage -ConfigurationPath (Join-Path $taskRoot 'config\general-owned-translations.json')
    & (Join-Path $taskRoot 'verify-entity-sets.ps1') -StagingDirectory $taskStage -PythonPath $PythonPath -EvidenceDirectory (Join-Path $taskEvidence 'code')
    & (Join-Path $taskRoot 'test-copy-paths.ps1') -StagingDirectory $taskStage -EvidencePath (Join-Path $taskEvidence 'junction.json') -OfflineFixtures
    & (Join-Path $taskRoot 'test-deployment-preflight.ps1') -StagingDirectory $taskStage -EvidencePath (Join-Path $taskEvidence 'deployment.json') -OfflineFixtures
    $taskPassed = $true
} finally {
    [ordered]@{schema='psbridge.baseline.v1';startedUtc=$taskStarted;finishedUtc=[DateTime]::UtcNow.ToString('o');passed=$taskPassed;configuration='config/general-owned-translations.json';pythonVersion=(& $PythonPath --version);cadConnectionAttempted=$false;runtimeDeploymentPerformed=$false;processInspection='synthetic empty panel/worker fixture in plan-only deployment test';stage=$taskStage} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskEvidence 'baseline.json') -Encoding utf8
}
Write-Output 'PASS: isolated baseline, native fixtures, Python rules, MCP protocol, junction and plan-only deployment checks.'
