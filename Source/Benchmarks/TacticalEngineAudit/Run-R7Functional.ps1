param(
    [string]$AuditRoot = ('C:\Users\Public\Documents\ESTsoft\CreatorTemp\hd-r7-functional-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string[]]$Cases = @(),
    [switch]$Preview
)
$ErrorActionPreference = 'Stop'
# Functional queue only. No CPU gate and no claim of whole R7 completion.
$specs = @()
function Add-Case([string]$Name, [string]$Case, [int]$Population = 12, [int]$Ticks = 8000,
    [string]$Workload = 'sapper-wall', [bool]$High = $false, [bool]$Reload = $false,
    [string[]]$Flags = @(), [bool]$Functional = $true, [int]$Reloads = 0, [bool]$Retained = $false) {
    $script:specs += [pscustomobject]@{ name=$Name; case=$Case; population=$Population; ticks=$Ticks;
        workload=$Workload; high=$High; reload=$Reload; flags=$Flags; functional=$Functional; reloads=$Reloads; retained=$Retained }
}
Add-Case door normal -Workload sapper-door
Add-Case narrow narrow -Ticks 10000 -Flags @('caseTriggered','r7FixtureActorWithdrawn','r7FixtureContactBeforeWithdrawal')
foreach ($case in @('rocks','interrupt','recovery','cutter','cutter-active-recovery',
    'charge-recovery','charge-fuse-casualty','charge-change','wide-opening','unexpected-hole','inside-goal',
    'tiny-adjacent','room-recovery','door-contact','small-unseen','outdoor-opening','r4-contact-drill')) {
    Add-Case $case $case -Ticks 10000
}
Add-Case casualty casualty -Population 13 -High $true -Flags caseTriggered
Add-Case low-cooperation r5-low-coop -Population 24 -Ticks 12000
Add-Case shared-entry r5-shared -Population 24 -Ticks 12000
Add-Case radio-loss r5-radio-loss -Population 26 -High $true -Ticks 14000
foreach ($high in @($false,$true)) {
    $size = 12; $label = 'low'
    if ($high) { $size = 13; $label = 'high' }
    Add-Case "field-$label" r6-field-drill -Population $size -High $high -Ticks 10000
    Add-Case "smoke-$label" r6-smoke-drill -Population $size -High $high -Ticks 10000
}
Add-Case care r6-care-drill -Ticks 14000
Add-Case care-interrupt r6-care-interrupt -Ticks 14000
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
    $reloadFlags + @('r7ReloadFieldPreserved','r7ReloadToilStatePreserved'))
Add-Case reload-care r6-care-drill -Reload $true -Reloads 2 -Ticks 14000 -Flags (
    $reloadFlags + @('r7ReloadCarePreserved','r7ReloadToilStatePreserved'))
$packetFlags = $reloadFlags + @('r7ReloadAgreementPreserved','r7ReloadPacketsPreserved',
    'r7ReloadPacketEndpointsBound','r7ReloadPacketsDelivered')
Add-Case reload-low r5-low-coop -Population 24 -Reload $true -Reloads 2 -Ticks 12000 -Flags $packetFlags
Add-Case reload-high r5-shared -Population 26 -High $true -Reload $true -Reloads 2 -Ticks 10000 -Flags (
    $packetFlags + 'r7ReloadWornEquipmentPreserved')
Add-Case reload-defense r7-defense-transition -Reload $true -Reloads 2 -Ticks 10000 -Functional $false -Flags (
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
        pinned=$pinned; requestedCases=@($selected.name); fullFunctionalQueue=($Cases.Count -eq 0);
        allSpecifiedPassed=($records.Count -eq $selected.Count -and @($records | Where-Object { $_.status -ne 'passed' }).Count -eq 0);
        records=$records } | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $root 'checks.json')
}
Write-Journal
foreach ($spec in $selected) {
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
        $audit = Get-Content -Encoding UTF8 -LiteralPath (Join-Path $runRoot 'audit.json') -Raw | ConvertFrom-Json
        $manifest = Get-Content -Encoding UTF8 -LiteralPath (Join-Path $runRoot 'launcher.json') -Raw | ConvertFrom-Json
        $record.actualMinimumSpeed = $audit.r7TickRateMinimum; $record.actualMaximumSpeed = $audit.r7TickRateMaximum
        $record.reloads = $audit.r7Reloads; $record.functionalComplete = $audit.newFunctionalComplete
        $record.nativeExceptions = @(Select-String -LiteralPath (Join-Path $runRoot 'Player.log') -Pattern 'Exception:|Exception while|Error in ' -Encoding UTF8).Count
        # Loading can produce a normal-speed sample even with forced combat
        # slowdown disabled. Preserve actual rates; this queue never measures
        # the CPU gate. Non-reload cases still require the requested actual3x.
        $speedValid = $audit.speed -eq 3 -and $audit.r7RateSamples -gt 0 -and
            $audit.r7TickRateMinimum -ge 1 -and $audit.r7TickRateMaximum -le 3 -and
            ($spec.reloads -gt 0 -or ($audit.r7TickRateMinimum -eq 3 -and $audit.r7TickRateMaximum -eq 3))
        if (-not $audit.complete -or $audit.error -or -not $audit.isolationVerified -or -not $audit.environmentControlled -or
            -not $audit.r7RetiredTypesAbsent -or -not $audit.r7RetiredDefinitionsAbsent -or
            $manifest.assemblySha256 -ne $pinned['Assemblies\Helodrace.dll'] -or $manifest.methodProfile -or
            $audit.newUnsafeEntries -ne 0 -or -not $speedValid -or $record.nativeExceptions -gt 0) {
            throw 'Native run, content, environment, retirement, speed or safety check failed.'
        }
        if ($spec.functional -and -not $audit.newFunctionalComplete) { throw 'Full native functional completion failed.' }
        foreach ($flag in $spec.flags) { if ($audit.$flag -ne $true) { throw "Required native proof missing: $flag" } }
        if ($spec.name -eq 'narrow' -and ($audit.newContactResponses -lt 1 -or $audit.newContactResumes -lt 1)) {
            throw 'Narrow fixture must actually respond to its observed defender and resume after withdrawal.'
        }
        if ($audit.r7Reloads -lt $spec.reloads) { throw 'Required actual save/load count missing.' }
        $record.status = 'passed'; Write-Journal
        Write-Output "R7 functional passed: $($spec.name)"
    }
    catch { $record.status='failed'; $record.error=$_.Exception.Message; Write-Journal; throw }
}
Write-Output "R7 functional queue complete. No CPU or whole-R7 completion claim: $root"
