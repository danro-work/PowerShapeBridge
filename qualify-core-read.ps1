param(
    [string]$StagingDirectory = (Join-Path $PSScriptRoot 'work\P2.23-core'),
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'evidence\P2.23-core')
)
$ErrorActionPreference = 'Stop'
function Read-SharedHash([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $hasher.Dispose(); $stream.Dispose() }
}
$stagedRoot = [IO.Path]::GetFullPath($StagingDirectory)
if (!$stagedRoot.StartsWith([IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'work')) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Qualification requires project staging.' }
$configuration = Get-Content -LiteralPath (Join-Path $stagedRoot 'bridge.config.json') -Raw | ConvertFrom-Json
if ($configuration.coreProfile -ne 'general-v1') { throw 'General core configuration required.' }
$activeQueue = Join-Path $PSScriptRoot 'releases\P2.6\requests'
if (@(Get-ChildItem -LiteralPath $activeQueue -File | Where-Object { $_.Name -match '\.json$|\.processing$' }).Count -gt 0 -or @(Get-Process -Name PSBridge.Worker -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Runtime queue is not idle.' }
$requestFolder = Join-Path $stagedRoot 'requests'
New-Item -ItemType Directory -Force -Path $requestFolder | Out-Null
$evidenceFolder = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force -Path $evidenceFolder | Out-Null
$protectedPaths = @((Join-Path $PSScriptRoot 'P2_Test_Projects\Test_Block_P2.psmodel'), 'D:\ChatGPT\PowerShapeBridge\Test_Models\Test_Block.step', 'D:\ChatGPT\PowerShapeBridge\Test_Projects\Test_Block.psmodel')
$before = @($protectedPaths | ForEach-Object { @{path=$_;sha256=(Read-SharedHash $_)} })
$results = @()
foreach ($recipe in @('list_documents','inspect_active_model')) {
    $requestId = [Guid]::NewGuid().ToString('N')
    $envelope = @{requestId=$requestId;operation='recipe.prepare';arguments=@{recipeId=$recipe;parameters=@{}}}
    $envelope | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $requestFolder ($requestId+'.json.processing')) -Encoding utf8
    $raw = & (Join-Path $stagedRoot 'PSBridge.Worker.exe') --mcp-operation $requestId
    $raw | Set-Content -LiteralPath (Join-Path $evidenceFolder ($recipe+'.json')) -Encoding utf8
    $result = $raw | ConvertFrom-Json
    Move-Item -LiteralPath (Join-Path $requestFolder ($requestId+'.json.processing')) -Destination (Join-Path $requestFolder ($requestId+'.json.processing.done'))
    if ($result.state -notin @('documents_listed_verified','succeeded_verified')) { throw "Read failed: $raw" }
    $results += @{recipe=$recipe;requestId=$requestId;state=$result.state;bridgeVersion=$result.bridgeVersion}
}
$after = @($protectedPaths | ForEach-Object { @{path=$_;sha256=(Read-SharedHash $_)} })
for ($index=0; $index -lt $before.Count; $index++) { if ($before[$index].sha256 -ne $after[$index].sha256) { throw 'Protected input changed.' } }
@{timestampUtc=[DateTime]::UtcNow.ToString('o');profile='general-v1';results=$results;protectedFiles=$after;protectedHashesUnchanged=$true;cadMutationsAttempted=$false;runtimeDeploymentPerformed=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceFolder 'qualification.json') -Encoding utf8
Write-Output 'General core native reads passed; protected input hashes unchanged; runtime not deployed.'
