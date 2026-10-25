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
& (Join-Path $taskRoot 'verify-rotation-samples.ps1') -StageDirectory ($NativeStageDirectory+'-sample-predicate') -EvidenceDirectory (Join-Path $taskEvidence 'rotation-sample-predicate')
& (Join-Path $taskRoot 'verify-uniform-scale-samples.ps1') -StageDirectory ($NativeStageDirectory+'-uniform-scale-predicate') -EvidenceDirectory (Join-Path $taskEvidence 'uniform-scale-predicate')
& (Join-Path $taskRoot 'verify-solid-scale-membership.ps1') -StageDirectory ($NativeStageDirectory+'-solid-scale-membership') -EvidenceDirectory (Join-Path $taskEvidence 'solid-scale-membership')
& (Join-Path $taskRoot 'verify-solid-scale-measurements.ps1') -StageDirectory ($NativeStageDirectory+'-solid-scale-measurements') -EvidenceDirectory (Join-Path $taskEvidence 'solid-scale-measurements')
& (Join-Path $taskRoot 'verify-owned-solid-scale.ps1') -StageDirectory ($NativeStageDirectory+'-owned-solid-scale') -EvidenceDirectory (Join-Path $taskEvidence 'owned-solid-scale')
& (Join-Path $taskRoot 'verify-owned-open-adoption.ps1') -StageDirectory ($NativeStageDirectory+'-owned-open-adoption') -EvidenceDirectory (Join-Path $taskEvidence 'owned-open-adoption')
& (Join-Path $taskRoot 'verify-saved-result-observation.ps1') -StageDirectory ($NativeStageDirectory+'-saved-result-observation') -EvidenceDirectory (Join-Path $taskEvidence 'saved-result-observation')
& (Join-Path $taskRoot 'verify-current-saved-source.ps1') -StageDirectory ($NativeStageDirectory+'-current-saved-source') -EvidenceDirectory (Join-Path $taskEvidence 'current-saved-source')
& (Join-Path $taskRoot 'verify-saved-result-restore.ps1') -StageDirectory ($NativeStageDirectory+'-saved-result-restore') -EvidenceDirectory (Join-Path $taskEvidence 'saved-result-restore')
& (Join-Path $taskRoot 'verify-saved-reopen-window-policy.ps1') -StageDirectory ($NativeStageDirectory+'-saved-reopen-quota') -EvidenceDirectory (Join-Path $taskEvidence 'saved-reopen-quota')
& (Join-Path $taskRoot 'verify-owned-saved-copy-close.ps1') -StageDirectory ($NativeStageDirectory+'-saved-copy-close') -EvidenceDirectory (Join-Path $taskEvidence 'saved-copy-close')
& (Join-Path $taskRoot 'verify-owned-saved-result-close.ps1') -StageDirectory ($NativeStageDirectory+'-saved-result-close') -EvidenceDirectory (Join-Path $taskEvidence 'saved-result-close')
& (Join-Path $taskRoot 'verify-owned-saved-finalization.ps1') -StageDirectory ($NativeStageDirectory+'-saved-finalization') -EvidenceDirectory (Join-Path $taskEvidence 'saved-finalization')
& (Join-Path $taskRoot 'verify-returned-rotation.ps1') -StageDirectory ($NativeStageDirectory+'-returned-rotation') -EvidenceDirectory (Join-Path $taskEvidence 'returned-rotation')
& (Join-Path $taskRoot 'verify-dmt-reader.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-dmt-reader') -EvidenceDirectory (Join-Path $taskEvidence 'dmt-reader')
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
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_rotation_plan_stdio.py') --dotnet $DotnetPath --host $taskHost --evidence (Join-Path $taskEvidence 'rotation-plans')
if ($LASTEXITCODE -ne 0) { throw 'Independent passive rotation plan STDIO failed.' }
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_uniform_scale_stdio.py') --dotnet $DotnetPath --host $taskHost --evidence (Join-Path $taskEvidence 'uniform-scale-plans')
if ($LASTEXITCODE -ne 0) { throw 'Independent uniform scale STDIO failed.' }
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_execution.py') --dotnet $DotnetPath --host $taskHost --evidence $taskEvidence
if ($LASTEXITCODE -ne 0) { throw 'Independent execution facade parity failed.' }
& $PythonPath -m unittest discover -s (Join-Path $taskRoot 'src') -p 'test_save_target_fixture_oracle.py'
if ($LASTEXITCODE -ne 0) { throw 'Type-sensitive target oracle regression failed.' }
& (Join-Path $taskRoot 'build.ps1') -OutputDirectory $NativeStageDirectory -ConfigurationPath (Join-Path $taskRoot 'config\general-owned-translations.json')
& (Join-Path $NativeStageDirectory 'PSBridge.Worker.exe') --self-test | Tee-Object -FilePath (Join-Path $taskEvidence 'native-fixture-tests.txt')
if ($LASTEXITCODE -ne 0) { throw 'Native synthetic fixture generation failed.' }
& (Join-Path $NativeStageDirectory 'PSBridge.Worker.exe') --dgk-bundle-files-test | Tee-Object -FilePath (Join-Path $taskEvidence 'dgk-bundle-file-tests.txt')
if ($LASTEXITCODE -ne 0) { throw 'DGK retained artifact fixture tests failed.' }
& $PythonPath (Join-Path $taskRoot 'tools\audit_dgk_bundle.py') --stage $NativeStageDirectory --output (Join-Path $taskEvidence 'dgk-bundle-file-audit.json')
if ($LASTEXITCODE -ne 0) { throw 'Independent DGK byte/identity audit failed.' }
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
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_activation_recovery_stdio.py') --dotnet $DotnetPath --host $taskHost --stage ($NativeStageDirectory+'-activation') --evidence $taskEvidence
if($LASTEXITCODE -ne 0){throw 'SDK legacy activation recovery STDIO qualification failed.'}
& (Join-Path $taskRoot 'verify-checkpoint-recovery.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-checkpoint') -EvidenceDirectory (Join-Path $taskEvidence 'checkpoint-recovery')
& $PythonPath (Join-Path $taskRoot 'src\test_sdk_checkpoint_recovery_stdio.py') --dotnet $DotnetPath --host $taskHost --stage ($NativeStageDirectory+'-checkpoint') --evidence (Join-Path $taskEvidence 'checkpoint-recovery')
if($LASTEXITCODE -ne 0){throw 'SDK/native checkpoint recovery STDIO qualification failed.'}
& (Join-Path $taskRoot 'verify-checkpoint-recovered-open.ps1') -PythonPath $PythonPath -DotnetPath $DotnetPath -StageDirectory ($NativeStageDirectory+'-recovered-open') -EvidenceDirectory (Join-Path $taskEvidence 'checkpoint-recovered-open')
$taskFixture = Join-Path $taskRoot ('work\K2-inspector-'+[guid]::NewGuid().ToString('N'))
& (Join-Path $taskRoot 'verify-owned-rotation-plans.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-owned-rotation') -EvidenceDirectory (Join-Path $taskEvidence 'owned-rotation-plans')
& (Join-Path $taskRoot 'verify-owned-uniform-scale-plans.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-owned-scale') -EvidenceDirectory (Join-Path $taskEvidence 'owned-uniform-scale-plans')
& $DotnetPath run --no-restore --configuration Release --project $taskNativeProject -- --owned-rotation-packets ($NativeStageDirectory+'-owned-rotation') (Join-Path $taskEvidence 'owned-rotation-native-sdk.json')
if($LASTEXITCODE -ne 0){throw 'Native owned rotation packet SDK proof compatibility failed.'}
& (Join-Path $taskRoot 'verify-owned-rotation-wire.ps1') -PythonPath $PythonPath -DotnetPath $DotnetPath -HostPath $taskHost -StageDirectory ($NativeStageDirectory+'-rotation-wire') -EvidenceDirectory (Join-Path $taskEvidence 'owned-rotation-wire')
& (Join-Path $taskRoot 'verify-owned-uniform-scale-wire.ps1') -PythonPath $PythonPath -DotnetPath $DotnetPath -HostPath $taskHost -StageDirectory ($NativeStageDirectory+'-scale-wire') -EvidenceDirectory (Join-Path $taskEvidence 'owned-scale-wire')
& (Join-Path $taskRoot 'verify-owned-dgk-wire.ps1') -PythonPath $PythonPath -DotnetPath $DotnetPath -HostPath $taskHost -StageDirectory ($NativeStageDirectory+'-dgk-wire') -EvidenceDirectory (Join-Path $taskEvidence 'owned-dgk-wire')
& (Join-Path $taskRoot 'verify-owned-rotation-execution.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-rotation-execution') -EvidenceDirectory (Join-Path $taskEvidence 'owned-rotation-execution')
& (Join-Path $taskRoot 'verify-rotation-measurements.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-rotation-measurements') -EvidenceDirectory (Join-Path $taskEvidence 'rotation-measurements')
& (Join-Path $taskRoot 'verify-solid-face-measurements.ps1') -StageDirectory ($NativeStageDirectory+'-solid-face-measurements') -EvidenceDirectory (Join-Path $taskEvidence 'solid-face-measurements')
& (Join-Path $taskRoot 'verify-solid-quarter-turn.ps1') -StageDirectory ($NativeStageDirectory+'-solid-quarter-turn') -EvidenceDirectory (Join-Path $taskEvidence 'solid-quarter-turn')
& (Join-Path $taskRoot 'verify-solid-quarter-turn.ps1') -StageDirectory ($NativeStageDirectory+'-solid-rotation') -EvidenceDirectory (Join-Path $taskEvidence 'solid-rotation') -ConfigurationPath (Join-Path $taskRoot 'config\general-solid-rotation.json')
& (Join-Path $taskRoot 'verify-solid-rotation-context.ps1') -StageDirectory ($NativeStageDirectory+'-solid-rotation-context') -EvidenceDirectory (Join-Path $taskEvidence 'solid-rotation-context')
& (Join-Path $taskRoot 'verify-rotation-measurement-records.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-measurement-records') -EvidenceDirectory (Join-Path $taskEvidence 'rotation-measurement-records')
& (Join-Path $taskRoot 'verify-rotation-verification.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-rotation-verification') -EvidenceDirectory (Join-Path $taskEvidence 'rotation-verification')
& (Join-Path $taskRoot 'verify-geometry-export-targets.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-export-targets') -EvidenceDirectory (Join-Path $taskEvidence 'geometry-export-targets')
& (Join-Path $taskRoot 'verify-owned-dgk-targets.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-dgk-targets') -EvidenceDirectory (Join-Path $taskEvidence 'owned-dgk-targets')
& $DotnetPath run --no-restore --configuration Release --project $taskNativeProject -- --owned-dgk-targets ($NativeStageDirectory+'-dgk-targets') (Join-Path $taskEvidence 'dgk-native-sdk-compatibility.json')
if($LASTEXITCODE -ne 0){throw 'Native DGK target SDK compatibility failed.'}
& (Join-Path $taskRoot 'verify-owned-dgk-export.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-dgk-export') -EvidenceDirectory (Join-Path $taskEvidence 'owned-dgk-export')
& (Join-Path $taskRoot 'verify-owned-dgk-verification.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-dgk-verifier') -EvidenceDirectory (Join-Path $taskEvidence 'owned-dgk-verification')
& (Join-Path $taskRoot 'verify-geometry-export-executor.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-export-executor') -EvidenceDirectory (Join-Path $taskEvidence 'geometry-export-executor')
& (Join-Path $taskRoot 'verify-geometry-export-verification.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-export-verifier') -EvidenceDirectory (Join-Path $taskEvidence 'geometry-export-verification')
& (Join-Path $taskRoot 'verify-native-selection-reading.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-native-selection') -EvidenceDirectory (Join-Path $taskEvidence 'native-selection-reading')
& (Join-Path $taskRoot 'verify-native-levels-reading.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-native-levels') -EvidenceDirectory (Join-Path $taskEvidence 'native-levels-reading')
& (Join-Path $taskRoot 'verify-owned-level-move-plans.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-level-moves') -EvidenceDirectory (Join-Path $taskEvidence 'owned-level-move-plans')
& (Join-Path $taskRoot 'verify-owned-level-move-execution.ps1') -PythonPath $PythonPath -StageDirectory ($NativeStageDirectory+'-level-execution') -EvidenceDirectory (Join-Path $taskEvidence 'owned-level-move-execution')
& (Join-Path $taskRoot 'verify-owned-level-move-wire.ps1') -PythonPath $PythonPath -DotnetPath $DotnetPath -HostPath $taskHost -StageDirectory ($NativeStageDirectory+'-level-wire') -EvidenceDirectory (Join-Path $taskEvidence 'owned-level-move-wire')
& (Join-Path $taskRoot 'verify-owned-workplanes.ps1') -PythonPath $PythonPath -DotnetPath $DotnetPath -HostPath $taskHost -StageDirectory ($NativeStageDirectory+'-workplanes') -EvidenceDirectory (Join-Path $taskEvidence 'owned-workplanes')
& (Join-Path $taskRoot 'verify-workplane-activation.ps1') -PythonPath $PythonPath -DotnetPath $DotnetPath -HostPath $taskHost -StageDirectory ($NativeStageDirectory+'-workplane-activation') -EvidenceDirectory (Join-Path $taskEvidence 'workplane-activation')
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
function Get-SdkSourceFiles([string]$Directory) {
    Get-ChildItem -LiteralPath $Directory -File -Filter '*.cs'
    foreach ($taskSourceDirectory in (Get-ChildItem -LiteralPath $Directory -Directory)) {
        if ($taskSourceDirectory.Name -notin @('bin','obj')) { Get-SdkSourceFiles $taskSourceDirectory.FullName }
    }
}
$taskFiles += @(foreach ($taskSourceRoot in @('src\PowerShapeMcp.Host','tests\PowerShapeMcp.Tests')) {
    Get-SdkSourceFiles (Join-Path $taskRoot $taskSourceRoot) | ForEach-Object { [IO.Path]::GetRelativePath($taskRoot,$_.FullName) }
})
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
$taskFiles += @('src\test_sdk_activation_recovery_stdio.py')
$taskFiles += @('src\CheckpointRecovery.cs','src\CheckpointRecoveryTests.cs','src\audit_checkpoint_recovery.py','src\test_sdk_checkpoint_recovery_stdio.py','verify-checkpoint-recovery.ps1','config\general-checkpoint-recovery.json')
$taskFiles += @('src\CheckpointRecoveredOpenTests.cs','src\audit_checkpoint_recovered_open.py','verify-checkpoint-recovered-open.ps1','tools\NativeFixtureCheck\CheckpointOpenCheck.cs')
$taskFiles += @('src\CheckpointRecoveryWireFixture.cs','src\test_sdk_checkpoint_wire_stdio.py','src\audit_checkpoint_wire_stdio.py')
$taskFiles += @('src\OwnedCopies.cs','src\Bridge.cs','src\test_sdk_checkpoint_filesystem_faults.py','src\audit_checkpoint_filesystem_stdio.py')
$taskFiles += @('src\test_sdk_rotation_plan_stdio.py','config\general-rotation-plans.json')
$taskFiles += @('src\OwnedRotationPlans.cs','src\OwnedRotationPlanTests.cs','src\audit_owned_rotation_plans.py','verify-owned-rotation-plans.ps1')
$taskFiles += @('src\OwnedUniformScalePlans.cs','src\OwnedUniformScalePlanTests.cs','src\audit_owned_uniform_scale_plans.py','verify-owned-uniform-scale-plans.ps1')
$taskFiles += @('tools\NativeFixtureCheck\OwnedRotationCheck.cs','config\general-owned-rotation-plans.json')
$taskFiles += @('src\OwnedRotationWireFixture.cs','src\test_sdk_owned_rotation_wire_stdio.py','src\audit_owned_rotation_wire_stdio.py','verify-owned-rotation-wire.ps1')
$taskFiles += @('src\OwnedUniformScaleWireFixture.cs','src\test_sdk_owned_uniform_scale_wire_stdio.py','src\audit_owned_uniform_scale_wire_stdio.py','verify-owned-uniform-scale-wire.ps1','config\general-owned-uniform-scale-plans.json','src\PowerShapeMcp.Host\OwnedUniformScaleStore.cs','src\PowerShapeMcp.Host\OwnedUniformScaleTools.cs','tests\PowerShapeMcp.Tests\OwnedUniformScaleFixture.cs','tests\PowerShapeMcp.Tests\OwnedUniformScaleStoreTests.cs','tests\PowerShapeMcp.Tests\OwnedUniformScaleRoutesTests.cs')
$taskFiles += @('src\OwnedRotationExecution.cs','src\OwnedRotationExecutionTests.cs','src\audit_owned_rotation_execution.py','verify-owned-rotation-execution.ps1')
$taskFiles += @('src\RotationSampleVerifier.cs','tools\VerifyRotationSamples.cs','verify-rotation-samples.ps1')
$taskFiles += @('src\UniformScaleSampleVerifier.cs','tools\VerifyUniformScaleSamples.cs','verify-uniform-scale-samples.ps1','tests\PowerShapeMcp.Tests\UniformScaleSampleVerifierTests.cs')
$taskFiles += @('src\PowerShapeMcp.Host\UniformScalePlanStore.cs','tests\PowerShapeMcp.Tests\UniformScalePlanStoreTests.cs')
$taskFiles += @('src\PowerShapeMcp.Host\UniformScalePlanTools.cs','tests\PowerShapeMcp.Tests\UniformScalePlanRoutesTests.cs','src\test_sdk_uniform_scale_stdio.py','config\general-uniform-scale-plans.json')
$taskFiles += @('src\RotationMeasurements.cs','src\RotationMeasurementTests.cs','src\audit_rotation_measurements.py','verify-rotation-measurements.ps1')
$taskFiles += @('src\SolidFaceMeasurements.cs','src\SolidFaceMeasurementTests.cs','verify-solid-face-measurements.ps1')
$taskFiles += @('src\SolidScaleMembershipMeasurements.cs','src\SolidScaleMembershipMeasurementTests.cs','tools\ReadNativeScaleSource.cs','verify-solid-scale-measurements.ps1')
$taskFiles += @('src\OwnedSolidScale.cs','src\OwnedSolidScaleTests.cs','src\SolidUniformScaleProgram.cs','src\PowerShapeMcp.Host\SolidScaleTools.cs','src\PowerShapeMcp.Host\SolidScaleStore.cs','src\PowerShapeMcp.Host\SolidScaleProof.cs','tests\PowerShapeMcp.Tests\SolidScaleRoutesTests.cs','config\general-solid-scale.json','verify-owned-solid-scale.ps1')
$taskFiles += @('src\OwnedScaleRestoration.cs','src\PowerShapeMcp.Host\ScaleRestoreTools.cs','tests\PowerShapeMcp.Tests\ScaleRestoreTests.cs','config\general-owned-solid-scale-restore.json')
$taskFiles += @('src\OwnedOpenAdoption.cs','src\OwnedOpenAdoptionTests.cs','src\PowerShapeMcp.Host\OwnedOpenAdoptionTools.cs','tests\PowerShapeMcp.Tests\OwnedOpenAdoptionTests.cs','config\general-open-adoption.json','verify-owned-open-adoption.ps1')
$taskFiles += @('src\CurrentSavedResultSource.cs','src\CurrentSavedResultSourceTests.cs','verify-current-saved-source.ps1')
$taskFiles += @('src\SavedReopenSource.cs','src\SavedResultRestoreTests.cs','src\PowerShapeMcp.Host\SavedResultObservationProof.cs','src\PowerShapeMcp.Host\SavedResultRestoreTools.cs','tests\PowerShapeMcp.Tests\SavedResultRestoreTests.cs','config\general-saved-result-restore.json','verify-saved-result-restore.ps1')
$taskFiles += @('src\SavedReopenWindowPolicy.cs','verify-saved-reopen-window-policy.ps1')
$taskFiles += @('src\OwnedSavedCopyClose.cs','src\OwnedSavedCopyCloseTests.cs','verify-owned-saved-copy-close.ps1')
$taskFiles += @('src\OwnedSavedResultClose.cs','src\OwnedSavedResultCloseTests.cs','src\PowerShapeMcp.Host\SavedResultCloseTools.cs','tests\PowerShapeMcp.Tests\SavedResultCloseTests.cs','config\general-owned-saved-result-close.json','verify-owned-saved-result-close.ps1')
$taskFiles += @('src\OwnedSavedFinalization.cs','src\SavedFinalizationSeal.cs','src\OwnedSavedFinalizationTests.cs','src\PowerShapeMcp.Host\SavedFinalizationTools.cs','tests\PowerShapeMcp.Tests\SavedFinalizationTests.cs','config\general-owned-saved-finalization.json','verify-owned-saved-finalization.ps1')
$taskFiles += @('src\ReturnedRotationEvidence.cs','src\ReturnedRotationEvidenceTests.cs','src\ReturnedSolidRotationVerification.cs','src\PowerShapeMcp.Host\ReturnedRotationProof.cs','tests\PowerShapeMcp.Tests\ReturnedRotationProofTests.cs','verify-returned-rotation.ps1')
$taskFiles += @('src\PowerShapeMcp.Host\SavedCopyCloseProof.cs','src\PowerShapeMcp.Host\SavedCopyCloseTools.cs','tests\PowerShapeMcp.Tests\SavedCopyCloseTests.cs','config\general-owned-saved-copy-close.json')
$taskFiles += @('src\OwnedSolidQuarterTurn.cs','src\PowerShapeMcp.Host\SolidQuarterTurnTools.cs','src\PowerShapeMcp.Host\SolidQuarterTurnProof.cs','src\PowerShapeMcp.Host\SolidQuarterTurnStore.cs','tests\PowerShapeMcp.Tests\SolidQuarterTurnRoutesTests.cs','tools\CheckSolidQuarterTurnBoundaries.cs','verify-solid-quarter-turn.ps1','config\general-solid-quarter-turn.json')
$taskFiles += @('src\PowerShapeMcp.Host\SolidRotationTools.cs','tests\PowerShapeMcp.Tests\SolidRotationRoutesTests.cs','config\general-solid-rotation.json')
$taskFiles += @('src\SolidRotationContext.cs','src\SolidRotationContextTests.cs','tools\ReadGuardedRotationSnapshots.cs','verify-solid-rotation-context.ps1')
$taskFiles += @('src\RotationMeasurementRecords.cs','src\RotationMeasurementRecordTests.cs','src\audit_rotation_measurement_records.py','verify-rotation-measurement-records.ps1')
$taskFiles += @('src\OwnedRotationVerification.cs','src\OwnedRotationVerificationTests.cs','src\audit_rotation_verification.py','verify-rotation-verification.ps1')
$taskFiles += @('src\OwnedGeometryExportTargets.cs','src\OwnedGeometryExportTargetTests.cs','src\audit_geometry_export_targets.py','verify-geometry-export-targets.ps1')
$taskFiles += @('src\DmtMeshReader.cs','tools\ReadDmtMesh.cs','src\qualify_dmt_reader.py','verify-dmt-reader.ps1','tests\PowerShapeMcp.Tests\DmtMeshReaderTests.cs','tests\PowerShapeMcp.Tests\fixtures\dmt\source-manifest.jsonl','tests\PowerShapeMcp.Tests\fixtures\dmt\NormalDmt.dmt','tests\PowerShapeMcp.Tests\fixtures\dmt\TwoBlocks.dmt','tests\PowerShapeMcp.Tests\fixtures\dmt\smallModel.dmt')
$taskFiles += @('src\OwnedGeometryExport.cs','src\OwnedGeometryExportTests.cs','src\audit_geometry_export_executor.py','verify-geometry-export-executor.ps1')
$taskFiles += @('src\OwnedGeometryExportVerification.cs','src\OwnedGeometryExportVerificationTests.cs','src\audit_geometry_export_verification.py','verify-geometry-export-verification.ps1')
$taskFiles += @('src\NativeSelectionReading.cs','src\NativeSelectionReadingTests.cs','src\audit_native_selection_reading.py','verify-native-selection-reading.ps1','tools\ReadPowerShapeExportMetadata.cs')
$taskFiles += @('src\NativeLevelReading.cs','src\NativeLevelReadingTests.cs','src\audit_native_levels_reading.py','verify-native-levels-reading.ps1','tools\ReadNativeLevelLayout.cs','tools\audit_native_levels_read.py')
$taskFiles += @('src\PowerShapeMcp.Host\LevelReadTools.cs','tests\PowerShapeMcp.Tests\LevelReadToolsTests.cs','config\general-levels-read.json','tools\ReadNativeLevelRecipe.cs','tools\qualify_levels_live_stdio.py','tools\audit_levels_live_stdio.py')
$taskFiles += @('src\OwnedLevelMovePlans.cs','src\OwnedLevelMovePlanTests.cs','src\audit_owned_level_move_plans.py','verify-owned-level-move-plans.ps1')
$taskFiles += @('src\LevelMovePostconditions.cs','src\OwnedLevelMoveExecution.cs','src\OwnedLevelMoveExecutionTests.cs','src\audit_owned_level_move_execution.py','verify-owned-level-move-execution.ps1')
$taskFiles += @('src\OwnedLevelMoveOperations.cs','src\OwnedLevelMoveWireFixture.cs','src\PowerShapeMcp.Host\OwnedLevelMoveStore.cs','src\PowerShapeMcp.Host\OwnedLevelMoveTools.cs','tests\PowerShapeMcp.Tests\OwnedLevelMoveStoreTests.cs','config\general-owned-level-moves.json','src\test_sdk_owned_level_move_wire_stdio.py','src\audit_owned_level_move_wire_stdio.py','verify-owned-level-move-wire.ps1')
$taskFiles += @('src\DgkBundleVerifier.cs','src\DgkBundleFiles.cs','src\DgkBundleFilesTests.cs','tools\audit_dgk_bundle.py')
$taskFiles += @('src\OwnedDgkTargets.cs','src\OwnedDgkTargetTests.cs','src\audit_owned_dgk_targets.py','verify-owned-dgk-targets.ps1')
$taskFiles += @('src\OwnedWorkingBytePin.cs','src\OwnedWorkingBytePinTests.cs')
$taskFiles += @('src\OwnedDgkExport.cs','src\OwnedDgkExportTests.cs','src\audit_owned_dgk_export.py','verify-owned-dgk-export.ps1')
$taskFiles += @('src\OwnedDgkExportVerification.cs','src\OwnedDgkExportVerificationTests.cs','src\audit_owned_dgk_verification.py','verify-owned-dgk-verification.ps1')
$taskFiles += @('tools\NativeFixtureCheck\DgkTargetCheck.cs','config\general-owned-dgk-targets.json')
$taskFiles += @('src\OwnedDgkTargetWireFixture.cs','src\test_sdk_owned_dgk_wire_stdio.py','src\audit_owned_dgk_wire_stdio.py','verify-owned-dgk-wire.ps1')
$taskFiles += @('src\WorkplaneFrame.cs','src\OwnedWorkplanePlans.cs','src\OwnedWorkplaneExecution.cs','src\OwnedWorkplaneOperations.cs','src\OwnedWorkplaneTests.cs','src\OwnedWorkplaneWireFixture.cs','src\PowerShapeMcp.Host\OwnedWorkplaneStore.cs','src\PowerShapeMcp.Host\OwnedWorkplaneTools.cs','tests\PowerShapeMcp.Tests\OwnedWorkplaneTests.cs','config\general-owned-workplanes.json','verify-owned-workplanes.ps1','src\test_sdk_owned_workplane_wire_stdio.py','src\audit_owned_workplanes.py','src\audit_owned_workplane_wire_stdio.py','src\test_sdk_owned_workplane_transport.py','src\audit_owned_workplane_transport.py')
$taskFiles += @('src\OwnedWorkplaneActivation.cs','src\OwnedWorkplaneActivationTests.cs','src\WorkplaneActivationWireFixture.cs','src\PowerShapeMcp.Host\OwnedWorkplaneActivationStore.cs','src\PowerShapeMcp.Host\OwnedWorkplaneActivationTools.cs','tests\PowerShapeMcp.Tests\WorkplaneActivationTests.cs','config\general-workplane-activation.json','verify-workplane-activation.ps1','src\test_sdk_workplane_activation_stdio.py','src\audit_workplane_activation_stdio.py','src\test_sdk_workplane_activation_transport.py','src\audit_workplane_activation_transport.py')
$taskFiles += @('tools\audit_live_workplane_activation.py','tools\verify_live_activation_audit.py')
$taskFiles += @('tools\audit_live_multiple_workplanes.py','tools\ReadOwnedWorkplaneFrames.cs')
$taskFiles += @('src\OwnedTransformSource.cs','src\OwnedSurfaceRotation.cs','src\OwnedSurfaceRotationTests.cs','src\OwnedSurfaceTranslation.cs','src\OwnedSurfaceSelection.cs','src\NativeSurfaceSampling.cs','src\PowerShapeMcp.Host\OwnedSurfaceRotationTools.cs','tests\PowerShapeMcp.Tests\OwnedSurfaceRotationTests.cs','tools\NativeFixtureCheck\SurfaceRotationCheck.cs','config\general-surface-rotations.json','verify-owned-surface-rotation.ps1')
$taskHashes = @($taskFiles | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot $_)).Hash.ToLowerInvariant()} })
[ordered]@{schema='psbridge.sdk-verification.v1';finishedUtc=[DateTime]::UtcNow.ToString('o');passed=$true;version=$taskResult.structuredContent.adapterVersion;scope='SDK/read-only/prepared fake-panel; Python byte/schema parity; actual Framework synthetic artifact compatibility; Inspector CLI';cadConnectionAttempted=$false;runtimeDeploymentPerformed=$false;files=$taskHashes} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskEvidence 'sdk-verification.json') -Encoding utf8
Write-Output 'PASS: SDK build/tests, independent STDIO client and Inspector CLI; no CAD or deployment.'
