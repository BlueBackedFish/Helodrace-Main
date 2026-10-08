param(
    [ValidateSet('vanilla','legacy','new')][string]$Engine = 'vanilla',
    [ValidateSet('open-approach','sapper-wall','sapper-door')][string]$Workload = 'open-approach',
    [ValidateRange(0,400)][int]$Population = 50,
    [ValidateRange(0,10000)][int]$WarmupTicks = 600,
    [ValidateRange(1,20000)][int]$SampleTicks = 1200,
    [string]$Seed = 'hd-r1-20261007',
    [ValidateSet('normal','interrupt','casualty','rocks','narrow','contact','field','recovery','cutter','cutter-recovery','charge-recovery','charge-fuse-casualty','charge-change','multiroom','unexpected-hole','inside-goal','door-contact','outdoor-opening','small-unseen','room-recovery','cutter-active-recovery','tiny-adjacent')][string]$Case = 'normal',
    [switch]$High,
    [switch]$NoMethodProfile,
    [ValidateSet('coarse','detailed','spikes','pawn-spikes','needs-spikes','jobs-spikes','path-spikes')][string]$ProfilePreset = 'coarse',
    [ValidateRange(0.1,1000)][double]$SpikeThresholdMs = 5,
    [ValidateRange(0,2147483647)][Nullable[int]]$SpikePawnId = $null,
    [string]$ProfileTargets = 'Verse.AI.Pawn_JobTracker::StartJob;Verse.PathFinder::CreateRequest',
    [string]$AuditRoot = ('C:\Users\Public\Documents\ESTsoft\CreatorTemp\hd-engine-audit-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
)
$ErrorActionPreference = 'Stop'
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
$manifest = @{ engine=$Engine; workload=$Workload; fixtureCase=$Case; requestedPopulation=$Population; high=[bool]$High; warmupTicks=$WarmupTicks; sampleTicks=$SampleTicks; seed=$Seed; methodProfile=(-not $NoMethodProfile); profilePreset=$ProfilePreset; spikeThresholdMs=$SpikeThresholdMs; spikePawnId=$SpikePawnId; targets=$ProfileTargets; assemblySha256=(Get-FileHash -LiteralPath (Join-Path $repository 'Assemblies\Helodrace.dll')).Hash.ToLowerInvariant() }
$manifest | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $auditPath 'launcher.json')
$arguments = @('-quicktest', ('"-savedatafolder=' + $auditPath + '"'), ('"-hdTacticalEngineAudit=' + $auditPath + '\audit.json"'),
    "-hdTacticalEngine=$Engine", "-hdTacticalAuditWorkload=$Workload", "-hdTacticalAuditPopulation=$Population",
    "-hdTacticalAuditWarmup=$WarmupTicks", "-hdTacticalAuditSample=$SampleTicks", "-hdRaidMovementAuditSeed=$Seed",
    "-hdTacticalAuditCase=$Case",
    $(if ($High) { '-hdTacticalAuditHigh=true' } else { '-hdTacticalAuditLow=true' }),
    $(if ($NoMethodProfile) { '-hdMethodProfileDisabled=true' } else { '"-hdMethodProfile=' + $auditPath + '\profiles"' }),
    "-hdMethodProfilePreset=$ProfilePreset", ('-hdMethodProfileSpikeThresholdMs=' + $SpikeThresholdMs.ToString([Globalization.CultureInfo]::InvariantCulture)), ('"-hdMethodProfileTargets=' + $ProfileTargets + '"'),
    '-screen-fullscreen','0','-screen-width','800','-screen-height','600',
    '-logFile', ('"' + $auditPath + '\Player.log"'))
if ($null -ne $SpikePawnId) { $arguments += "-hdMethodProfileSpikePawnId=$SpikePawnId" }
$process = Start-Process -FilePath (Join-Path $GameRoot 'RimWorldWin64.exe') -WorkingDirectory $GameRoot -WindowStyle Hidden -PassThru -ArgumentList $arguments
$process.Id | Set-Content -LiteralPath (Join-Path $auditPath 'process-id.txt')
Write-Output "Engine audit started: pid=$($process.Id) engine=$Engine workload=$Workload root=$auditPath"
