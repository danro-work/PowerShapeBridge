param([string]$StagingDirectory = (Join-Path $PSScriptRoot 'work\P2.22'))
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$activeRelease = Join-Path $projectRoot 'releases\P2.6'
$panelPath = Join-Path $activeRelease 'PSBridge.exe'
$pending = @(Get-ChildItem -LiteralPath (Join-Path $activeRelease 'requests') -File | Where-Object { $_.Name.EndsWith('.json') -or $_.Name.EndsWith('.processing') })
if ($pending.Count -gt 0 -or @(Get-Process -Name 'PSBridge.Worker' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'A Bridge request is pending or running; wait for completion before deployment.'
}
foreach ($file in @('PSBridge.exe','PSBridge.Worker.exe','PSBridge.Launcher.exe')) {
    if (!(Test-Path -LiteralPath (Join-Path $StagingDirectory $file))) { throw "Missing staged file: $file" }
}
$rollback = Join-Path $projectRoot ('work\deployment-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $rollback | Out-Null
foreach ($file in @('PSBridge.exe','PSBridge.Worker.exe','bridge.config.json','mcp_server.py')) {
    Copy-Item -LiteralPath (Join-Path $activeRelease $file) -Destination (Join-Path $rollback $file)
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'PSBridge.exe') -Destination (Join-Path $rollback 'root-PSBridge.exe')
# Only close the known Bridge panel, never PowerShape or its document windows.
$oldPanels = @(Get-Process -Name 'PSBridge' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $panelPath })
foreach ($panel in $oldPanels) {
    if ($panel.CloseMainWindow()) {
        if (!$panel.WaitForExit(10000)) { throw 'Panel did not close cleanly; deployment stopped.' }
    } else {
        throw 'This session cannot close the existing panel. Close Bridge manually after all requests finish, then deploy again.'
    }
}
foreach ($file in @('PSBridge.exe','PSBridge.Worker.exe')) {
    Copy-Item -LiteralPath (Join-Path $StagingDirectory $file) -Destination (Join-Path $activeRelease $file) -Force
}
foreach ($file in @('mcp_server.py','core_config.py','entity_sets.py','surface_job.py','slice_job.py','group_job.py','entity_references.py','document_handles.py')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot ('src\'+$file)) -Destination (Join-Path $activeRelease $file) -Force
}
# Preserve the qualified configuration, receipts and current MCP registration.
$settings = Get-Content -LiteralPath (Join-Path $activeRelease 'bridge.config.json') -Raw | ConvertFrom-Json
$settings | Add-Member -NotePropertyName operationTimeoutSeconds -NotePropertyValue 80 -Force
$settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $activeRelease 'bridge.config.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $StagingDirectory 'PSBridge.Launcher.exe') -Destination (Join-Path $projectRoot 'PSBridge.exe') -Force
@('@echo off','call "%~dp0start.cmd"') | Set-Content -LiteralPath (Join-Path $projectRoot 'Start-P2.6.cmd') -Encoding ascii
# This is the interactive project panel the user needs to control.
Start-Process -FilePath (Join-Path $projectRoot 'PSBridge.exe')
$manifest = [ordered]@{ deployedUtc=[DateTime]::UtcNow.ToString('o'); version='0.22.0'; releaseStage='P2.22'; activeRelease=$activeRelease; canonicalStart=(Join-Path $projectRoot 'PSBridge.exe'); rollbackDirectory=$rollback; files=@() }
foreach ($path in @((Join-Path $projectRoot 'PSBridge.exe'),(Join-Path $activeRelease 'PSBridge.exe'),(Join-Path $activeRelease 'PSBridge.Worker.exe'),(Join-Path $activeRelease 'mcp_server.py'),(Join-Path $activeRelease 'bridge.config.json'))) {
    $manifest.files += @{path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $activeRelease 'reports\deployment-current.json') -Encoding utf8
$manifest | ConvertTo-Json -Depth 8

