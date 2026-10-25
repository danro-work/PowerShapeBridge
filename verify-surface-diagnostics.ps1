param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'evidence\surface-diagnostics-ci'))
$ErrorActionPreference = 'Stop'
$taskEvidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!$taskEvidence.StartsWith($PSScriptRoot+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Diagnostic evidence must remain in this project.' }
$taskStage = Join-Path $PSScriptRoot ('work\surface-diagnostics-ci-'+[guid]::NewGuid().ToString('N'))
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path $taskStage | Out-Null
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
$taskFiles = @()
foreach ($taskName in @('ReadGuardedSurfaceSnapshots','ReadSurfaceGetterLayout','ReadSurfaceParameterLayout')) {
    $taskSource = Join-Path $PSScriptRoot ('tools\'+$taskName+'.cs')
    $taskExe = Join-Path $taskStage ($taskName+'.exe')
    & $taskCompiler /nologo /utf8output /target:exe /platform:x64 /optimize+ /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll "/out:$taskExe" $taskSource
    if ($LASTEXITCODE -ne 0) { throw "Diagnostic compilation failed: $taskName" }
    $taskFiles += @{name=$taskName;sourceSha256=(Get-FileHash -LiteralPath $taskSource).Hash.ToLowerInvariant();binarySha256=(Get-FileHash -LiteralPath $taskExe).Hash.ToLowerInvariant()}
}
# Compile only: no diagnostic, worker, panel or CAD process is launched here.
@{state='compiled';files=$taskFiles;diagnosticsExecuted=$false;cadConnectionAttempted=$false;runtimeDeploymentPerformed=$false} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskEvidence 'compilation.json') -Encoding utf8
Write-Output 'PASS: three standalone surface diagnostics compiled; no CAD connection or diagnostic execution.'
