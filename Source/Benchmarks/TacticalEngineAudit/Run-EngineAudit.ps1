param(
    [ValidateSet('vanilla','new')][string]$Engine = 'vanilla',
    [ValidateSet('open-approach','sapper-wall','sapper-door')][string]$Workload = 'open-approach',
    [ValidateRange(0,400)][int]$Population = 50,
    [ValidateSet(1,3)][int]$Speed = 3,
    [ValidateRange(0,10000)][int]$WarmupTicks = 600,
    [ValidateRange(1,20000)][int]$SampleTicks = 1200,
    [string]$Seed = 'hd-r1-20261007',
    [ValidateSet('normal','interrupt','casualty','rocks','narrow','contact','field','recovery','cutter','cutter-recovery','charge-recovery','charge-fuse-casualty','charge-change','multiroom','wide-opening','unexpected-hole','inside-goal','door-contact','outdoor-opening','small-unseen','room-recovery','cutter-active-recovery','tiny-adjacent','r4-contact-drill','r5-low-coop','r5-shared','r5-radio-loss','r6-field-drill','r6-smoke-drill','r6-field-cpu','r6-care-drill','r6-care-interrupt','r7-cqb-cpu','r7-cleanup','r7-cleanup-grenade','r7-cleanup-charge','r7-save-load','r7-charge-load','r7-multimap','r7-defense','r7-defense-transition','r7-offer-retirement','r7-support')][string]$Case = 'normal',
    [switch]$High,
    [switch]$NoMethodProfile,
    [switch]$Headless,
    [switch]$Reload,
    [switch]$DefaultEngine,
    [ValidateSet('coarse','detailed','spikes','pawn-spikes','needs-spikes','jobs-spikes','path-spikes')][string]$ProfilePreset = 'coarse',
    [ValidateRange(0.1,1000)][double]$SpikeThresholdMs = 5,
    [ValidateRange(0,2147483647)][Nullable[int]]$SpikePawnId = $null,
    [string]$ProfileTargets = 'Verse.AI.Pawn_JobTracker::StartJob;Verse.PathFinder::CreateRequest',
    [string]$AuditRoot = ('C:\Users\Public\Documents\ESTsoft\CreatorTemp\hd-engine-audit-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
)
$ErrorActionPreference = 'Stop'
if ($DefaultEngine -and $Engine -ne 'new') { throw 'Default-engine verification requires the new engine.' }
if (($Reload -or $Case -in @('r7-save-load','r7-charge-load')) -and -not $NoMethodProfile) { throw 'Native save/load audits require NoMethodProfile; a method capture cannot span game reloads.' }
if ($Reload -and $Case -notin @('r6-field-drill','r6-smoke-drill','r6-care-drill','r5-low-coop','r5-shared','r7-defense','r7-defense-transition','r7-support')) { throw 'Optional reload supports field/smoke/care and cooperation/defense functional drills only.' }
if ($Headless -and (-not $NoMethodProfile -or -not $Case.StartsWith('r7-') -and -not $Reload)) { throw 'Headless mode is only for unprofiled R7 functional audits, not CPU comparisons.' }
if (Get-Process -Name 'RimWorld*' -ErrorAction SilentlyContinue) { throw 'An existing RimWorld process is running. Preserve it and run the isolated audit after it exits.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$auditPath = [IO.Path]::GetFullPath($AuditRoot)
if (-not $auditPath.StartsWith('C:\Users\Public\Documents\ESTsoft\CreatorTemp\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a new audit directory under CreatorTemp.' }
if (Test-Path -LiteralPath $auditPath) { throw 'Audit directory already exists; preserve previous data.' }
New-Item -ItemType Directory -Path (Join-Path $auditPath 'Config') -Force | Out-Null
@'
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData><version>1.6.4871 rev590</version><activeMods><li>brrainz.harmony</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>bluebackedfish.helodrace.main</li></activeMods><knownExpansions><li>ludeon.rimworld.biotech</li></knownExpansions></ModsConfigData>
'@ | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $auditPath 'Config\ModsConfig.xml')
foreach ($name in @('Helodrace.dll','Helodrace.pdb')) {
    $sourcePath = Join-Path $repository "Assemblies\$name"
    $targetPath = Join-Path $GameRoot "Mods\HelodRace-Main\Assemblies\$name"
    Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
    if ((Get-FileHash -LiteralPath $sourcePath).Hash -ne (Get-FileHash -LiteralPath $targetPath).Hash) { throw "Deployment hash mismatch: $name" }
}
$newJobPath = Join-Path $repository 'Defs\Organization\NewTacticalJobs.xml'
Copy-Item -LiteralPath $newJobPath -Destination (Join-Path $GameRoot 'Mods\HelodRace-Main\Defs\Organization\NewTacticalJobs.xml') -Force
$breachDefPath = Join-Path $repository 'Defs\ColdWar\BreachExplosive_ColdWar.xml'
Copy-Item -LiteralPath $breachDefPath -Destination (Join-Path $GameRoot 'Mods\HelodRace-Main\Defs\ColdWar\BreachExplosive_ColdWar.xml') -Force
$hammerJobsPath = Join-Path $repository 'Defs\GreatWar\Sledgehammer_Breach.xml'
Copy-Item -LiteralPath $hammerJobsPath -Destination (Join-Path $GameRoot 'Mods\HelodRace-Main\Defs\GreatWar\Sledgehammer_Breach.xml') -Force
# Remove only these retired definitions from the explicitly named installed mod.
foreach ($relative in @('Defs\Tactical\RaidTactical_Duties.xml','Defs\GreatWar\Jobs_RaidGrenadePreparation.xml','Defs\GreatWar\Jobs_RaidEntryObservation.xml',
    'Languages\Korean (한국어)\DefInjected\JobDef\RaidGrenadePreparation.xml','Languages\Korean (한국어)\DefInjected\JobDef\RaidEntryObservation.xml')) {
    $retiredInstalled = [IO.Path]::GetFullPath((Join-Path (Join-Path $GameRoot 'Mods\HelodRace-Main') $relative))
    $installedRoot = [IO.Path]::GetFullPath((Join-Path $GameRoot 'Mods\HelodRace-Main')) + [IO.Path]::DirectorySeparatorChar
    if (-not $retiredInstalled.StartsWith($installedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Retired definition path escaped the installed mod.' }
    if (Test-Path -LiteralPath $retiredInstalled) { Remove-Item -LiteralPath $retiredInstalled -Force }
}
$manifest = @{ engine=$Engine; workload=$Workload; fixtureCase=$Case; requestedPopulation=$Population; high=[bool]$High; headless=[bool]$Headless; warmupTicks=$WarmupTicks; sampleTicks=$SampleTicks; seed=$Seed; methodProfile=(-not $NoMethodProfile); profilePreset=$ProfilePreset; spikeThresholdMs=$SpikeThresholdMs; spikePawnId=$SpikePawnId; targets=$ProfileTargets; assemblySha256=(Get-FileHash -LiteralPath (Join-Path $repository 'Assemblies\Helodrace.dll')).Hash.ToLowerInvariant() }
$manifest.breachDefinitionsSha256 = (Get-FileHash -LiteralPath $breachDefPath).Hash.ToLowerInvariant()
$manifest.hammerJobDefinitionsSha256 = (Get-FileHash -LiteralPath $hammerJobsPath).Hash.ToLowerInvariant()
$manifest.reload = [bool]$Reload
$manifest.speed = $Speed
$manifest.defaultEngine = [bool]$DefaultEngine
$manifest.newJobDefinitionsSha256 = (Get-FileHash -LiteralPath $newJobPath).Hash.ToLowerInvariant()
$manifest | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $auditPath 'launcher.json')
$arguments = @('-quicktest', ('"-savedatafolder=' + $auditPath + '"'), ('"-hdTacticalEngineAudit=' + $auditPath + '\audit.json"'),
    "-hdTacticalAuditWorkload=$Workload", "-hdTacticalAuditPopulation=$Population", "-hdTacticalAuditSpeed=$Speed",
    "-hdTacticalAuditWarmup=$WarmupTicks", "-hdTacticalAuditSample=$SampleTicks", "-hdTacticalAuditSeed=$Seed",
    "-hdTacticalAuditCase=$Case",
    $(if ($High) { '-hdTacticalAuditHigh=true' } else { '-hdTacticalAuditLow=true' }),
    $(if ($NoMethodProfile) { '-hdMethodProfileDisabled=true' } else { '"-hdMethodProfile=' + $auditPath + '\profiles"' }),
    "-hdMethodProfilePreset=$ProfilePreset", ('-hdMethodProfileSpikeThresholdMs=' + $SpikeThresholdMs.ToString([Globalization.CultureInfo]::InvariantCulture)), ('"-hdMethodProfileTargets=' + $ProfileTargets + '"'),
    '-screen-fullscreen','0','-screen-width','800','-screen-height','600',
    '-logFile', ('"' + $auditPath + '\Player.log"'))
if ($null -ne $SpikePawnId) { $arguments += "-hdMethodProfileSpikePawnId=$SpikePawnId" }
if (-not $DefaultEngine) { $arguments += "-hdTacticalEngine=$Engine" }
if ($Headless) { $arguments += @('-batchmode','-nographics') }
if ($Reload) { $arguments += '-hdTacticalAuditReload=true' }
$manifest.arguments = $arguments
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $auditPath 'launcher.json')
$process = Start-Process -FilePath (Join-Path $GameRoot 'RimWorldWin64.exe') -WorkingDirectory $GameRoot -WindowStyle Hidden -PassThru -ArgumentList $arguments
$process.Id | Set-Content -LiteralPath (Join-Path $auditPath 'process-id.txt')
Write-Output "Engine audit started: pid=$($process.Id) engine=$Engine workload=$Workload root=$auditPath"
