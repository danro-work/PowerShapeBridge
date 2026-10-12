param(
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [string]$ComparePath
)
$ErrorActionPreference = 'Stop'
$paths = @(
    (Join-Path $PSScriptRoot 'P2_Test_Projects\Test_Block_P2.psmodel'),
    (Join-Path (Split-Path $PSScriptRoot -Parent) 'Test_Models\Test_Block.step'),
    (Join-Path (Split-Path $PSScriptRoot -Parent) 'Test_Projects\Test_Block.psmodel')
)
$files = @()
foreach ($path in $paths) {
    $stream = [IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $digest = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $stream.Dispose(); $hash.Dispose() }
    $files += @{path=$path;sha256=$digest}
}
$matched = $null
if ($ComparePath) {
    $previous = Get-Content -LiteralPath $ComparePath -Raw | ConvertFrom-Json
    $matched = $previous.files.Count -eq $files.Count
    foreach ($file in $files) {
        $expected = @($previous.files | Where-Object { $_.path -eq $file.path })
        if ($expected.Count -ne 1 -or $expected[0].sha256 -ne $file.sha256) { $matched=$false }
    }
}
@{timestampUtc=[DateTime]::UtcNow.ToString('o');scope='Shared read of protected files only; not proof of in-memory CAD topology';files=$files;matchesPrevious=$matched;cadConnectionAttempted=$false} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
if ($matched -eq $false) { throw 'Protected file hashes changed.' }
Write-Output 'Protected file hashes recorded; no CAD file writes.'
