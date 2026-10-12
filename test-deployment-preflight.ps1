param(
    [string]$StagingDirectory = (Join-Path $PSScriptRoot 'work\P2.31-executor'),
    [string]$EvidencePath = (Join-Path $PSScriptRoot 'evidence\P2.31-executor\deployment-preflight.json'),
    [switch]$OfflineFixtures
)
$ErrorActionPreference = 'Stop'
$taskStage = [IO.Path]::GetFullPath($StagingDirectory)
$taskCopy = Join-Path $PSScriptRoot ('work\deployment-preflight-'+[guid]::NewGuid().ToString('N'))
$taskEvidence = [IO.Path]::GetFullPath($EvidencePath)
$taskRuntimeConfig = Join-Path $PSScriptRoot 'releases\P2.6\bridge.config.json'
if ($OfflineFixtures) {
    if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'releases\P2.6\.baseline-fixture'))) { throw 'Offline process fixtures require the isolated baseline runtime.' }
    # Scoped to this test and its plan-only child. Production deployment is unchanged.
    function Get-Process {
        param([string]$Name, [int]$Id, $ErrorAction)
        if ($Name -in @('PSBridge','PSBridge.Worker')) { return }
        if ($Id -eq $PID) { return Microsoft.PowerShell.Management\Get-Process -Id $Id }
        throw 'Unexpected process query in offline preflight.'
    }
}
$taskRuntimeHash = (Get-FileHash -LiteralPath $taskRuntimeConfig).Hash
New-Item -ItemType Directory -Path $taskCopy | Out-Null
$taskManifest = Get-Content -LiteralPath (Join-Path $taskStage 'build-manifest.json') -Raw | ConvertFrom-Json
foreach ($taskFile in $taskManifest.files) { Copy-Item -LiteralPath (Join-Path $taskStage $taskFile.name) -Destination (Join-Path $taskCopy $taskFile.name) }
Copy-Item -LiteralPath (Join-Path $taskStage 'build-manifest.json') -Destination (Join-Path $taskCopy 'build-manifest.json')
# Only plan mode is invoked, including failure cases; it never closes a panel or applies files.
& (Join-Path $PSScriptRoot 'deploy-general.ps1') -StagingDirectory $taskCopy -PlanPath (Join-Path $taskCopy 'plan.json')
$taskScript = Join-Path $taskCopy 'mcp_server.py'
Add-Content -LiteralPath $taskScript -Value '# preflight corruption fixture'
$taskTamperRejected = $false
try { & (Join-Path $PSScriptRoot 'deploy-general.ps1') -StagingDirectory $taskCopy -PlanPath (Join-Path $taskCopy 'invalid-plan.json') }
catch { if ($_.Exception.Message -notmatch 'Staged artifact differs') { throw }; $taskTamperRejected = $true }
if (!$taskTamperRejected) { throw 'Corrupt staged artifact was not rejected.' }
Copy-Item -LiteralPath (Join-Path $taskStage 'mcp_server.py') -Destination $taskScript -Force
$taskManifest.version = '0.99.0'
$taskManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskCopy 'build-manifest.json') -Encoding utf8
$taskVersionRejected = $false
try { & (Join-Path $PSScriptRoot 'deploy-general.ps1') -StagingDirectory $taskCopy -PlanPath (Join-Path $taskCopy 'wrong-version-plan.json') }
catch { if ($_.Exception.Message -notmatch 'Compiled worker does not match') { throw }; $taskVersionRejected = $true }
if (!$taskVersionRejected -or (Get-FileHash -LiteralPath $taskRuntimeConfig).Hash -ne $taskRuntimeHash) { throw 'Deployment preflight verification failed.' }
@{state='passed';mode='plan_only';tamperedArtifactRejected=$taskTamperRejected;compiledVersionMismatchRejected=$taskVersionRejected;runtimeConfigurationUnchanged=$true;cadConnectionAttempted=$false;fixture=$taskCopy} | ConvertTo-Json | Set-Content -LiteralPath $taskEvidence -Encoding utf8
Write-Output 'PASS: valid plan, corrupt artifacts and version mismatch; runtime configuration unchanged.'


