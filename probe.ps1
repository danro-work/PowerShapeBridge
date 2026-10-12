$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bridge.config.json') -Raw | ConvertFrom-Json
$reports = Join-Path $PSScriptRoot 'reports'
New-Item -ItemType Directory -Force -Path $reports | Out-Null
$id = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')
$out = Join-Path $reports "probe-$id.json"
$err = Join-Path $reports "probe-$id.stderr.txt"
$worker = Start-Process -FilePath (Join-Path $PSScriptRoot 'PSBridge.Worker.exe') -ArgumentList '--probe' -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
if (!$worker.WaitForExit([int]$config.timeoutSeconds * 1000)) {
    $worker.Kill()
    $worker.WaitForExit()
    @{state='timeout_outcome_unknown'; error=@{code='worker_timeout'; message='Only the Bridge worker was stopped. PowerShape was not closed.'}} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $out -Encoding UTF8
}
Get-Content -LiteralPath $out -Raw
Write-Output "Report: $out"
