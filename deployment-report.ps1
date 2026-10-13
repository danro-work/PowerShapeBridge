# Read-only heartbeat observation. Sharing retries belong to the readiness loop,
# never to deployment copying, panel restart or any CAD command.
function Read-BridgeDeploymentReport([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return $null }
    $stream=$null
    $reader=$null
    try {
        $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
        $reader=[IO.StreamReader]::new($stream)
        return ($reader.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop)
    } catch [IO.IOException] {
        $code=$_.Exception.GetBaseException().HResult -band 0xffff
        if ($code -in @(32,33)) { return $null }
        throw
    } finally {
        if ($reader) { $reader.Dispose() } elseif ($stream) { $stream.Dispose() }
    }
}
