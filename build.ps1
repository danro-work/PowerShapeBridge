param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'work\build-staging'),
    [string]$ConfigurationPath = (Join-Path $PSScriptRoot 'releases\P2.6\bridge.config.json')
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$stagePath = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if (!$stagePath.StartsWith((Join-Path $taskRoot 'work')+'\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build into project work staging; never overwrite root launcher or installed runtime.'
}
if (!(Test-Path -LiteralPath $ConfigurationPath)) { throw 'Configuration is missing; provide -ConfigurationPath explicitly.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler not found.' }
$frameworkRelease = (Get-ItemProperty -LiteralPath 'HKLM:/SOFTWARE/Microsoft/NET Framework Setup/NDP/v4/Full').Release
if ($frameworkRelease -lt 528040) { throw '.NET Framework 4.8 or higher is required.' }
$executable = Join-Path $taskRoot 'PSBridge.exe'
$source = Join-Path $taskRoot 'src\Bridge.cs'
foreach ($target in @('winexe','exe')) {
    $executable = Join-Path $OutputDirectory $(if ($target -eq 'winexe') {'PSBridge.exe'} else {'PSBridge.Worker.exe'})
    & $compiler /nologo "/target:$target" /platform:x64 /optimize+ /utf8output /reference:Microsoft.CSharp.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/out:$executable" $source (Join-Path $taskRoot 'src\OwnedTranslation.cs') (Join-Path $taskRoot 'src\OwnedTranslationTests.cs') (Join-Path $taskRoot 'src\OwnedOperationPlans.cs') (Join-Path $taskRoot 'src\OwnedOperationPlanTests.cs') (Join-Path $taskRoot 'src\TranslationTest.cs') (Join-Path $taskRoot 'src\CheckpointTest.cs') (Join-Path $taskRoot 'src\PlannedTranslation.cs') (Join-Path $taskRoot 'src\DiskVerification.cs') (Join-Path $taskRoot 'src\QueueOperations.cs') (Join-Path $taskRoot 'src\SurfaceRecipes.cs') (Join-Path $taskRoot 'src\ExchangeQualification.cs') (Join-Path $taskRoot 'src\SectionQualification.cs') (Join-Path $taskRoot 'src\SliceRecipes.cs') (Join-Path $taskRoot 'src\GroupedExport.cs') (Join-Path $taskRoot 'src\SurfaceSectionJob.cs') (Join-Path $taskRoot 'src\BatchSectionQualification.cs') (Join-Path $taskRoot 'src\ReadReferenceQualification.cs') (Join-Path $taskRoot 'src\DocumentSession.cs') (Join-Path $taskRoot 'src\EntitySets.cs') (Join-Path $taskRoot 'src\OwnedCopies.cs') (Join-Path $taskRoot 'src\DocumentBindings.cs') (Join-Path $taskRoot 'src\NativeJournal.cs') (Join-Path $taskRoot 'src\WorkingCopyOpen.cs') (Join-Path $taskRoot 'src\OwnedOpenVerification.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
}
Copy-Item -LiteralPath $ConfigurationPath -Destination (Join-Path $OutputDirectory 'bridge.config.json') -Force
foreach ($module in @('mcp_server.py','core_config.py','entity_sets.py','operation_plans.py','working_copies.py','document_bindings.py','working_copy_open.py','owned_operation_plans.py','owned_translation.py','entity_references.py','document_handles.py','surface_job.py','slice_job.py','group_job.py')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot ('src\'+$module)) -Destination (Join-Path $OutputDirectory $module) -Force
}
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output /reference:System.Windows.Forms.dll "/out:$(Join-Path $OutputDirectory 'PSBridge.Launcher.exe')" (Join-Path $taskRoot 'src\Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
Write-Output 'Built panel and worker (x64, .NET Framework).'
$workerSource = Get-Content -LiteralPath $source -Raw
$workerVersion = [regex]::Match($workerSource, 'ReleaseVersion = "([^"]+)"').Groups[1].Value
$adapterSource = Get-Content -LiteralPath (Join-Path $OutputDirectory 'mcp_server.py') -Raw
$adapterVersion = [regex]::Match($adapterSource, "ADAPTER_VERSION = '([^']+)'").Groups[1].Value
if (!$workerVersion -or $workerVersion -ne $adapterVersion) { throw 'Worker/adapter versions differ.' }
$manifest = [ordered]@{builtUtc=[DateTime]::UtcNow.ToString('o');version=$workerVersion;configurationProfile=(Get-Content -LiteralPath (Join-Path $OutputDirectory 'bridge.config.json') -Raw | ConvertFrom-Json).coreProfile;files=@()}
foreach ($file in @('PSBridge.exe','PSBridge.Worker.exe','PSBridge.Launcher.exe','bridge.config.json','mcp_server.py','core_config.py','entity_sets.py','operation_plans.py','working_copies.py','document_bindings.py','working_copy_open.py','owned_operation_plans.py','owned_translation.py','entity_references.py','document_handles.py','surface_job.py','slice_job.py','group_job.py')) {
    $manifest.files += @{name=$file;sha256=(Get-FileHash -LiteralPath (Join-Path $OutputDirectory $file)).Hash.ToLowerInvariant()}
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build-manifest.json') -Encoding utf8








