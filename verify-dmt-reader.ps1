param(
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [string]$StageDirectory=(Join-Path $PSScriptRoot 'work\dmt-reader'),
    [string]$EvidenceDirectory=(Join-Path $PSScriptRoot 'evidence\K8-dmt-reader\framework')
)
$ErrorActionPreference='Stop'
$taskStage=[IO.Path]::GetFullPath($StageDirectory)
if(!$taskStage.StartsWith((Join-Path $PSScriptRoot 'work')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Own project work stage required.'}
if(Test-Path -LiteralPath $taskStage){throw 'Fresh DMT reader stage required.'}
New-Item -ItemType Directory -Path $taskStage | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskBinary=Join-Path $taskStage 'ReadDmtMesh.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /reference:System.Core.dll /reference:System.Web.Extensions.dll "/out:$taskBinary" (Join-Path $PSScriptRoot 'src\DmtMeshReader.cs') (Join-Path $PSScriptRoot 'tools\ReadDmtMesh.cs')
if($LASTEXITCODE -ne 0){throw 'Framework DMT reader compilation failed.'}
& $PythonPath (Join-Path $PSScriptRoot 'src\qualify_dmt_reader.py') --binary $taskBinary --fixtures (Join-Path $PSScriptRoot 'tests\PowerShapeMcp.Tests\fixtures\dmt') --stage (Join-Path $taskStage 'fixtures') --evidence $EvidenceDirectory
if($LASTEXITCODE -ne 0){throw 'Independent DMT reader qualification failed.'}
