param(
    [string]$StagingDirectory = (Join-Path $PSScriptRoot 'work\P2.27-copies'),
    [string]$EvidencePath = (Join-Path $PSScriptRoot 'evidence\P2.27-copies\junction-preflight.json')
)
$ErrorActionPreference = 'Stop'
$stage = [IO.Path]::GetFullPath($StagingDirectory)
if (!$stage.StartsWith((Join-Path $PSScriptRoot 'work')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Only staging may run this file test.' }
if (@(Get-Process -Name PSBridge.Worker -ErrorAction SilentlyContinue).Count -gt 0) { throw 'A worker is active.' }
if (@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'releases\P2.6\requests') -File | Where-Object { $_.Name -match '\.json$|\.processing$' }).Count -gt 0) { throw 'Runtime queue is pending.' }
$fixture = Join-Path $PSScriptRoot ('work\copy-path-test-'+[guid]::NewGuid().ToString('N'))
$real = Join-Path $fixture 'real'
New-Item -ItemType Directory -Path $real | Out-Null
$source = Join-Path $real 'Source.psmodel'
Set-Content -LiteralPath $source -Value 'Opaque filesystem test fixture; not CAD.'
$link = Join-Path $fixture 'junction'
New-Item -ItemType Junction -Path $link -Target $real | Out-Null
$id = [guid]::NewGuid().ToString('N')
$requestFolder = Join-Path $stage 'requests'
New-Item -ItemType Directory -Force -Path $requestFolder | Out-Null
$requestPath = Join-Path $requestFolder ($id+'.json.processing')
@{requestId=$id;operation='recipe.prepare';arguments=@{recipeId='prepare_working_copy';parameters=@{sourcePath=(Join-Path $link 'Source.psmodel');sourceSha256=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()}}} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $requestPath -Encoding utf8
$raw = & (Join-Path $stage 'PSBridge.Worker.exe') --mcp-operation $id
if ($LASTEXITCODE -ne 0) { throw 'Native file test worker failed.' }
$result = $raw | ConvertFrom-Json
if ($result.state -ne 'working_copy_rejected' -or $result.error -notmatch 'Reparse points/junctions' -or $result.powerShapeConnectionAttempted -or (Test-Path -LiteralPath (Join-Path $stage ('workingcopies\'+$id)))) { throw 'Junction was not rejected before copy creation.' }
Move-Item -LiteralPath $requestPath -Destination ($requestPath+'.done')
@{state='passed';junctionRejectedBeforeCopy=$true;nativeResult=$result;fixture=$fixture;cadConnectionAttempted=$false} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $EvidencePath -Encoding utf8
Write-Output 'PASS: real junction rejected before any copy or CAD connection.'
