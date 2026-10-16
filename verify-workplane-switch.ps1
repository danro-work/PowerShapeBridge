param(
    [string]$StageDirectory = (Join-Path $PSScriptRoot 'work\workplane-switch-ci'),
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'evidence\workplane-switch-ci')
)
$ErrorActionPreference = 'Stop'
$taskStage = [IO.Path]::GetFullPath($StageDirectory)
$taskEvidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!$taskStage.StartsWith((Join-Path $PSScriptRoot 'work')+'\', [StringComparison]::OrdinalIgnoreCase) -or
    !$taskEvidence.StartsWith((Join-Path $PSScriptRoot 'evidence')+'\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Isolated project stage and evidence directories required.'
}
if ((Test-Path -LiteralPath $taskStage) -or (Test-Path -LiteralPath $taskEvidence)) {
    throw 'Fresh directories required; no reuse of qualification artifacts.'
}
New-Item -ItemType Directory -Path $taskStage,$taskEvidence | Out-Null
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskHelper = Join-Path $taskStage 'QualifyOwnedWorkplaneSwitch.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /utf8output /reference:Microsoft.CSharp.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll "/out:$taskHelper" (Join-Path $PSScriptRoot 'tools\QualifyOwnedWorkplaneSwitch.cs')
if ($LASTEXITCODE -ne 0) { throw 'Workplane qualifier compilation failed.' }
$taskFixture = Join-Path $PSScriptRoot 'tests\fixtures\workplane-switch-synthetic.json'
if (!(Get-Content -LiteralPath $taskFixture -Raw | ConvertFrom-Json).fixtureOnly) { throw 'Synthetic fixture required.' }
# This explicit mode uses Fake only. No worker, config, COM or live reader is loaded.
$taskOutput = & $taskHelper --self-test $taskFixture
if ($LASTEXITCODE -ne 0) { throw 'Coordinate and no-replay controls failed.' }
$taskResult = $taskOutput | ConvertFrom-Json
if ($taskResult.state -ne 'passed' -or $taskResult.tests -ne 9 -or
    $taskResult.powerShapeConnectionAttempted -ne $false -or $taskResult.cadMutationAttempted -ne $false) {
    throw 'Unexpected control result.'
}
$taskOutput | Set-Content -LiteralPath (Join-Path $taskEvidence 'controls.json') -Encoding utf8
[ordered]@{
    state='passed'; tests=9; cadConnectionAttempted=$false; runtimeDeploymentPerformed=$false
    sourceSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'tools\QualifyOwnedWorkplaneSwitch.cs')).Hash.ToLowerInvariant()
    fixtureSha256=(Get-FileHash -LiteralPath $taskFixture).Hash.ToLowerInvariant()
    scope='Synthetic coordinate and unknown-outcome boundaries; no live CAD qualification'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskEvidence 'result.json') -Encoding utf8
Write-Output 'PASS: compiled qualifier, nine synthetic controls; no CAD or runtime changes.'
