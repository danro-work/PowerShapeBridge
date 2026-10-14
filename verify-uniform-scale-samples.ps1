param(
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\uniform-scale-sample-predicate'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-uniform-scale-samples\framework')
)
$ErrorActionPreference='Stop'
$taskStage=[IO.Path]::GetFullPath($StageDirectory)
if(!$taskStage.StartsWith((Join-Path $PSScriptRoot 'work')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Own project work stage required.'}
if(Test-Path -LiteralPath $taskStage){throw 'Fresh Framework predicate stage required.'}
if(Test-Path -LiteralPath $EvidenceDirectory){throw 'Fresh evidence directory required.'}
New-Item -ItemType Directory -Path $taskStage | Out-Null
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskBinary=Join-Path $taskStage 'VerifyUniformScaleSamples.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /reference:System.Core.dll "/out:$taskBinary" (Join-Path $PSScriptRoot 'src\UniformScaleSampleVerifier.cs') (Join-Path $PSScriptRoot 'tools\VerifyUniformScaleSamples.cs')
if($LASTEXITCODE -ne 0){throw 'Framework scale predicate compilation failed.'}
& $taskBinary | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'framework-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Framework scale analytic checks failed.'}
$taskFiles=@('src\UniformScaleSampleVerifier.cs','tools\VerifyUniformScaleSamples.cs','verify-uniform-scale-samples.ps1')
$taskHashes=@($taskFiles|ForEach-Object{[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_)).Hash.ToLowerInvariant()}})
[ordered]@{state='passed';controls=4;refusals=5;scope='Shared uniform scale mathematical predicate';cadConnectionAttempted=$false;nativeProviderQualified=$false;copyFenceReleased=$false;binarySha256=(Get-FileHash -LiteralPath $taskBinary).Hash.ToLowerInvariant();files=$taskHashes} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'framework-qualification.json') -Encoding utf8
