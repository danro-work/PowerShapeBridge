param(
    [string]$StagingDirectory = (Join-Path $PSScriptRoot 'work\P2.31-executor'),
    [string]$PythonPath = (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'),
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'evidence\P2.31-executor\code')
)
$ErrorActionPreference = 'Stop'
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
$records = @()
$env:PSBRIDGE_TEST_STAGE = [IO.Path]::GetFullPath($StagingDirectory)
& (Join-Path $StagingDirectory 'PSBridge.Worker.exe') --self-test | Tee-Object -FilePath (Join-Path $evidenceRoot 'worker-tests.txt')
if ($LASTEXITCODE -ne 0) { throw 'Worker internal tests failed.' }
foreach ($test in @('test_owned_translation.py','test_owned_operation_plans.py','test_working_copy_open.py','test_document_bindings.py','test_working_copies.py','test_operation_plans.py','test_entity_sets.py','test_entity_references.py','test_document_handles.py','test_core_config.py')) {
    & $PythonPath -m unittest discover -s (Join-Path $PSScriptRoot 'src') -p $test -v 2>&1 | Tee-Object -FilePath (Join-Path $evidenceRoot ($test+'.log'))
    $testExitCode = $LASTEXITCODE
    $records += @{test=$test;exitCode=$testExitCode}
    if ($testExitCode -ne 0) { throw "Test failed: $test" }
}
& $PythonPath (Join-Path $PSScriptRoot 'src\test_mcp.py') | Tee-Object -FilePath (Join-Path $evidenceRoot 'protocol.log')
if ($LASTEXITCODE -ne 0) { throw 'Protocol test failed.' }
$files = @()
foreach ($file in @('src\OwnedTranslation.cs','src\OwnedTranslationTests.cs','src\owned_translation.py','src\test_owned_translation.py','src\OwnedOperationPlans.cs','src\OwnedOperationPlanTests.cs','src\owned_operation_plans.py','src\test_owned_operation_plans.py','src\EntitySets.cs','src\entity_sets.py','src\OwnedCopies.cs','src\DocumentBindings.cs','src\NativeJournal.cs','src\WorkingCopyOpen.cs','src\OwnedOpenVerification.cs','src\working_copy_open.py','src\test_working_copy_open.py','src\document_bindings.py','src\test_document_bindings.py','src\working_copies.py','src\test_working_copies.py','src\operation_plans.py','src\test_operation_plans.py','src\mcp_server.py','src\core_config.py','src\Bridge.cs','src\QueueOperations.cs','config\general-owned-translations.json','build.ps1','deploy-general.ps1','panel-window.ps1')) {
    $path = Join-Path $PSScriptRoot $file
    $files += @{path=$file;sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}
}
foreach ($file in @('PSBridge.Worker.exe','PSBridge.exe','mcp_server.py','entity_sets.py','operation_plans.py','working_copies.py','document_bindings.py','working_copy_open.py','owned_operation_plans.py','owned_translation.py','bridge.config.json','build-manifest.json')) {
    $path = Join-Path $StagingDirectory $file
    $files += @{path=$path;sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}
}
$workerSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\Bridge.cs') -Raw
$workerVersion = [regex]::Match($workerSource, 'ReleaseVersion = "([^"]+)"').Groups[1].Value
@{timestampUtc=[DateTime]::UtcNow.ToString('o');stage=(Split-Path $StagingDirectory -Leaf);version=$workerVersion;scope='Code checks only; no CAD connection or mutation in this verification script';tests=$records;protocolPassed=$true;workerInternalTestsPassed=$true;runtimeDeploymentPerformed=$false;files=$files} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'code-verification.json') -Encoding utf8


