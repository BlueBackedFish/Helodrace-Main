param(
    [ValidateRange(1,10)][int]$Repeats = 3,
    [ValidateSet('vanilla','new')][string[]]$Engines = @('vanilla','new'),
    [ValidateSet('open-approach','sapper-wall','sapper-door')][string[]]$Workloads = @('open-approach','sapper-wall'),
    [ValidateRange(0,400)][int]$Population = 50,
    [int]$WarmupTicks = 600,
    [int]$SampleTicks = 1200,
    [switch]$High,
    [switch]$NoMethodProfile,
    [string]$ProfileTargets = 'Verse.AI.Pawn_JobTracker::StartJob;Verse.PathFinder::CreateRequest',
    [string]$AuditRoot = ('C:\Users\Public\Documents\ESTsoft\CreatorTemp\hd-engine-matrix-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$Seed = 'hd-r1-20261007'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($AuditRoot)
if (-not $root.StartsWith('C:\Users\Public\Documents\ESTsoft\CreatorTemp\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)) { throw 'Use a fresh matrix directory under CreatorTemp.' }
$records = @()
foreach ($workload in $Workloads) {
    for ($repeat=1; $repeat -le $Repeats; $repeat++) {
        # Rotate order to avoid always warming the machine with the same engine.
        for ($slot=0; $slot -lt $Engines.Count; $slot++) {
            $engine = $Engines[($slot + $repeat - 1) % $Engines.Count]
            $runRoot = Join-Path $root "$workload\$engine\run-$repeat"
            & (Join-Path $PSScriptRoot 'Run-EngineAudit.ps1') -Engine $engine -Workload $workload -Population $Population -WarmupTicks $WarmupTicks -SampleTicks $SampleTicks -High:$High -NoMethodProfile:$NoMethodProfile -ProfileTargets $ProfileTargets -Seed $Seed -AuditRoot $runRoot
            $auditProcess = [int](Get-Content -LiteralPath (Join-Path $runRoot 'process-id.txt'))
            $deadline = (Get-Date).AddMinutes(30)
            while (Get-Process -Id $auditProcess -ErrorAction SilentlyContinue) {
                if ((Get-Date) -gt $deadline) { throw "Audit $auditProcess exceeded 30 minutes; preserve the live process/logs for diagnosis." }
                Start-Sleep -Seconds 5
            }
            $audit = Get-Content -LiteralPath (Join-Path $runRoot 'audit.json') -Raw | ConvertFrom-Json
            if (-not $audit.complete -or -not $audit.isolationVerified -or $audit.error) { throw "Audit failed: $runRoot" }
            if ($engine -eq 'new' -and $SampleTicks -ge 1000 -and -not $audit.newFunctionalComplete) { throw "New functional progress failed; preserve this run: $runRoot" }
            $records += @{ root=$runRoot; engine=$engine; workload=$workload; repeat=$repeat; population=$audit.population; entered=$audit.entered; objectiveReached=$audit.objectiveReached }
            $records | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $root 'matrix.json')
            Write-Output "Completed $engine/$workload repeat=$repeat actual=$($audit.population) entered=$($audit.entered) reached=$($audit.objectiveReached)"
        }
    }
}
$records | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $root 'matrix.json')
Write-Output "Engine matrix complete: $root"
