param(
    [string]$AuditRoot = ('C:\Users\Public\Documents\ESTsoft\CreatorTemp\hd-profile-repeat-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [ValidateRange(2,10)][int]$Repeats = 3,
    [string]$Cases = '16,17,18,19,15',
    [int]$WarmupTicks = 600,
    [int]$SampleTicks = 1200,
    [string]$Seed = 'hd-perf-20261007',
    [switch]$High,
    [switch]$Resume,
    [string]$AssemblyDirectory = '',
    [string]$ProfileTargets = '',
    [ValidateSet('coarse','detailed','spikes','pawn-spikes')][string]$ProfilePreset = 'coarse'
)
$ErrorActionPreference = 'Stop'
$auditScript = Join-Path $PSScriptRoot '..\RaidMovementAudit\Run-GameAudit.ps1'
$cli = Join-Path $PSScriptRoot '..\..\Tools\AgentProfiler\bin\Debug\net10.0\AgentProfiler.dll'
for ($run = 1; $run -le $Repeats; $run++) {
    $runRoot = Join-Path $AuditRoot "run-$run"
    if ($Resume -and (Test-Path -LiteralPath $runRoot)) {
        $existing = @(Get-Content -LiteralPath (Join-Path $runRoot 'measurements.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
        if (@($existing | Where-Object { $_.error }).Count -gt 0 -or -not ($existing | Where-Object { $_.complete -eq $true })) {
            throw "Cannot resume incomplete audit $runRoot; use a new root to preserve it."
        }
        $oldProfiles = @(Get-ChildItem -LiteralPath (Join-Path $runRoot 'profiles') -Filter 'capture-*.json' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
        $expectedAssembly = if ($AssemblyDirectory) { Join-Path $AssemblyDirectory 'Helodrace.dll' } else { Join-Path $PSScriptRoot '..\..\..\Assemblies\Helodrace.dll' }
        $expectedHash = (Get-FileHash -LiteralPath $expectedAssembly).Hash.ToLowerInvariant()
        $existingScenarios = ($oldProfiles.scenario | Sort-Object) -join ','
        $expectedScenarios = ($Cases -split ',' | ForEach-Object {[int]$_} | Sort-Object) -join ','
        $expectedFaction = if ($High) { 'HD_HelodCivilHighFaction' } else { 'HD_HelodCivilLowFaction' }
        $invalidProfiles = @($oldProfiles | Where-Object { $_.assemblySha256 -ne $expectedHash -or $_.benchmark.seed -ne $Seed -or $_.benchmark.warmupTicks -ne $WarmupTicks -or $_.benchmark.sampleTicks -ne $SampleTicks -or $_.benchmark.faction -ne $expectedFaction })
        if ($existingScenarios -ne $expectedScenarios -or $invalidProfiles.Count -gt 0) {
            throw "Resume conditions differ in $runRoot."
        }
        Write-Output "Reusing completed audit $runRoot"; continue
    }
    & $auditScript -AuditRoot $runRoot -Cases $Cases -WarmupTicks $WarmupTicks -SampleTicks $SampleTicks -Seed $Seed -High:$High -AssemblyDirectory $AssemblyDirectory -MethodProfile -ProfileTargets $ProfileTargets -ProfilePreset $ProfilePreset
    $auditPid = [int](Get-Content -LiteralPath (Join-Path $runRoot 'process-id.txt'))
    $deadline = (Get-Date).AddMinutes(30)
    while (Get-Process -Id $auditPid -ErrorAction SilentlyContinue) {
        if ((Get-Date) -gt $deadline) { throw "Audit $auditPid exceeded 30 minutes; preserve the process/logs for diagnosis." }
        Start-Sleep -Seconds 5
    }
    $measurements = @(Get-Content -LiteralPath (Join-Path $runRoot 'measurements.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
    if (@($measurements | Where-Object { $_.error }).Count -gt 0 -or -not ($measurements | Where-Object { $_.complete -eq $true })) { throw "Audit $run failed; inspect $runRoot." }
}
$aggregate = & dotnet $cli aggregate $AuditRoot
if ($LASTEXITCODE -ne 0) { throw ($aggregate -join "`n") }
$aggregate | Set-Content -LiteralPath (Join-Path $AuditRoot 'aggregate.json') -Encoding UTF8
Write-Output "Repeated benchmark complete: $AuditRoot\aggregate.json"
