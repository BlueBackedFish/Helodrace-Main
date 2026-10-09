param(
    [string]$AuditRoot = ('C:\Users\Public\Documents\ESTsoft\CreatorTemp\hd-r7-functional-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string[]]$Cases = @(),
    [switch]$Preview,
    [switch]$ContinueOnFailure
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'R7FunctionalEvidence.ps1')
# Functional queue only. No CPU gate and no claim of whole R7 completion.
$specs = @()
function Add-Case([string]$Name, [string]$Case, [int]$Population = 12, [int]$Ticks = 8000,
    [string]$Workload = 'sapper-wall', [bool]$High = $false, [bool]$Reload = $false,
    [string[]]$Flags = @(), [bool]$Functional = $true, [int]$Reloads = 0, [bool]$Retained = $false,
    [hashtable]$Minimums = @{}) {
    $script:specs += [pscustomobject]@{ name=$Name; case=$Case; population=$Population; ticks=$Ticks;
        workload=$Workload; high=$High; reload=$Reload; flags=$Flags; functional=$Functional; reloads=$Reloads; retained=$Retained; minimums=$Minimums }
}
Add-Case door normal -Workload sapper-door -Minimums @{ newDoorFaults=1 }
Add-Case narrow narrow -Ticks 10000 -Flags @('caseTriggered','r7FixtureActorWithdrawn','r7FixtureContactBeforeWithdrawal') -Minimums @{ newContactResponses=1; newContactResumes=1 }
foreach ($case in @('rocks','interrupt','recovery','cutter','cutter-active-recovery',
    'charge-recovery','charge-fuse-casualty','charge-change','wide-opening','unexpected-hole','inside-goal',
    'tiny-adjacent','room-recovery','door-contact','small-unseen','outdoor-opening','r4-contact-drill')) {
    $flags=@(); $minimums=@{}
    switch ($case) {
        interrupt { $flags=@('caseTriggered'); $minimums=@{ interruptionTick=1 } }
        recovery { $flags=@('caseTriggered'); $minimums=@{ newToolRecoveriesStarted=1; newToolRecoveriesCompleted=1 } }
        cutter { $minimums=@{ newCutterJobsStarted=1 } }
        'cutter-active-recovery' { $flags=@('newActiveCutterRecovered'); $minimums=@{ newCutterJobsStarted=2; newToolRecoveriesCompleted=1 } }
        'charge-recovery' { $flags=@('caseTriggered'); $minimums=@{ newChargesInstalled=1; newChargeDetonations=1; newChargeWaits=1; newToolRecoveriesCompleted=1 } }
        'charge-fuse-casualty' { $flags=@('caseTriggered'); $minimums=@{ newChargesInstalled=1; newChargeDetonations=1; newChargeWaits=1; newChargeOperatorTransfers=1 } }
        'charge-change' { $flags=@('caseTriggered'); $minimums=@{ newChargesInstalled=1; newChargeDetonations=1; newChargeWaits=1 } }
        'unexpected-hole' { $flags=@('caseTriggered','r7FixtureActorWithdrawn','r7FixtureContactBeforeWithdrawal'); $minimums=@{ newContactResponses=1; newContactResumes=1 } }
        'inside-goal' { $flags=@('r7FixtureActorWithdrawn','newDirectObjectiveCleared') }
        'room-recovery' { $flags=@('r7FixtureActorWithdrawn','newRoomRecoveryContinued') }
        'door-contact' { $flags=@('r7FixtureActorWithdrawn','newDoorContactObserved'); $minimums=@{ newSupportThrows=1; newSupportWaits=1 } }
        'small-unseen' { $flags=@('newSmallRoomSupportSaved') }
        'outdoor-opening' { $flags=@('r7FixtureActorWithdrawn','r7FixtureContactBeforeWithdrawal','newOutdoorSmokeSeen','newOutdoorSmokeUsed'); $minimums=@{ newSupportThrows=1; newSupportWaits=1; newSupportReturns=1; newFieldResponses=1; newFieldResumes=1 } }
        'r4-contact-drill' { $flags=@('caseTriggered','newContactDrillComplete','newContactMemoryFrozen','newContactPlanPreserved','newUnseenDoorIgnored'); $minimums=@{ newRearResponses=1; newDoorResponses=1; newOpposedResponses=1; newContactResumes=3; newContactGuardJobs=6 } }
    }
    Add-Case $case $case -Ticks 10000 -Flags $flags -Minimums $minimums
}
Add-Case casualty casualty -Population 13 -High $true -Flags @('caseTriggered','newCasualtyContinued') -Minimums @{ caseLossTick=1 }
$cooperationFlags=@('newCooperationComplete','newCooperationMilestonePreserved')
$cooperationMinimums=@{ newCooperationCompletedTick=1; newAgreementsConfirmed=2; newCooperationStarts=2 }
Add-Case low-cooperation r5-low-coop -Population 24 -Ticks 12000 -Flags $cooperationFlags -Minimums $cooperationMinimums
Add-Case shared-entry r5-shared -Workload sapper-door -Population 24 -Ticks 12000 -Flags $cooperationFlags -Minimums $cooperationMinimums
Add-Case radio-loss r5-radio-loss -Population 26 -High $true -Ticks 14000 -Flags $cooperationFlags -Minimums $cooperationMinimums
foreach ($high in @($false,$true)) {
    $size = 12; $label = 'low'
    if ($high) { $size = 13; $label = 'high' }
    $fieldFlags=@('caseTriggered','newFieldEarlySight','newFieldMotionObserved','newFieldUniquePosts',
        'newFieldSingleTeamBounds','newFieldMemoryFrozen','newFieldMissionResumed')
    Add-Case "field-$label" r6-field-drill -Population $size -High $high -Ticks 10000 -Flags $fieldFlags -Minimums @{ newFieldResponses=1; newFieldResumes=1 }
    Add-Case "smoke-$label" r6-smoke-drill -Population $size -High $high -Ticks 10000 -Flags (
        $fieldFlags + @('newFieldSmokeSharedTargets','newFieldSmokeSeen')) -Minimums @{
        newFieldResponses=1; newFieldResumes=1; newFieldSmokeThrows=2; newFieldSmokeAdvances=2; newFieldShots=1 }
}
$careFlags=@('caseTriggered','newMedicalNoVanillaTend','newMedicalGuardsHeld','newMedicalMissionResumed')
Add-Case care r6-care-drill -Ticks 14000 -Flags ($careFlags + @('newMedicalDressings','newMedicalPlasmaApplied')) -Minimums @{
    newMedicalTreatments=2; newMedicalCompleted=2; newMedicalPlasma=1; newMedicalRejoins=1 }
Add-Case care-interrupt r6-care-interrupt -Ticks 14000 -Flags ($careFlags + 'newMedicalInterruptionSafe') -Minimums @{
    newMedicalTreatments=1; newMedicalAborted=1; newContactShots=1 }
$cleanupFlags = @('r7CleanupComplete','r7IdleStable','r7WorldOrganizationsCleared')
Add-Case cleanup r7-cleanup -Ticks 6000 -Functional $false -Flags $cleanupFlags
foreach ($effect in @('grenade','charge')) {
    Add-Case "cleanup-$effect" "r7-cleanup-$effect" -Ticks 8000 -Functional $false -Flags (
        $cleanupFlags + @('r7EffectSurvivedExit','r7EffectDetonated','r7EffectDrained','r7ExitDuringLiveEffect'))
}
$multiMapFlags = @('r7TwoActualMaps','r7GlobalBudgetShared','r7CrossMapCommunicationBlocked',
    'r7RemovedMapClean','r7RemainingMapPreserved','r7RemainingMapCompleted')
Add-Case multi-map r7-multimap -Flags $multiMapFlags
Add-Case multi-map-scale r7-multimap -Population 48 -Ticks 10000 -Flags $multiMapFlags
Add-Case offer-retirement r7-offer-retirement -Population 24 -Functional $false -Flags @(
    'r7OneSidedOfferSeen','r7RetiredPeerClean','r7OfferSurvivorComplete','r7WorldOrganizationsCleared')
$reloadFlags = @('r7ReloadHistoryPreserved','r7ReloadJobsBound','r7ReloadOpeningPreserved','r7ReloadResponsePreserved')
Add-Case reload-cqb r7-save-load -Ticks 10000 -Reloads 4 -Flags ($reloadFlags + 'r7ReloadLiveGrenadePreserved')
Add-Case reload-retained r7-save-load -Ticks 12000 -Reloads 4 -Retained $true -Flags (
    $reloadFlags + @('r7ReloadLiveGrenadePreserved','r7ReloadRetainedReplanPreserved','r7RetainedReplanResumed'))
Add-Case reload-charge r7-charge-load -Ticks 10000 -Reloads 3 -Flags ($reloadFlags + 'r7ReloadChargePreserved')
Add-Case reload-field r6-smoke-drill -Population 13 -High $true -Reload $true -Reloads 2 -Ticks 10000 -Flags (
    $reloadFlags + $fieldFlags + @('r7ReloadFieldPreserved','r7ReloadToilStatePreserved','newFieldSmokeSharedTargets','newFieldSmokeSeen')) -Minimums @{
    newFieldResponses=1; newFieldResumes=1; newFieldSmokeThrows=2; newFieldSmokeAdvances=2; newFieldShots=1 }
Add-Case reload-care r6-care-drill -Reload $true -Reloads 2 -Ticks 14000 -Flags (
    $reloadFlags + $careFlags + @('r7ReloadCarePreserved','r7ReloadToilStatePreserved','newMedicalDressings','newMedicalPlasmaApplied')) -Minimums @{
    newMedicalTreatments=2; newMedicalCompleted=2; newMedicalPlasma=1; newMedicalRejoins=1 }
$packetFlags = $reloadFlags + @('r7ReloadAgreementPreserved','r7ReloadPacketsPreserved',
    'r7ReloadPacketEndpointsBound','r7ReloadPacketsDelivered')
Add-Case reload-low r5-low-coop -Population 24 -Reload $true -Reloads 2 -Ticks 12000 -Flags ($packetFlags + $cooperationFlags) -Minimums $cooperationMinimums
Add-Case reload-high r5-shared -Workload sapper-door -Population 26 -High $true -Reload $true -Reloads 2 -Ticks 10000 -Flags (
    $packetFlags + $cooperationFlags + 'r7ReloadWornEquipmentPreserved') -Minimums $cooperationMinimums
Add-Case reload-defense r7-defense-transition -Population 24 -Reload $true -Reloads 2 -Ticks 10000 -Functional $false -Flags (
    $reloadFlags + @('r7ReloadDefensePreserved','r7DefenseComplete','r7DefenseAssaultTransition','r7DefenseAssaultComplete'))
Add-Case reload-support r7-support -Reload $true -Reloads 2 -Ticks 14000 -Flags (
    $reloadFlags + @('r7ReloadSupportPreserved','r7MortarRequestBound','r7CasRequestBound','r7GuidanceJobPreserved',
        'r7SupportMissionComplete','r7SupportNoAdvance','r7SupportLongStrikeHeld'))

$selected = @($specs | Where-Object { $Cases.Count -eq 0 -or $Cases -contains $_.name })
foreach ($name in $Cases) { if ($specs.name -notcontains $name) { throw "Unknown R7 case: $name" } }
$launcher = Join-Path $PSScriptRoot 'Run-EngineAudit.ps1'
$allowedCases = (Get-Command $launcher).Parameters['Case'].Attributes |
    Where-Object { $_ -is [Management.Automation.ValidateSetAttribute] } | Select-Object -ExpandProperty ValidValues
foreach ($spec in $specs) { if ($allowedCases -notcontains $spec.case) { throw "Unsupported launcher case: $($spec.case)" } }
if ($Preview) { $selected | ConvertTo-Json -Depth 8; return }
$root = [IO.Path]::GetFullPath($AuditRoot)
if (-not $root.StartsWith('C:\Users\Public\Documents\ESTsoft\CreatorTemp\', [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $root)) { throw 'Use a fresh functional directory under CreatorTemp.' }
if (Get-Process -Name 'RimWorld*' -ErrorAction SilentlyContinue) { throw 'Preserve the existing RimWorld process; do not start another queue.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$content = @('Assemblies\Helodrace.dll','Defs\Organization\NewTacticalJobs.xml',
    'Defs\ColdWar\BreachExplosive_ColdWar.xml','Defs\GreatWar\Sledgehammer_Breach.xml')
$pinned = @{}
foreach ($file in $content) { $pinned[$file] = (Get-FileHash -LiteralPath (Join-Path $repository $file)).Hash.ToLowerInvariant() }
New-Item -ItemType Directory -Path $root | Out-Null
$records = @()
function Write-Journal {
    [pscustomobject]@{ scope='R7 functional regression only'; finalR7Complete=$false; cpuGateEvaluated=$false;
        continueOnFailure=[bool]$ContinueOnFailure; collectionComplete=($records.Count -eq $selected.Count -and @($records | Where-Object { $_.status -eq 'running' }).Count -eq 0);
        failedCases=@($records | Where-Object { $_.status -eq 'failed' } | ForEach-Object { $_.name });
        pinned=$pinned; requestedCases=@($selected.name); fullFunctionalQueue=($Cases.Count -eq 0);
        allSpecifiedPassed=($records.Count -eq $selected.Count -and @($records | Where-Object { $_.status -ne 'passed' }).Count -eq 0);
        records=$records } | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $root 'checks.json')
}
Write-Journal
foreach ($spec in $selected) {
    if (Get-Process -Name 'RimWorld*' -ErrorAction SilentlyContinue) { throw 'Preserve a remaining native process; do not advance the queue.' }
    foreach ($file in $content) {
        if ((Get-FileHash -LiteralPath (Join-Path $repository $file)).Hash.ToLowerInvariant() -ne $pinned[$file]) {
            throw "Content changed during queue; preserve completed runs and restart a new final-code queue: $file"
        }
    }
    $runRoot = Join-Path $root $spec.name
    & $launcher -Engine new -DefaultEngine -Workload $spec.workload -Case $spec.case -Population $spec.population `
        -High:$spec.high -Reload:$spec.reload -RetainedReload:$spec.retained -Speed 3 -WarmupTicks 0 -SampleTicks $spec.ticks -NoMethodProfile `
        -Seed hd-r1-20261007 -AuditRoot $runRoot
    $pidValue = [int](Get-Content -LiteralPath (Join-Path $runRoot 'process-id.txt'))
    $record = [pscustomobject]@{ name=$spec.name; root=$runRoot; pid=$pidValue; status='running'; error=$null;
        actualMinimumSpeed=$null; actualMaximumSpeed=$null; reloads=$null; functionalComplete=$null; nativeExceptions=0 }
    $records += $record; Write-Journal
    $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
    $creation = if ($process) { $process.StartTime } else { $null }
    $deadline = (Get-Date).AddMinutes(30)
    while ($process) {
        if ($process.ProcessName -ne 'RimWorldWin64' -or $process.StartTime -ne $creation) { break }
        if ((Get-Date) -gt $deadline) { throw "Observation deadline reached; preserve live process $pidValue and its running journal. Never restart on timeout." }
        Start-Sleep -Seconds 5
        $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
    }
    try {
        Assert-R7FunctionalRun $spec $runRoot $pinned $record | Out-Null
        $record.status = 'passed'; Write-Journal
        Write-Output "R7 functional passed: $($spec.name)"
    }
    catch {
        $record.status='failed'; $record.error=$_.Exception.Message; Write-Journal
        if (-not $ContinueOnFailure) { throw }
        Write-Warning "R7 functional failed; continuing diagnostic collection: $($spec.name): $($record.error)"
    }
}
$failed = @($records | Where-Object { $_.status -eq 'failed' })
if ($failed.Count) { Write-Warning "Diagnostic collection finished with $($failed.Count) failed cases: $($failed.name -join ', '). This is not a passing regression." }
Write-Output "R7 functional queue complete. No CPU or whole-R7 completion claim: $root"
