param(
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$AuditRoot = ('C:\Users\Public\Documents\ESTsoft\CreatorTemp\hd-raid-audit-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$Cases = '0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19',
    [int]$WarmupTicks = 300,
    [int]$SampleTicks = 600,
    [switch]$Functional,
    [switch]$SpawnCommands,
    [switch]$MethodProfile,
    [switch]$ProfileElapsedOnly,
    [switch]$ProfileManual,
    [ValidateSet('detailed', 'coarse')][string]$ProfilePreset = 'detailed',
    [string]$ProfileTargets = ''
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name 'RimWorld*' -ErrorAction SilentlyContinue) {
    throw 'Close the existing game before deploying or running this isolated audit.'
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$auditPath = [IO.Path]::GetFullPath($AuditRoot)
if (-not $auditPath.StartsWith('C:\Users\Public\Documents\ESTsoft\CreatorTemp\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Audit output must be a new directory under CreatorTemp.'
}
if (Test-Path -LiteralPath $auditPath) { throw 'Use a new audit folder to preserve previous measurements.' }
New-Item -ItemType Directory -Path (Join-Path $auditPath 'Config') -Force | Out-Null
@'
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData><version>1.6.4871 rev590</version><activeMods><li>brrainz.harmony</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>bluebackedfish.helodrace.main</li></activeMods><knownExpansions><li>ludeon.rimworld.biotech</li></knownExpansions></ModsConfigData>
'@ | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $auditPath 'Config\ModsConfig.xml')
foreach ($name in @('Helodrace.dll', 'Helodrace.pdb')) {
    $sourcePath = Join-Path $repository "Assemblies\$name"
    $targetPath = Join-Path $GameRoot "Mods\HelodRace-Main\Assemblies\$name"
    Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
    if ((Get-FileHash -LiteralPath $sourcePath).Hash -ne (Get-FileHash -LiteralPath $targetPath).Hash) {
        throw "Deployment hash mismatch: $name"
    }
}
$process = Start-Process -FilePath (Join-Path $GameRoot 'RimWorldWin64.exe') -WorkingDirectory $GameRoot -WindowStyle Hidden -PassThru -ArgumentList @(
    '-quicktest', ('"-savedatafolder=' + $auditPath + '"'), ('"-hdRaidMovementAudit=' + $auditPath + '\measurements.ndjson"'),
    ("-hdRaidMovementAuditCases=$Cases"),
    ("-hdRaidMovementAuditWarmup=$WarmupTicks"), ("-hdRaidMovementAuditSample=$SampleTicks"),
    $(if ($Functional) { '-hdRaidMovementAuditFunctional=true' } else { '-hdRaidMovementAuditFunctionalDisabled=true' }),
    $(if ($SpawnCommands) { '-hdRaidSpawnAudit=true' } else { '-hdRaidSpawnAuditDisabled=true' }),
    $(if ($MethodProfile) { '"-hdMethodProfile=' + $auditPath + '\profiles"' } else { '-hdMethodProfileDisabled=true' }),
    $(if ($ProfileElapsedOnly) { '-hdMethodProfileElapsedOnly=true' } else { '-hdMethodProfileCpu=true' }),
    $(if ($ProfileManual) { '-hdMethodProfileManual=true' } else { '-hdMethodProfileAuto=true' }),
    ("-hdMethodProfilePreset=$ProfilePreset"),
    $(if ($ProfileTargets) { '"-hdMethodProfileTargets=' + $ProfileTargets + '"' } else { '-hdMethodProfileTargetsDisabled=true' }),
    '-logFile', ('"' + $auditPath + '\Player.log"'), '-screen-width', '800', '-screen-height', '600', '-screen-fullscreen', '0'
)
$process.Id | Set-Content -LiteralPath (Join-Path $auditPath 'process-id.txt')
Write-Output "Audit process $($process.Id); output $auditPath\measurements.ndjson"
# The explicitly requested audit exits its own game after the last case.
# Normal saves, configuration and already running games are never reused.
