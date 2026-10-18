param(
    [string]$AssemblyPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Assemblies/Helodrace.dll'),
    [string]$RimWorldPath = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $RimWorldPath 'RimWorldWin64_Data/Managed'
foreach ($name in 'UnityEngine.CoreModule','UnityEngine','Assembly-CSharp') {
    $null = [Reflection.Assembly]::LoadFrom("$managed/$name.dll")
}
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$armorType = $assembly.GetType('Helodrace.ModernWar.CompModularArmor',$true)
function Assert($condition,$message) { if (!$condition) { throw $message } }
function Set-Field($object,[type]$owner,$name,$value) { $owner.GetField($name,[Reflection.BindingFlags]'Instance,Public,NonPublic').SetValue($object,$value) }
function Invoke-Armor($object,$name,[object[]]$arguments = @()) { ,($armorType.GetMethod($name,[Reflection.BindingFlags]'Instance,Public,NonPublic').Invoke($object,$arguments)) }
$parent = [RimWorld.Apparel]::new()
$slotA = [Helodrace.ModernWar.ModularArmorSlotDef]::new(); $slotA.defName = 'StatsSlot'
$position = [Helodrace.ModernWar.ModularArmorPositionDef]::new(); $position.defName = 'StatsPosition'

# The stats panel must describe the draft's real combat effect, including ordered
# offset/factor combinations and a zero factor, without editing worn apparel.
$statsArmor = [Helodrace.ModernWar.CompModularArmor]::new()
$statsProps = [Helodrace.ModernWar.CompProperties_ModularArmor]::new()
$statsProps.baseErgonomics = 7
Set-Field $statsArmor ([Verse.ThingComp]) 'parent' $parent
Set-Field $statsArmor ([Verse.ThingComp]) 'props' $statsProps
Set-Field $statsArmor $armorType 'installedParts' ([Collections.Generic.List[Helodrace.ModernWar.InstalledModularArmorPart]]::new())
$statsProps.slots = [Collections.Generic.List[Helodrace.ModernWar.ModularArmorSlotDef]]::new()
$statsProps.slots.Add($slotA)
$statsDef = [RimWorld.StatDef]::new(); Set-Field $statsDef ([Verse.Def]) 'defName' 'FixtureMoveSpeed'
$conversion = [Helodrace.ModernWar.ModularArmorStatConversion]::new()
$conversion.targetStat = $statsDef; $conversion.source = [Helodrace.ModernWar.ModularArmorMetric]::Ergonomics
$conversion.referenceValue = 5; $conversion.factor = 0.04
$statsProps.statConversions = [Collections.Generic.List[Helodrace.ModernWar.ModularArmorStatConversion]]::new()
$statsProps.statConversions.Add($conversion)
$statsDraft = Invoke-Armor $statsArmor 'CreateEditorDraft'
$effectResolver = $assembly.GetType('Helodrace.ModernWar.ModularArmorStatEffect',$true).GetMethod('Resolve')
$effect = $effectResolver.Invoke($null,[object[]]@($statsDraft,$statsDef))
Assert ([Math]::Abs($effect.Offset - 0.08) -lt 0.00001 -and [Math]::Abs($effect.Factor - 1) -lt 0.00001) 'Initial converted movement effect is incorrect.'
$statsPart = [Helodrace.ModernWar.ModularArmorPartDef]::new()
$statsPart.defName = 'FixtureStatsPart'; $statsPart.slot = $slotA; $statsPart.fixedPosition = $position; $statsPart.ergonomics = -3
$statsPart.statModifiers = [Collections.Generic.List[Helodrace.ModernWar.ModularArmorPartStatModifier]]::new()
$factorModifier = [Helodrace.ModernWar.ModularArmorPartStatModifier]::new()
$factorModifier.stat = $statsDef; $factorModifier.mode = [Helodrace.ModernWar.ModularArmorConversionMode]::Factor; $factorModifier.value = 0.5
$offsetModifier = [Helodrace.ModernWar.ModularArmorPartStatModifier]::new()
$offsetModifier.stat = $statsDef; $offsetModifier.value = 0.2
$statsPart.statModifiers.Add($factorModifier); $statsPart.statModifiers.Add($offsetModifier)
Assert ((Invoke-Armor $statsDraft 'SetPart' ([object[]]@($slotA,$statsPart,$position,$null)))) 'Stat preview draft installation failed.'
$effect = $effectResolver.Invoke($null,[object[]]@($statsDraft,$statsDef))
Assert ([Math]::Abs($effect.Offset - 0.18) -lt 0.00001 -and [Math]::Abs($effect.Factor - 0.5) -lt 0.00001) 'Stat preview loses modifier ordering.'
$combatArgs = [object[]]@($statsDef,[single]4.6)
$null = $armorType.GetMethod('ApplyStatConversions').Invoke($statsDraft,$combatArgs)
Assert ([Math]::Abs($combatArgs[1] - (4.6 * $effect.Factor + $effect.Offset)) -lt 0.00001) 'Displayed effect does not reproduce the real combat stat.'
$liveEffect = $effectResolver.Invoke($null,[object[]]@($statsArmor,$statsDef))
$liveParts = $armorType.GetProperty('InstalledParts').GetValue($statsArmor)
Assert ([Math]::Abs($liveEffect.Offset - 0.08) -lt 0.00001 -and $liveParts.Count -eq 0) 'Stat preview mutates live apparel.'
$factorModifier.value = 0
$effect = $effectResolver.Invoke($null,[object[]]@($statsDraft,$statsDef))
Assert ([Math]::Abs($effect.Factor) -lt 0.00001 -and [Math]::Abs($effect.Offset - 0.2) -lt 0.00001) 'A zero stat factor breaks the preview.'
Assert ((Invoke-Armor $statsDraft 'SetPart' ([object[]]@($slotA,$null,$null,$null)))) 'Stat preview part removal failed.'
$effect = $effectResolver.Invoke($null,[object[]]@($statsDraft,$statsDef))
Assert ([Math]::Abs($effect.Offset - 0.08) -lt 0.00001 -and [Math]::Abs($effect.Factor - 1) -lt 0.00001) 'Removing a part leaves stale converted stats.'
Write-Output 'PASS: converted draft stats match combat, preserve offset/factor order, handle zero factors and update after part removal without mutating worn apparel.'
