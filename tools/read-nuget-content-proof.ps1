param(
    [Parameter(Mandatory=$true)][string]$Nupkg,
    [Parameter(Mandatory=$true)][string]$LockedContentHash,
    [Parameter(Mandatory=$true)][string]$NugetSdkDirectory
)
$ErrorActionPreference='Stop'
# Signed NuGet content hashes differ from whole signed ZIP hashes. Use the
# installed NuGet implementation, and validate content integrity offline.
foreach ($taskLibrary in @('NuGet.Common.dll','NuGet.Frameworks.dll','NuGet.Versioning.dll','System.Security.Cryptography.Pkcs.dll')) {
    $taskLibraryPath=Join-Path $NugetSdkDirectory $taskLibrary
    if (Test-Path -LiteralPath $taskLibraryPath) { [Reflection.Assembly]::LoadFrom($taskLibraryPath) | Out-Null }
}
$taskPackagingPath=Join-Path $NugetSdkDirectory 'NuGet.Packaging.dll'
$taskPackaging=[Reflection.Assembly]::LoadFrom($taskPackagingPath)
$taskReaderType=$taskPackaging.GetType('NuGet.Packaging.PackageArchiveReader',$true)
$taskStream=[IO.File]::Open([IO.Path]::GetFullPath($Nupkg),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
$taskReader=$null
try {
    $taskConstructor=$taskReaderType.GetConstructor([Type[]]@([IO.Stream]))
    if ($null -eq $taskConstructor) { throw 'Expected NuGet Stream reader constructor unavailable.' }
    $taskReader=$taskConstructor.Invoke([object[]]@($taskStream))
    $taskContentHash=$taskReader.GetContentHash([Threading.CancellationToken]::None,$null)
    if ($taskContentHash -cne $LockedContentHash) { throw 'NuGet content differs from packages.lock.json.' }
    $taskSignature=$taskReader.GetPrimarySignatureAsync([Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $taskSigned=$null -ne $taskSignature
    if ($taskSigned) { $taskReader.ValidateIntegrityAsync($taskSignature.SignatureContent,[Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null }
    [ordered]@{
        state='passed'
        contentHash=$taskContentHash
        contentHashMethod='NuGet.Packaging.PackageArchiveReader.GetContentHash'
        signed=$taskSigned
        signatureContentIntegrityVerified=$taskSigned
        certificateTrustChecked=$false
        networkAccessAttempted=$false
        nugetPackagingAssemblySha256=(Get-FileHash -LiteralPath $taskPackagingPath).Hash.ToLowerInvariant()
        rawArchiveSha256=(Get-FileHash -LiteralPath $Nupkg).Hash.ToLowerInvariant()
    } | ConvertTo-Json -Compress
} finally {
    if ($null -ne $taskReader) { $taskReader.Dispose() }
    $taskStream.Dispose()
}
