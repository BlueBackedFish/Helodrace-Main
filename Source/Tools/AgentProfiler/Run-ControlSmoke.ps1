param([Parameter(Mandatory=$true)][string]$ProfileRoot)
$ErrorActionPreference = 'Stop'
$cli = Join-Path $PSScriptRoot 'bin\Debug\net10.0\AgentProfiler.dll'
if (-not (Test-Path -LiteralPath $cli)) { throw 'Build AgentProfiler.csproj first.' }
$wait = [Diagnostics.Stopwatch]::StartNew()
while (-not (Test-Path -LiteralPath (Join-Path $ProfileRoot 'status.json'))) {
    if ($wait.Elapsed.TotalSeconds -gt 180) { throw 'Profiler handshake timed out.' }
    Start-Sleep -Milliseconds 250
}
function Invoke-Profiler([string[]]$Arguments) {
    $response = & dotnet $cli @Arguments
    if ($LASTEXITCODE -ne 0) { throw ($response -join "`n") }
    $value = ($response -join "`n") | ConvertFrom-Json
    $value | ConvertTo-Json -Depth 20 -Compress | Add-Content -Encoding UTF8 -LiteralPath (Join-Path $ProfileRoot 'control-smoke.ndjson')
    return $value
}
$initial = Invoke-Profiler @('status', $ProfileRoot)
if ($initial.state -ne 'idle') { throw 'Run the control smoke during audit warmup, before auto capture starts.' }
$started = Invoke-Profiler @('start', $ProfileRoot, '2', 'cpu')
if ($started.state -ne 'capturing') { throw 'Capture failed to start.' }
Start-Sleep -Milliseconds 300
$stopped = Invoke-Profiler @('stop', $ProfileRoot)
if ($stopped.state -ne 'idle' -or -not $stopped.latestCapture) { throw 'Capture failed to stop.' }
$first = $stopped.latestCapture
$started = Invoke-Profiler @('start', $ProfileRoot, '1', 'cpu')
Start-Sleep -Milliseconds 1500
$finished = Invoke-Profiler @('status', $ProfileRoot)
if ($finished.state -ne 'idle' -or $finished.latestCapture -eq $first) { throw 'Duration limit failed.' }
Write-Output "Profiler command smoke passed: status, start, explicit stop, automatic stop; $ProfileRoot\control-smoke.ndjson"
