param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'evidence\P2.31-executor'))
$ErrorActionPreference = 'Stop'
$taskEvidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!$taskEvidence.StartsWith((Join-Path $PSScriptRoot 'evidence')+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence path must stay in project.' }
function Read-SharedSha([string]$Path) {
    # Passive saved-byte observation only. This sharing is never used for copy/open guards.
    $taskStream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $taskHash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($taskHash.ComputeHash($taskStream)).Replace('-','').ToLowerInvariant() }
    finally { $taskHash.Dispose(); $taskStream.Dispose() }
}
$taskRuntime = Join-Path $PSScriptRoot 'releases\P2.6'
$taskPanel = Get-Content (Join-Path $taskRuntime 'reports\panel-status.json') -Raw | ConvertFrom-Json
$taskProcesses = @(Get-Process -Name powershape,PSBridge,PSBridge.Worker -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,SessionId,@{N='startedUtc';E={$_.StartTime.ToUniversalTime().ToString('o')}},Path)
$taskPanels = @($taskProcesses | Where-Object ProcessName -eq 'PSBridge')
if ($taskPanels.Count -ne 1 -or $taskPanels[0].Id -ne $taskPanel.processId -or $taskPanels[0].Path -ne (Join-Path $taskRuntime 'PSBridge.exe') -or ([DateTime]$taskPanels[0].startedUtc).ToUniversalTime() -ne ([DateTime]$taskPanel.processStartedUtc).ToUniversalTime() -or !$taskPanel.acceptingRequests -or !$taskPanel.workerIdle) { throw 'Known single idle panel required.' }
if (@($taskProcesses | Where-Object ProcessName -eq 'powershape').Count -ne 1 -or @($taskProcesses | Where-Object ProcessName -eq 'PSBridge.Worker').Count -ne 0) { throw 'Unexpected live process count.' }
if ([DateTime]::UtcNow - ([DateTime]$taskPanel.timestampUtc).ToUniversalTime() -gt [TimeSpan]::FromSeconds(20)) { throw 'Stale heartbeat.' }
if (@(Get-ChildItem (Join-Path $taskRuntime 'requests') -File | Where-Object { $_.Name -match '\.json$|\.processing$' }).Count -ne 0) { throw 'Pending queue.' }
$taskDeployment = Get-Content (Join-Path $taskEvidence 'deployment-hardened.json') -Raw | ConvertFrom-Json
$taskFiles = @($taskDeployment.files | ForEach-Object {
    $taskActual = Read-SharedSha $_.destination
    if ($taskActual -ne $_.afterSha256) { throw "Installed artifact changed: $($_.name)" }
    @{path=$_.destination;sha256=$taskActual;matchesStage=$true}
})
$taskCode = Get-Content (Join-Path $taskEvidence 'code\code-verification.json') -Raw | ConvertFrom-Json
foreach ($taskFile in $taskCode.files) {
    $taskPath = if ([IO.Path]::IsPathRooted($taskFile.path)) {$taskFile.path} else {Join-Path $PSScriptRoot $taskFile.path}
    if ((Read-SharedSha $taskPath) -ne $taskFile.sha256) { throw "Tested source/staging artifact changed: $taskPath" }
}
$taskComponent = (& (Join-Path $taskRuntime 'PSBridge.Worker.exe') --component-info) | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $taskComponent.version -ne '0.31.0' -or $taskComponent.powerShapeConnectionAttempted -or $taskComponent.configurationSha256 -ne $taskPanel.configurationSha256) { throw 'Component mismatch.' }
$taskProtected = Get-Content (Join-Path $taskEvidence 'protected-after-translation.json') -Raw | ConvertFrom-Json
foreach ($taskFile in $taskProtected.files) { if ((Read-SharedSha $taskFile.path) -ne $taskFile.sha256) { throw "Protected saved file changed: $($taskFile.path)" } }
$taskSources = @(Get-ChildItem (Join-Path $PSScriptRoot 'src') -File | Where-Object Extension -in @('.cs','.py') | ForEach-Object { @{path=$_.FullName;sha256=(Read-SharedSha $_.FullName)} })
$taskResult = @{timestampUtc=[DateTime]::UtcNow.ToString('o');panel=$taskPanel;processes=$taskProcesses;pendingQueueEmpty=$true;installedFiles=$taskFiles;sourceFiles=$taskSources;testedSourcesAndStageUnchanged=$true;component=$taskComponent;protectedFiles=$taskProtected.files;protectedSavedHashesVerified=$true;geometryMutationAttempted=$false;scope='Process discovery, local heartbeat and shared saved-byte observations only; no CAD connection, selection, capture, save or geometry command'}
$taskResult | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $taskEvidence 'final-runtime.json') -Encoding utf8
Write-Output "PASS: one idle panel, empty queue, installed/tested source hashes, component 0.31.0 and three protected saved-file hashes verified. No CAD connection."



