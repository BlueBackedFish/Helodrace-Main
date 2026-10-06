param(
    [string]$AuditRoot = ('C:\Users\Public\Documents\ESTsoft\CreatorTemp\hd-profile-repeat-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [ValidateRange(2,10)][int]$Repeats = 3,
    [string]$Cases = '16,17,18,19,15',
    [int]$WarmupTicks = 600,
    [int]$SampleTicks = 1200,
    [string]$Seed = 'hd-perf-20261007',
    [switch]$High,
    [string]$AssemblyDirectory = '',
    [string]$ProfileTargets = '',
    [ValidateSet('coarse','detailed')][string]$ProfilePreset = 'coarse'
)
$ErrorActionPreference = 'Stop'
$auditScript = Join-Path $PSScriptRoot '..\RaidMovementAudit\Run-GameAudit.ps1'
$cli = Join-Path $PSScriptRoot '..\..\Tools\AgentProfiler\bin\Debug\net10.0\AgentProfiler.dll'
for ($run = 1; $run -le $Repeats; $run++) {
    $runRoot = Join-Path $AuditRoot "run-$run"
    & $auditScript -AuditRoot $runRoot -Cases $Cases -WarmupTicks $WarmupTicks -SampleTicks $SampleTicks -Seed $Seed -High:$High -AssemblyDirectory $AssemblyDirectory -MethodProfile -ProfileTargets $ProfileTargets -ProfilePreset $ProfilePreset
    $auditPid = [int](Get-Content -LiteralPath (Join-Path $runRoot 'process-id.txt'))
    $deadline = (Get-Date).AddMinutes(30)
    while (Get-Process -Id $auditPid -ErrorAction SilentlyContinue) {
        if ((Get-Date) -gt $deadline) { throw "Audit $auditPid exceeded 30 minutes; preserve the process/logs for diagnosis." }
        Start-Sleep -Seconds 5
    }
    $measurements = @(Get-Content -LiteralPath (Join-Path $runRoot 'measurements.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
    if ($measurements.error -or -not ($measurements | Where-Object complete)) { throw "Audit $run failed; inspect $runRoot." }
}
$aggregate = & dotnet $cli aggregate $AuditRoot
if ($LASTEXITCODE -ne 0) { throw ($aggregate -join "`n") }
$aggregate | Set-Content -LiteralPath (Join-Path $AuditRoot 'aggregate.json') -Encoding UTF8
Write-Output "Repeated benchmark complete: $AuditRoot\aggregate.json"
