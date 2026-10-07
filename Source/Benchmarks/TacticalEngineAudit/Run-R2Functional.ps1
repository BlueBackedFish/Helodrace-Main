param(
    [string]$AuditRoot = ('C:\Users\Public\Documents\ESTsoft\CreatorTemp\hd-r2-functional-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [ValidateSet('door','narrow','rocks','interrupt','casualty','crowd200')][string[]]$Cases = @('door','narrow','rocks','interrupt','casualty','crowd200')
)
$ErrorActionPreference = 'Stop'
$records = @()
$scenarios = @(
    @{Name='door'; Workload='sapper-door'; Case='normal'; Population=12; High=$false; Ticks=3000},
    @{Name='narrow'; Workload='sapper-wall'; Case='narrow'; Population=12; High=$false; Ticks=4000},
    @{Name='rocks'; Workload='sapper-wall'; Case='rocks'; Population=12; High=$false; Ticks=4000},
    @{Name='interrupt'; Workload='sapper-wall'; Case='interrupt'; Population=12; High=$false; Ticks=4000},
    @{Name='casualty'; Workload='sapper-wall'; Case='casualty'; Population=13; High=$true; Ticks=5000},
    @{Name='crowd200'; Workload='sapper-wall'; Case='normal'; Population=200; High=$false; Ticks=10000}
)
foreach ($case in $scenarios) {
    if ($Cases -notcontains $case.Name) { continue }
    $runRoot = Join-Path $AuditRoot $case.Name
    & (Join-Path $PSScriptRoot 'Run-EngineAudit.ps1') -Engine new -Workload $case.Workload -Case $case.Case -Population $case.Population -WarmupTicks 0 -SampleTicks $case.Ticks -High:$case.High -Seed hd-r2-20261007 -AuditRoot $runRoot -ProfileTargets 'Verse.AI.Pawn_JobTracker::StartJob;Verse.PathFinder::CreateRequest;Helodrace.Tactics.MapComponent_TacticalCommands::Advance;Helodrace.Tactics.TacticalLocalPlanner::Find;Helodrace.Tactics.MapComponent_TacticalCommands::Issue'
    $runPid = [int](Get-Content -LiteralPath (Join-Path $runRoot 'process-id.txt'))
    $deadline = (Get-Date).AddMinutes(30)
    while (Get-Process -Id $runPid -ErrorAction SilentlyContinue) {
        if ((Get-Date) -gt $deadline) { throw "Timed out; preserve audit process $runPid." }
        Start-Sleep -Seconds 5
    }
    $audit = Get-Content -LiteralPath (Join-Path $runRoot 'audit.json') -Raw | ConvertFrom-Json
    $passed = $audit.complete -and $audit.isolationVerified -and -not $audit.error -and $audit.newFunctionalComplete
    if ($case.Name -eq 'door') { $passed = $passed -and $audit.newDoorFaults -eq 1 }
    if ($case.Name -eq 'interrupt') { $passed = $passed -and $audit.caseTriggered -and -not $audit.returnedOutsideAfterInterruption }
    if ($case.Name -eq 'casualty') { $passed = $passed -and $audit.caseTriggered -and $audit.alive -eq 12 }
    $records += @{name=$case.Name; root=$runRoot; passed=[bool]$passed; actual=$audit.population; completed=$audit.newCompletedUnits; failures=$audit.newJobFailures}
    $records | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $AuditRoot 'checks.json')
    Write-Output "R2 $($case.Name): passed=$passed completed=$($audit.newCompletedUnits)/$($audit.units) failures=$($audit.newJobFailures)"
    if (-not $passed) { throw "R2 functional failure: $runRoot" }
}
