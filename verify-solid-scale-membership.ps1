param(
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\solid-scale-membership-compile'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\solid-scale-membership-compile')
)
$ErrorActionPreference='Stop'
$taskStage=[IO.Path]::GetFullPath($StageDirectory)
$taskAllowed=([IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'work'))).TrimEnd('\')+'\'
if(!$taskStage.StartsWith($taskAllowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Own work staging required; never compile over installed runtime.'}
if(Test-Path -LiteralPath $taskStage){throw 'Fresh qualification compile stage required.'}
New-Item -ItemType Directory -Path $taskStage|Out-Null
New-Item -ItemType Directory -Force -Path $EvidenceDirectory|Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskReader=Join-Path $taskStage 'ReadSolidScaleMembership.exe'
$taskSource=Join-Path $PSScriptRoot 'src\SolidScaleMembershipVerifier.cs'
$taskReaderSource=Join-Path $PSScriptRoot 'tools\ReadSolidScaleMembership.cs'
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /reference:System.Core.dll /reference:System.Web.Extensions.dll "/out:$taskReader" $taskSource $taskReaderSource
if($LASTEXITCODE -ne 0){throw 'Framework membership verifier/reader compilation failed.'}
# No arguments: Main refuses before loading Native, config, any COM or file input.
& $taskReader
if($LASTEXITCODE -ne 2){throw 'Unconfigured qualification reader must refuse before connection.'}
[ordered]@{
    state='compiled_framework_membership_reader_refuses_without_inputs'
    verifierSourceSha256=(Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash.ToLowerInvariant()
    readerSourceSha256=(Get-FileHash -LiteralPath $taskReaderSource -Algorithm SHA256).Hash.ToLowerInvariant()
    readerSha256=(Get-FileHash -LiteralPath $taskReader -Algorithm SHA256).Hash.ToLowerInvariant()
    powerShapeConnectionAttempted=$false
    cadCommands=0
    fenceReleased=$false
}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'compile.json') -Encoding utf8
Write-Output 'PASS: shared membership code compiles for Framework; unconfigured reader refused without CAD access.'
