param(
    [string]$StagingDirectory = (Join-Path $PSScriptRoot 'work\P2.31-executor'),
    [string]$PlanPath = (Join-Path $PSScriptRoot 'work\general-deployment-plan.json'),
    [string]$EvidencePath = (Join-Path $PSScriptRoot 'evidence\P2.31-executor\deployment.json'),
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$stagedRoot = [IO.Path]::GetFullPath($StagingDirectory)
$runtimeRoot = Join-Path $projectRoot 'releases\P2.6'
$panelPath = Join-Path $runtimeRoot 'PSBridge.exe'
. (Join-Path $projectRoot 'panel-window.ps1')
$fileNames = @('PSBridge.exe','PSBridge.Worker.exe','bridge.config.json','mcp_server.py','core_config.py','entity_sets.py','operation_plans.py','working_copies.py','document_bindings.py','working_copy_open.py','owned_operation_plans.py','owned_translation.py','entity_references.py','document_handles.py','surface_job.py','slice_job.py','group_job.py')
function Assert-Idle {
    if (@(Get-ChildItem -LiteralPath (Join-Path $runtimeRoot 'requests') -File | Where-Object { $_.Name -match '\.json$|\.processing$' }).Count -gt 0 -or @(Get-Process -Name PSBridge.Worker -ErrorAction SilentlyContinue).Count -gt 0) { throw 'A Bridge request is pending or running.' }
}
function File-HashOrNull([string]$Path) {
    if (Test-Path -LiteralPath $Path) { return (Get-FileHash -LiteralPath $Path).Hash.ToLowerInvariant() }
    return $null
}
function Read-Panels {
    $session = (Get-Process -Id $PID).SessionId
    $panels = @(Get-Process -Name PSBridge -ErrorAction SilentlyContinue)
    foreach ($panel in $panels) {
        if ($panel.Path -ne $panelPath -or $panel.SessionId -ne $session) { throw 'Unknown Bridge process or different Windows session; no window will be closed.' }
    }
    if ($panels.Count -gt 1) { throw 'More than one Bridge panel.' }
    return $panels
}
if (!$stagedRoot.StartsWith((Join-Path $projectRoot 'work')+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Deployment requires project staging.' }
if (Test-Path -LiteralPath (Join-Path $runtimeRoot 'maintenance.json')) { throw 'Existing maintenance marker; inspect previous deployment before continuing.' }
$buildPath = Join-Path $stagedRoot 'build-manifest.json'
$build = Get-Content -LiteralPath $buildPath -Raw | ConvertFrom-Json
if ($build.configurationProfile -ne 'general-v1') { throw 'General core build required.' }
foreach ($file in @($fileNames)+@('PSBridge.Launcher.exe')) {
    $entries = @($build.files | Where-Object { $_.name -eq $file })
    if ($entries.Count -ne 1 -or (File-HashOrNull (Join-Path $stagedRoot $file)) -ne $entries[0].sha256) { throw "Staged artifact differs from build manifest: $file" }
}
$componentRaw = & (Join-Path $stagedRoot 'PSBridge.Worker.exe') --component-info
if ($LASTEXITCODE -ne 0) { throw 'Staged worker rejected configuration.' }
$component = $componentRaw | ConvertFrom-Json
if ($component.state -ne 'component_configuration_validated' -or $component.version -ne $build.version -or $component.configurationProfile -ne 'general-v1') { throw 'Compiled worker does not match staged version/profile.' }
$configuration = Get-Content -LiteralPath (Join-Path $stagedRoot 'bridge.config.json') -Raw | ConvertFrom-Json
if ($configuration.coreProfile -ne 'general-v1' -or $configuration.captureViewport -or $configuration.mcpMutationsPanel -or $configuration.recipeOperationsPanel -or $configuration.expectedProject -or $configuration.sourceStep) { throw 'General profile must exclude fixtures and captures.' }
Assert-Idle
$panels = @(Read-Panels)
$files = @()
foreach ($file in $fileNames) { $files += @{name=$file;destination=(Join-Path $runtimeRoot $file);beforeSha256=(File-HashOrNull (Join-Path $runtimeRoot $file));afterSha256=(File-HashOrNull (Join-Path $stagedRoot $file))} }
$files += @{name='PSBridge.Launcher.exe';destination=(Join-Path $projectRoot 'PSBridge.exe');beforeSha256=(File-HashOrNull (Join-Path $projectRoot 'PSBridge.exe'));afterSha256=(File-HashOrNull (Join-Path $stagedRoot 'PSBridge.Launcher.exe'))}
$panelIdentity = @($panels | ForEach-Object { @{id=$_.Id;startedUtc=$_.StartTime.ToUniversalTime().ToString('o')} })
if (!$Apply) {
    $plan = [ordered]@{createdUtc=[DateTime]::UtcNow.ToString('o');staging=$stagedRoot;runtime=$runtimeRoot;version=$build.version;buildManifestSha256=(File-HashOrNull $buildPath);panels=$panelIdentity;files=$files;cadMutationPlanned=$false}
    $plan | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $PlanPath -Encoding utf8
    Write-Output "Prepared deployment plan: $PlanPath; version $($build.version); no runtime files changed."
    exit 0
}
$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
if ($plan.staging -ne $stagedRoot -or $plan.runtime -ne $runtimeRoot -or $plan.version -ne $build.version -or $plan.buildManifestSha256 -ne (File-HashOrNull $buildPath)) { throw 'Deployment plan no longer matches staging.' }
if (@($plan.files).Count -ne $files.Count -or @($plan.panels).Count -ne $panelIdentity.Count) { throw 'Runtime/panel changed since planning.' }
for ($index=0; $index -lt $files.Count; $index++) {
    $expected = $plan.files[$index]; $actual = $files[$index]
    if ($expected.name -ne $actual.name -or $expected.destination -ne $actual.destination -or $expected.beforeSha256 -ne $actual.beforeSha256 -or $expected.afterSha256 -ne $actual.afterSha256) { throw "Runtime file changed since planning: $($actual.name)" }
}
for ($index=0; $index -lt $panelIdentity.Count; $index++) {
    if ($plan.panels[$index].id -ne $panelIdentity[$index].id -or ([datetime]$plan.panels[$index].startedUtc).ToUniversalTime() -ne ([datetime]$panelIdentity[$index].startedUtc).ToUniversalTime()) { throw 'Panel instance changed since planning.' }
}
$journalPath = [IO.Path]::GetFullPath($EvidencePath)
if (!$journalPath.StartsWith((Join-Path $projectRoot 'evidence')+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Deployment journal must be under project evidence.' }
New-Item -ItemType Directory -Force -Path (Split-Path $journalPath -Parent) | Out-Null
$lock = [IO.File]::Open((Join-Path $runtimeRoot 'deployment.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$backup = Join-Path $projectRoot ('work\general-deployment-backup-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$marker = Join-Path $runtimeRoot 'maintenance.json'
$journal = [ordered]@{state='preparing';startedUtc=[DateTime]::UtcNow.ToString('o');version=$build.version;runtime=$runtimeRoot;rollbackDirectory=$backup;cadMutationAttempted=$false}
try {
    Assert-Idle
    New-Item -ItemType Directory -Path $backup | Out-Null
    foreach ($file in $files) { if ($file.beforeSha256) { Copy-Item -LiteralPath $file.destination -Destination (Join-Path $backup $file.name) } }
    Copy-Item -LiteralPath $PlanPath -Destination (Join-Path $backup 'deployment-plan.json')
    @{startedUtc=[DateTime]::UtcNow.ToString('o');purpose='general-core deployment';rollbackDirectory=$backup} | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding utf8
    foreach ($panel in $panels) {
        $journal.closedPanelWindow = Close-BridgePanelGracefully $panel
    }
    Assert-Idle
    $journal.state='copying'
    foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $stagedRoot $file.name) -Destination $file.destination -Force }
    foreach ($file in $files) { if ((File-HashOrNull $file.destination) -ne $file.afterSha256) { throw "Installed hash mismatch: $($file.name)" } }
    $journal.state='files_verified'
    $journal.files=$files
    $journal | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runtimeRoot 'reports\deployment-current.json') -Encoding utf8
    Remove-Item -LiteralPath $marker
    # Hidden start is required for a background deployment helper; no mouse/UI automation.
    $newPanel = Start-Process -FilePath $panelPath -WorkingDirectory $runtimeRoot -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $ready = $false
    do {
        if ($newPanel.HasExited) { throw 'New panel exited during startup.' }
        $reportPath = Join-Path $runtimeRoot 'reports\panel-status.json'
        if (Test-Path -LiteralPath $reportPath) {
            $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
            $ready = $report.processId -eq $newPanel.Id -and $report.version -eq $build.version -and $report.configurationProfile -eq 'general-v1' -and $report.configurationSha256 -eq (File-HashOrNull (Join-Path $runtimeRoot 'bridge.config.json'))
        }
        if (!$ready) { Start-Sleep -Milliseconds 200 }
    } until ($ready -or [DateTime]::UtcNow -ge $deadline)
    if (!$ready) { throw 'Panel readiness unverified; inspect deployment journal before recovery.' }
    if (@(Read-Panels).Count -ne 1) { throw 'Expected one panel after deployment.' }
    $journal.state='deployed_panel_verified'
    $journal.panel=$report
    $journal.finishedUtc=[DateTime]::UtcNow.ToString('o')
    $journal | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runtimeRoot 'reports\deployment-current.json') -Encoding utf8
    Write-Output "Deployed general profile $($build.version); one panel verified; rollback: $backup. CAD was not contacted by deployment."
} catch {
    $journal.state='deployment_incomplete'
    $journal.error=$_.Exception.Message
    $journal.recovery='No automatic restart or rollback. Inspect files, queue and known panel before recovery.'
    throw
} finally {
    New-Item -ItemType Directory -Force -Path (Split-Path $journalPath -Parent) | Out-Null
    $journal | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $journalPath -Encoding utf8
    $lock.Dispose()
}


