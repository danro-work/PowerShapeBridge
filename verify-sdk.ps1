param(
    [Parameter(Mandatory=$true)][string]$DotnetPath,
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [Parameter(Mandatory=$true)][string]$InspectorPath,
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'evidence\SDK')
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskProject = Join-Path $taskRoot 'tests\PowerShapeMcp.Tests\PowerShapeMcp.Tests.csproj'
$taskEvidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
[ordered]@{schema='psbridge.sdk-verification.v1';startedUtc=[DateTime]::UtcNow.ToString('o');passed=$false;state='running_or_interrupted';cadConnectionAttempted=$false;runtimeDeploymentPerformed=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskEvidence 'sdk-verification.json') -Encoding utf8
& $DotnetPath restore $taskProject --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
& $DotnetPath build $taskProject --no-restore --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'SDK build failed.' }
& $DotnetPath test $taskProject --no-build --no-restore --configuration Release --logger 'trx;LogFileName=sdk-tests.trx' --results-directory $taskEvidence
if ($LASTEXITCODE -ne 0) { throw 'SDK tests failed.' }
$taskHost = Join-Path $taskRoot 'src\PowerShapeMcp.Host\bin\Release\net10.0\PowerShapeMcp.Host.dll'
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_stdio.py') --dotnet $DotnetPath --host $taskHost --evidence $taskEvidence
if ($LASTEXITCODE -ne 0) { throw 'Independent STDIO parity failed.' }
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_readonly.py') --dotnet $DotnetPath --host $taskHost --evidence $taskEvidence
if ($LASTEXITCODE -ne 0) { throw 'Independent read-only parity failed.' }
$taskFixture = Join-Path $taskRoot ('work\K2-inspector-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskFixture | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'config\general-owned-translations.json') -Destination (Join-Path $taskFixture 'bridge.config.json')
$taskInspectorConfig = Join-Path $taskFixture 'inspector.json'
@{mcpServers=@{'powershape-k2'=@{command=$DotnetPath;args=@($taskHost,'--bridge-root',$taskFixture,'--read-only')}}} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $taskInspectorConfig -Encoding utf8
foreach ($taskMethod in @('tools/list','tools/call')) {
    $taskArguments = @('--cli','--config',$taskInspectorConfig,'--server','powershape-k2','--method',$taskMethod)
    if ($taskMethod -eq 'tools/call') { $taskArguments += @('--tool-name','bridge_status') }
    $taskName = $taskMethod.Replace('/','-')
    $taskOutput = & $InspectorPath @taskArguments 2> (Join-Path $taskEvidence ($taskName+'-inspector-stderr.txt'))
    if ($LASTEXITCODE -ne 0) { throw "Inspector failed: $taskMethod" }
    $taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence ($taskName+'-inspector.json')) -Encoding utf8
    $taskResult = ($taskOutput -join "`n") | ConvertFrom-Json
    if ($taskMethod -eq 'tools/list') {
        if ((@($taskResult.tools.name | Sort-Object) -join ',') -ne 'bridge_status,inspect_active_model,inspect_entity_candidates,list_documents,operation_result,resolve_entity_references') { throw 'Inspector catalog differs.' }
    } elseif ($taskResult.isError -or $taskResult.structuredContent.panelLiveness -ne 'not_verified' -or $taskResult.structuredContent.geometryMutations -ne 'disabled') { throw 'Inspector diagnostic result differs.' }
}
$taskFiles = @('global.json','src\PowerShapeMcp.Host\PowerShapeMcp.Host.csproj','src\PowerShapeMcp.Host\packages.lock.json','tests\PowerShapeMcp.Tests\packages.lock.json','src\test_sdk_stdio.py','verify-sdk.ps1')
$taskFiles += @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src\PowerShapeMcp.Host'),(Join-Path $taskRoot 'tests\PowerShapeMcp.Tests') -File -Recurse | Where-Object { $_.Extension -eq '.cs' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object { [IO.Path]::GetRelativePath($taskRoot,$_.FullName) })
$taskFiles += @('src\mcp_server.py','src\operation_plans.py','src\entity_references.py','src\PowerShapeMcp.Host\bin\Release\net10.0\PowerShapeMcp.Host.dll','src\PowerShapeMcp.Host\bin\Release\net10.0\PowerShapeMcp.Host.deps.json','src\PowerShapeMcp.Host\bin\Release\net10.0\PowerShapeMcp.Host.runtimeconfig.json')
$taskFiles += @('src\document_handles.py','src\entity_sets.py','src\test_sdk_readonly.py','src\generate_observation_fixtures.py','tests\PowerShapeMcp.Tests\fixtures\observations.json')
$taskHashes = @($taskFiles | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot $_)).Hash.ToLowerInvariant()} })
[ordered]@{schema='psbridge.sdk-verification.v1';finishedUtc=[DateTime]::UtcNow.ToString('o');passed=$true;version=$taskResult.structuredContent.adapterVersion;scope='SDK diagnostics, fixed IPC/read-only fake-panel, SDK clients, Python journal/read lease parity, Inspector CLI';cadConnectionAttempted=$false;runtimeDeploymentPerformed=$false;files=$taskHashes} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskEvidence 'sdk-verification.json') -Encoding utf8
Write-Output 'PASS: SDK build/tests, independent STDIO client and Inspector CLI; no CAD or deployment.'
