param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$ModAssembly = '',
    [string]$RimWorldPath = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
)
$ErrorActionPreference = 'Stop'
if (!$ModAssembly) { $ModAssembly = Join-Path $RepositoryRoot 'Assemblies/Helodrace.dll' }
$game = [Reflection.Assembly]::LoadFrom((Join-Path $RimWorldPath 'RimWorldWin64_Data/Managed/Assembly-CSharp.dll'))
$null = [Reflection.Assembly]::LoadFrom((Join-Path $RepositoryRoot 'Assemblies/0Harmony.dll'))
$mod = [Reflection.Assembly]::LoadFrom($ModAssembly)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance'
function Resolve-Type([string]$name) {
    foreach ($candidate in @($name, "Verse.$name", "RimWorld.$name")) {
        $type = $mod.GetType($candidate)
        if (!$type) { $type = $game.GetType($candidate) }
        if ($type) { return $type }
    }
    throw "Unknown game/mod type: $name"
}
function Check-Members($node, [Type]$type) {
    foreach ($child in $node.ChildNodes) {
        if ($child.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
        if (!$type.GetField($child.Name, $flags)) { throw "Invalid XML field $($type.FullName).$($child.Name)" }
    }
}
[xml]$defs = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Defs/ModernWar/Buildings/Crows.xml') -Raw
foreach ($def in $defs.Defs.ThingDef) {
    Check-Members $def (Resolve-Type 'ThingDef')
    if ($def.thingClass) { $null = Resolve-Type $def.thingClass }
    Check-Members $def.graphicData (Resolve-Type 'GraphicData')
    if ($def.building) { Check-Members $def.building (Resolve-Type 'BuildingProperties') }
    foreach ($comp in $def.comps.li) { if ($comp) { Check-Members $comp (Resolve-Type $comp.Class) } }
    foreach ($verb in $def.verbs.li) { if ($verb) { Check-Members $verb (Resolve-Type 'VerbProperties'); $null = Resolve-Type $verb.verbClass } }
}
[xml]$jobs = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Defs/ModernWar/Jobs_Crows.xml') -Raw
Check-Members $jobs.Defs.JobDef (Resolve-Type 'JobDef')
$null = Resolve-Type $jobs.Defs.JobDef.driverClass
if (!(Resolve-Type 'Building_TurretGun').IsAssignableFrom((Resolve-Type 'Helodrace.Building_Crows'))) { throw 'CROWS must preserve vanilla turret targeting.' }
if (!(Resolve-Type 'Verb_Shoot').IsAssignableFrom((Resolve-Type 'Helodrace.Verb_Crows'))) { throw 'CROWS gun must preserve vanilla shooting.' }
Write-Output 'PASS: new XML fields, comps, building/gun/job classes and inheritance match the installed RimWorld 1.6 API.'
