param(
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\rotation-sample-predicate'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-rotation-sample-verification\framework')
)
$ErrorActionPreference='Stop'
$taskStage=[IO.Path]::GetFullPath($StageDirectory)
if(!$taskStage.StartsWith((Join-Path $PSScriptRoot 'work')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Use an own project work stage.'}
if(Test-Path -LiteralPath $taskStage){throw 'Fresh Framework predicate stage required.'}
New-Item -ItemType Directory -Path $taskStage | Out-Null
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskBinary=Join-Path $taskStage 'VerifyRotationSamples.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /reference:System.Core.dll "/out:$taskBinary" (Join-Path $PSScriptRoot 'src\RotationSampleVerifier.cs') (Join-Path $PSScriptRoot 'tools\VerifyRotationSamples.cs')
if($LASTEXITCODE -ne 0){throw 'Framework predicate compilation failed.'}
& $taskBinary | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'framework-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Framework predicate controls failed.'}
$taskFiles=@('src\RotationSampleVerifier.cs','tools\VerifyRotationSamples.cs')
$taskHashes=@($taskFiles | ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_)).Hash.ToLowerInvariant()}})
[ordered]@{state='passed';scope='Shared reference predicate: 2 analytic controls/1 no-op rejection on Framework';cadConnectionAttempted=$false;binarySha256=(Get-FileHash -LiteralPath $taskBinary).Hash.ToLowerInvariant();files=$taskHashes} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'framework-qualification.json') -Encoding utf8
