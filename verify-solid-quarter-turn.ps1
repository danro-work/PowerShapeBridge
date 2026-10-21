param(
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\solid-quarter-turn-boundaries'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\solid-quarter-turn-boundaries'),
    [string]$ConfigurationPath=(Join-Path $PSScriptRoot 'config\general-solid-quarter-turn.json')
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StageDirectory){throw 'Fresh own boundary fixture stage required.'}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $StageDirectory -ConfigurationPath $ConfigurationPath
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskRunner=Join-Path $StageDirectory 'CheckBoundaries.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /reference:System.Core.dll /reference:System.Web.Extensions.dll "/out:$taskRunner" (Join-Path $PSScriptRoot 'tools\CheckSolidQuarterTurnBoundaries.cs')
if($LASTEXITCODE -ne 0){throw 'Boundary runner compile failed.'}
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
& $taskRunner (Join-Path $StageDirectory 'PSBridge.Worker.exe') 2>&1 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'marker-refusal.txt')
if($LASTEXITCODE -ne 1){throw 'Unmarked boundary runner must refuse.'}
Set-Content -LiteralPath (Join-Path $StageDirectory '.solid-quarter-turn-boundary-fixture') -Value 'Own offline scope/envelope checks only. No COM or worker Main.'
& $taskRunner (Join-Path $StageDirectory 'PSBridge.Worker.exe') | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'boundaries.txt')
if($LASTEXITCODE -ne 0){throw 'Solid quarter-turn boundary checks failed.'}
