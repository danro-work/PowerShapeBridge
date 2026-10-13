param(
    [Parameter(Mandatory=$true)][string]$DotnetPath,
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [Parameter(Mandatory=$true)][string]$InspectorPath,
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'evidence\SDK'),
    [string]$NativeStageDirectory = (Join-Path $PSScriptRoot 'work\SDK-native-fixtures')
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $NativeStageDirectory) { throw 'Native compatibility requires a fresh stage, not old fixture artifacts.' }
$taskRoot = $PSScriptRoot
$taskProject = Join-Path $taskRoot 'tests\PowerShapeMcp.Tests\PowerShapeMcp.Tests.csproj'
$taskEvidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
[ordered]@{schema='psbridge.sdk-verification.v1';startedUtc=[DateTime]::UtcNow.ToString('o');passed=$false;state='running_or_interrupted';cadConnectionAttempted=$false;runtimeDeploymentPerformed=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskEvidence 'sdk-verification.json') -Encoding utf8
& $DotnetPath restore $taskProject --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
& $DotnetPath build $taskProject --no-restore --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'SDK build failed.' }
$taskPreviousTransportEvidence = $env:PSBRIDGE_TEST_EVIDENCE
$taskTransportEvidence = Join-Path $taskEvidence 'transport-faults'
if (Test-Path -LiteralPath $taskTransportEvidence) { throw 'Transport fault evidence requires a fresh directory.' }
try {
    $env:PSBRIDGE_TEST_EVIDENCE = $taskTransportEvidence
    & $DotnetPath test $taskProject --no-build --no-restore --configuration Release --logger 'trx;LogFileName=sdk-tests.trx' --results-directory $taskEvidence
    if ($LASTEXITCODE -ne 0) { throw 'SDK tests failed.' }
} finally { $env:PSBRIDGE_TEST_EVIDENCE = $taskPreviousTransportEvidence }
& $PythonPath (Join-Path $taskRoot 'src\audit_sdk_transport_faults.py') --evidence $taskTransportEvidence --output (Join-Path $taskEvidence 'transport-fault-audit.json') --exercise-rejections
if ($LASTEXITCODE -ne 0) { throw 'SDK transport fault evidence audit failed.' }
$taskHost = Join-Path $taskRoot 'src\PowerShapeMcp.Host\bin\Release\net10.0\PowerShapeMcp.Host.dll'
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_stdio.py') --dotnet $DotnetPath --host $taskHost --evidence $taskEvidence
if ($LASTEXITCODE -ne 0) { throw 'Independent STDIO parity failed.' }
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_readonly.py') --dotnet $DotnetPath --host $taskHost --evidence $taskEvidence
if ($LASTEXITCODE -ne 0) { throw 'Independent read-only parity failed.' }
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_recovery_stdio.py') --dotnet $DotnetPath --host $taskHost --evidence $taskEvidence
if ($LASTEXITCODE -ne 0) { throw 'Independent recovery inspection failed.' }
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_prepared.py') --dotnet $DotnetPath --host $taskHost --evidence $taskEvidence
if ($LASTEXITCODE -ne 0) { throw 'Independent prepared parity failed.' }
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_execution.py') --dotnet $DotnetPath --host $taskHost --evidence $taskEvidence
if ($LASTEXITCODE -ne 0) { throw 'Independent execution facade parity failed.' }
& $PythonPath -m unittest discover -s (Join-Path $taskRoot 'src') -p 'test_save_target_fixture_oracle.py'
if ($LASTEXITCODE -ne 0) { throw 'Type-sensitive target oracle regression failed.' }
& (Join-Path $taskRoot 'build.ps1') -OutputDirectory $NativeStageDirectory -ConfigurationPath (Join-Path $taskRoot 'config\general-owned-translations.json')
& (Join-Path $NativeStageDirectory 'PSBridge.Worker.exe') --self-test | Tee-Object -FilePath (Join-Path $taskEvidence 'native-fixture-tests.txt')
if ($LASTEXITCODE -ne 0) { throw 'Native synthetic fixture generation failed.' }
& $PythonPath (Join-Path $taskRoot 'tools\audit_native_save_fixture.py') --stage $NativeStageDirectory --output (Join-Path $taskEvidence 'save-artifact-audit.json')
if ($LASTEXITCODE -ne 0) { throw 'Independent native SaveAs artifact audit failed.' }
& $PythonPath (Join-Path $taskRoot 'src\native_fixture_oracle.py') --stage $NativeStageDirectory --output (Join-Path $taskEvidence 'native-oracle.json')
if ($LASTEXITCODE -ne 0) { throw 'Native oracle generation failed.' }
$taskNativeProject=Join-Path $taskRoot 'tools\NativeFixtureCheck\NativeFixtureCheck.csproj'
& $DotnetPath restore $taskNativeProject --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Native checker locked restore failed.' }
& $DotnetPath run --no-restore --configuration Release --project $taskNativeProject -- (Join-Path $taskEvidence 'native-oracle.json') (Join-Path $taskEvidence 'native-compatibility.json')
if ($LASTEXITCODE -ne 0) { throw 'Native/Python/SDK artifact compatibility differs.' }
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_saved_result_stdio.py') --dotnet $DotnetPath --host $taskHost --stage $NativeStageDirectory --evidence $taskEvidence
if ($LASTEXITCODE -ne 0) { throw 'Independent saved-result SDK STDIO failed.' }
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_saved_result_stdio.py') --dotnet $DotnetPath --host $taskHost --stage $NativeStageDirectory --evidence $taskEvidence --reopen
if ($LASTEXITCODE -ne 0) { throw 'Independent saved-reopen SDK/native filesystem STDIO failed.' }
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_reopen_verification_stdio.py') --dotnet $DotnetPath --host $taskHost --stage $NativeStageDirectory --evidence $taskEvidence
if ($LASTEXITCODE -ne 0) { throw 'Independent saved-reopen verification/new binding STDIO failed.' }
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_saved_result_stdio.py') --dotnet $DotnetPath --host $taskHost --stage $NativeStageDirectory --evidence $taskEvidence --recovery-resolution
if ($LASTEXITCODE -ne 0) { throw 'Independent SaveAs resolution/history SDK STDIO failed.' }
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_reopen_verification_stdio.py') --dotnet $DotnetPath --host $taskHost --stage $NativeStageDirectory --evidence $taskEvidence --recovery-resolution
if ($LASTEXITCODE -ne 0) { throw 'Independent saved-reopen resolution/history SDK STDIO failed.' }
& (Join-Path $taskRoot 'verify-recovery-faults.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-faults') -EvidenceDirectory (Join-Path $taskEvidence 'native-core-faults')
& (Join-Path $taskRoot 'verify-activation-faults.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-activation') -EvidenceDirectory (Join-Path $taskEvidence 'activation-faults')
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
$taskFiles += @('src\test_sdk_prepared.py','src\native_fixture_oracle.py','src\generate_tool_schemas.py','src\PowerShapeMcp.Host\Schemas\prepared-tools.json','tools\NativeFixtureCheck\Program.cs','tools\NativeFixtureCheck\NativeFixtureCheck.csproj','tools\NativeFixtureCheck\packages.lock.json')
$taskFiles += @('src\test_sdk_execution.py','src\PowerShapeMcp.Host\Schemas\execution-tools.json','src\working_copy_open.py','src\owned_translation.py')
$taskFiles += @('src\save_target_fixture_oracle.py','src\test_save_target_fixture_oracle.py','src\OwnedDirectoryLease.cs','src\OwnedSaveTargets.cs','src\OwnedSaveTargetTests.cs')
$taskFiles += @('src\OwnedSaveAs.cs','src\OwnedSaveAsTests.cs','src\OwnedSaveVerification.cs','src\NativeJournal.cs','src\DocumentBindings.cs','src\QueueOperations.cs','src\core_config.py','config\general-owned-save-targets.json','config\general-owned-saves.json')
$taskFiles += @('tools\audit_native_save_fixture.py')
$taskFiles += @('src\saved_result_fixture_oracle.py')
$taskFiles += @('src\test_sdk_saved_result_stdio.py','src\OwnedOperationPlanTests.cs')
$taskFiles += @('src\saved_reopen_fixture_oracle.py','src\OwnedSavedSource.cs','src\OwnedSavedReopen.cs','src\OwnedSavedReopenTests.cs','config\general-owned-saved-reopen.json','build.ps1')
$taskFiles += @('src\SavedReopenObservation.cs','src\OwnedSavedReopenLease.cs','src\OwnedSavedReopenExecution.cs','src\OwnedSavedReopenVerification.cs','src\OwnedSavedReopenExecutionTests.cs','src\saved_reopen_verification_oracle.py','src\WorkingCopyOpen.cs','src\OwnedOpenVerification.cs')
$taskFiles += @('src\test_sdk_reopen_verification_stdio.py')
$taskFiles += @('config\general-recovery-inspection.json')
$taskFiles += @('config\general-recovery-resolution.json')
$taskFiles += @('src\RecoveryFaultTests.cs','src\audit_recovery_fault_matrix.py','verify-recovery-faults.ps1','src\OwnedOpenVerification.cs','src\EntitySets.cs','src\OwnedTranslationTests.cs')
$taskFiles += @('src\test_sdk_recovery_stdio.py')
$taskFiles += @('src\audit_sdk_transport_faults.py')
$taskFiles += @('src\DocumentSession.cs','src\ActivationFaultTests.cs','src\audit_activation_faults.py','verify-activation-faults.ps1')
$taskHashes = @($taskFiles | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot $_)).Hash.ToLowerInvariant()} })
[ordered]@{schema='psbridge.sdk-verification.v1';finishedUtc=[DateTime]::UtcNow.ToString('o');passed=$true;version=$taskResult.structuredContent.adapterVersion;scope='SDK/read-only/prepared fake-panel; Python byte/schema parity; actual Framework synthetic artifact compatibility; Inspector CLI';cadConnectionAttempted=$false;runtimeDeploymentPerformed=$false;files=$taskHashes} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskEvidence 'sdk-verification.json') -Encoding utf8
Write-Output 'PASS: SDK build/tests, independent STDIO client and Inspector CLI; no CAD or deployment.'
