param(
    [string]$AssemblyPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Assemblies/Helodrace.dll'),
    [string]$RimWorldPath = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
function Assert($condition,$message) { if (!$condition) { throw $message } }
$all = [xml]'<Defs />'
foreach ($file in Get-ChildItem "$repo/Defs" -Recurse -Filter *.xml) {
    [xml]$xml = Get-Content $file.FullName -Raw -Encoding UTF8
    foreach ($node in $xml.SelectNodes('/Defs/*')) { $null = $all.DocumentElement.AppendChild($all.ImportNode($node,$true)) }
}
foreach ($file in Get-ChildItem "$repo/Patches","$repo/Languages" -Recurse -Filter *.xml) { $null = [xml](Get-Content $file.FullName -Raw -Encoding UTF8) }
$nodes = $all.SelectNodes('/Defs/ThingDef[defName="HD_ClothingBench"]')
Assert ($nodes.Count -eq 1) 'Clothing workbench is missing or duplicated.'
$bench = $nodes[0]
$gun = $all.SelectSingleNode('/Defs/ThingDef[defName="HD_GunSmithTable"]')
foreach ($key in 'size','graphicData/drawSize','interactionCellOffset','hasInteractionCell') {
    Assert ($bench.SelectSingleNode($key).InnerText -eq $gun.SelectSingleNode($key).InnerText) "Gunsmith placement mismatch: $key"
}
$gunOffset = $gun.SelectSingleNode('graphicData/drawOffset')
Assert ($bench.graphicData.drawOffset -eq $(if ($gunOffset) { $gunOffset.InnerText } else { '(0,0,0)' })) 'Graphic offset differs from gunsmith table.'
foreach ($direction in 'north','east','south') {
    Assert (Test-Path "$repo/Textures/$($bench.graphicData.texPath)_${direction}.png") "Missing clothing workbench texture: $direction"
}
foreach ($cost in $bench.costList.ChildNodes) {
    Assert ($null -ne $all.SelectSingleNode('/Defs/ThingDef[defName="' + $cost.Name + '"]') -or $cost.Name -eq 'Steel') "Missing construction ingredient: $($cost.Name)"
}
Assert ($null -ne $all.SelectSingleNode('/Defs/ResearchProjectDef[defName="' + $bench.researchPrerequisites.li + '"]')) 'Workbench research prerequisite is undefined.'
[xml]$patch = Get-Content "$repo/Patches/ClothingBenchRecipes.xml" -Raw -Encoding UTF8
$added = 0
foreach ($operation in $patch.Patch.Operation) {
    $targets = $all.SelectNodes([string]$operation.xpath)
    Assert ($targets.Count -gt 0) 'Workbench recipe patch matches no definitions.'
    foreach ($target in $targets) {
        foreach ($item in $operation.value.ChildNodes) { $null = $target.AppendChild($all.ImportNode($item,$true)) }
        $added++
    }
}
$partBase = $all.SelectSingleNode('/Defs/ThingDef[@Name="HD_ModularArmorPartItemBase"]/recipeMaker/recipeUsers/li[text()="HD_ClothingBench"]')
Assert ($null -ne $partBase) 'Armor part recipes do not inherit clothing workbench support.'
Write-Output "PASS: workbench placement/texture/research/material definitions; $added recipe user lists support the workbench."

$managed = Join-Path $RimWorldPath 'RimWorldWin64_Data/Managed'
foreach ($name in 'UnityEngine.CoreModule','UnityEngine','Assembly-CSharp') { $null = [Reflection.Assembly]::LoadFrom("$managed/$name.dll") }
$null = [Reflection.Assembly]::LoadFrom("$repo/Assemblies/0Harmony.dll")
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$armorType = $assembly.GetType('Helodrace.ModernWar.CompModularArmor',$true)
$benchType = $assembly.GetType('Helodrace.ModernWar.CompClothingWorkbench',$true)
$propertiesType = $assembly.GetType('Helodrace.ModernWar.CompProperties_ClothingWorkbench',$true)
$properties = [Activator]::CreateInstance($propertiesType)
Assert ($properties.compClass -eq $benchType) 'Workbench component type is not bound.'
foreach ($method in 'CompGetGizmosExtra','CompGetWornGizmosExtra') {
    Assert ($armorType.GetMethod($method).DeclaringType -ne $armorType) 'Apparel still provides an independent customization command.'
}
$constructors = $assembly.GetType('Helodrace.ModernWar.Dialog_ModularArmor',$true).GetConstructors()
Assert ($constructors.Count -eq 1 -and $constructors[0].GetParameters().Count -eq 2 -and $constructors[0].GetParameters()[1].ParameterType -eq $benchType) 'Editor can be opened without workbench context.'
$gate = $benchType.GetMethod('CanUse',[Reflection.BindingFlags]'Static,NonPublic')
Assert (!$gate.Invoke($null,[object[]]@($null,$null))) 'No-workbench customization is accepted.'
$fixtureBench = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([Verse.ThingWithComps])
[Verse.Thing].GetField('mapIndexOrState',[Reflection.BindingFlags]'Instance,NonPublic').SetValue($fixtureBench,[sbyte]-1)
$fixtureComp = [Activator]::CreateInstance($benchType)
[Verse.ThingComp].GetField('parent').SetValue($fixtureComp,$fixtureBench)
$comps = [Collections.Generic.List[Verse.ThingComp]]::new(); $comps.Add($fixtureComp)
[Verse.ThingWithComps].GetField('comps',[Reflection.BindingFlags]'Instance,NonPublic').SetValue($fixtureBench,$comps)
Assert (!$gate.Invoke($null,[object[]]@($fixtureBench,$null))) 'Unspawned workbench accepts customization.'
Write-Output 'PASS: apparel commands removed; editor requires workbench context; absent and unspawned workbenches reject customization.'

$buildingType = $assembly.GetType([string]$bench.thingClass,$true)
$building = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($buildingType)
foreach ($property in 'DrawColor','DrawColorTwo') {
    $color = $buildingType.GetProperty($property).GetValue($building)
    Assert ($color.r -eq 1 -and $color.g -eq 1 -and $color.b -eq 1 -and $color.a -eq 1) 'Workbench texture is tinted.'
}
Write-Output 'PASS: clothing workbench uses white draw colors for both channels, independent of stuff.'

function Set-Field($object,[type]$owner,$name,$value) { $owner.GetField($name,[Reflection.BindingFlags]'Instance,Public,NonPublic').SetValue($object,$value) }
[RimWorld.DefOfHelper].GetField('bindingNow',[Reflection.BindingFlags]'Static,NonPublic').SetValue($null,$true)
# Container notifications need a live world. Suppress only those notifications in
# this headless fixture; exercise real ThingOwner adds/removals and item ownership.
Add-Type 'public static class ArmorFixtureNotifications { public static bool Prefix() { return false; } }'
$fixtureHarmony = [HarmonyLib.Harmony]::new('Helodrace.ArmorEditorFixture')
$prefix = [HarmonyLib.HarmonyMethod]::new([ArmorFixtureNotifications].GetMethod('Prefix'))
foreach ($name in 'NotifyAdded','NotifyRemoved') {
    $method = [Verse.ThingOwner].GetMethod($name,[Reflection.BindingFlags]'Instance,Public,NonPublic')
    $null = $fixtureHarmony.Patch($method,$prefix)
}
function Invoke-Armor($object,$name,[object[]]$arguments = @()) { ,($armorType.GetMethod($name,[Reflection.BindingFlags]'Instance,Public,NonPublic').Invoke($object,$arguments)) }
$parent = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([RimWorld.Apparel])
Set-Field $parent ([Verse.Thing]) 'mapIndexOrState' ([sbyte]-1)
$parentDef = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([Verse.ThingDef]); Set-Field $parentDef ([Verse.Def]) 'defName' 'EditorFixture'
Set-Field $parent ([Verse.Thing]) 'def' $parentDef
$armor = [Helodrace.ModernWar.CompModularArmor]::new()
Set-Field $armor ([Verse.ThingComp]) 'parent' $parent
$props = [Helodrace.ModernWar.CompProperties_ModularArmor]::new()
Set-Field $armor ([Verse.ThingComp]) 'props' $props
$slotA = [Helodrace.ModernWar.ModularArmorSlotDef]::new(); $slotA.defName='FixtureA'
$slotB = [Helodrace.ModernWar.ModularArmorSlotDef]::new(); $slotB.defName='FixtureB'
$props.slots = [Collections.Generic.List[Helodrace.ModernWar.ModularArmorSlotDef]]::new()
$props.slots.Add($slotA); $props.slots.Add($slotB)
$position = [Helodrace.ModernWar.ModularArmorPositionDef]::new(); $position.defName='FixturePosition'
$part = [Helodrace.ModernWar.ModularArmorPartDef]::new(); $part.defName='FixturePart'; $part.slot=$slotA; $part.fixedPosition=$position
$other = [Helodrace.ModernWar.ModularArmorPartDef]::new(); $other.defName='FixtureOther'; $other.slot=$slotB; $other.fixedPosition=$position
$itemDef = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([Verse.ThingDef]); Set-Field $itemDef ([Verse.Def]) 'defName' 'FixturePhysicalItem'; Set-Field $itemDef ([Verse.ThingDef]) 'category' ([Verse.ThingCategory]::Item)
$part.partThingDef=$itemDef
$physical = [Verse.Thing]::new(); Set-Field $physical ([Verse.Thing]) 'def' $itemDef; Set-Field $physical ([Verse.Thing]) 'stackCount' 1; [Verse.Thing].GetProperty('HitPoints').SetValue($physical,37)
$record = [Helodrace.ModernWar.InstalledModularArmorPart]::new($slotA,$part,$position)
Assert ($record.TryInstallRequiredItem($physical)) 'Cannot set up physical part fixture.'
$records = [Collections.Generic.List[Helodrace.ModernWar.InstalledModularArmorPart]]::new(); $records.Add($record)
Set-Field $armor $armorType 'installedParts' $records
$signature = Invoke-Armor $armor 'ConfigurationSignature'
$draft = Invoke-Armor $armor 'CreateEditorDraft'
Assert ((Invoke-Armor $draft 'InstalledIn' ([object[]]@($slotA))) -ne $record) 'Editor shares a mutable record with live apparel.'
Assert ((Invoke-Armor $draft 'InstalledIn' ([object[]]@($slotA))).InstalledItem -eq $physical) 'Preview cannot inspect original item health.'
Assert ((Invoke-Armor $draft 'SetPart' ([object[]]@($slotA,$null,$null,$null)))) 'Draft removal failed.'
Assert ((Invoke-Armor $draft 'SetPart' ([object[]]@($slotB,$other,$null,$null)))) 'Draft addition failed.'
Assert ((Invoke-Armor $armor 'InstalledIn' ([object[]]@($slotA))) -eq $record -and $record.InstalledItem -eq $physical -and (Invoke-Armor $armor 'InstalledIn' ([object[]]@($slotB))) -eq $null) 'Draft changes mutated live apparel.'
Assert ((Invoke-Armor $armor 'ConfigurationSignature') -eq $signature) 'Cancel changes the live configuration.'
Write-Output 'PASS: draft additions/removals are isolated; cancel preserves the physical item and original configuration.'

$draft = Invoke-Armor $armor 'CreateEditorDraft'
Assert ((Invoke-Armor $draft 'SetPart' ([object[]]@($slotB,$other,$null,$null)))) 'Second draft addition failed.'
$plan = Invoke-Armor $draft 'EditorPlan'
$empty = [Collections.Generic.List[Verse.Thing]]::new()
Assert (!(Invoke-Armor $armor 'CompleteCustomization' ([object[]]@($plan,'stale baseline',$empty)))) 'Stale editor plan was applied.'
Assert ((Invoke-Armor $armor 'CompleteCustomization' ([object[]]@($plan,$signature,$empty)))) 'Batch commit failed.'
Assert ((Invoke-Armor $armor 'InstalledIn' ([object[]]@($slotA))).InstalledItem -eq $physical -and [Verse.Thing].GetProperty('HitPoints').GetValue($physical) -eq 37) 'Batch replaced or healed the existing physical part.'
Assert ((Invoke-Armor $armor 'InstalledIn' ([object[]]@($slotB))).part -eq $other) 'Batch addition was not committed.'

$missing = [Helodrace.ModernWar.ModularArmorPartDef]::new(); $missing.defName='FixtureMissing'; $missing.slot=$slotB; $missing.fixedPosition=$position; $missing.partThingDef=$itemDef
$before = Invoke-Armor $armor 'ConfigurationSignature'
$draft = Invoke-Armor $armor 'CreateEditorDraft'
Assert ((Invoke-Armor $draft 'SetPart' ([object[]]@($slotB,$missing,$null,$null)))) 'Missing-stock draft setup failed.'
$plan = Invoke-Armor $draft 'EditorPlan'
Assert (!(Invoke-Armor $armor 'CompleteCustomization' ([object[]]@($plan,$before,$empty)))) 'Missing physical parts passed batch preflight.'
Assert ((Invoke-Armor $armor 'ConfigurationSignature') -eq $before -and [Verse.Thing].GetProperty('HitPoints').GetValue($physical) -eq 37) 'Failed preflight partially changed apparel.'
Write-Output 'PASS: batch applies all edits, retains item identity/health, rejects stale plans and missing stock without partial changes.'

$panel = [Helodrace.ModernWar.ModularArmorPalsPanelDef]::new(); $panel.defName='FixturePanel'
$props.palsPanels = [Collections.Generic.List[Helodrace.ModernWar.ModularArmorPalsPanelDef]]::new(); $props.palsPanels.Add($panel)
$pouch = [Helodrace.ModernWar.ModularArmorPartDef]::new(); $pouch.defName='FixturePouch'
$pouch.installMode = [Helodrace.ModernWar.ModularArmorInstallMode]::Positionable
$pouch.allowedPalsPanels = [Collections.Generic.List[Helodrace.ModernWar.ModularArmorPalsPanelDef]]::new(); $pouch.allowedPalsPanels.Add($panel)
$pouchRecord = [Helodrace.ModernWar.InstalledModularArmorPart]::new($pouch,$panel,0,0)
$currentRecords = $armorType.GetField('installedParts',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($armor); $currentRecords.Add($pouchRecord)
$before = Invoke-Armor $armor 'ConfigurationSignature'
$draft = Invoke-Armor $armor 'CreateEditorDraft'
$draftRecords = $armorType.GetField('installedParts',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($draft)
$draftPouch = $draftRecords[$draftRecords.Count-1]
Assert ((Invoke-Armor $draft 'MovePalsPart' ([object[]]@($draftPouch,$panel,1,1)))) 'Draft PALS movement failed.'
Assert ($pouchRecord.palsX -eq 0 -and $pouchRecord.palsY -eq 0) 'PALS movement changed live apparel before confirmation.'
$plan = Invoke-Armor $draft 'EditorPlan'
Assert ((Invoke-Armor $armor 'CompleteCustomization' ([object[]]@($plan,$before,$empty)))) 'PALS movement batch failed.'
$currentRecords = $armorType.GetField('installedParts',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($armor)
Assert ($currentRecords[$currentRecords.Count-1].palsX -eq 1 -and $currentRecords[$currentRecords.Count-1].palsY -eq 1) 'PALS movement was not committed.'
Write-Output 'PASS: PALS position changes remain in the preview until the whole batch commits.'

# The game creates a fresh driver when an ordered job begins. Verify that its
# plan comes from the apparel work order, rather than a disposable cached driver.
$order = [Helodrace.ModernWar.ModularArmorWorkOrder]::new(); $order.jobId=123; $order.workTicks=420; $order.baseline=$before; $order.plan=$plan
Set-Field $armor $armorType 'pendingCustomization' $order
$parentComps = [Collections.Generic.List[Verse.ThingComp]]::new(); $parentComps.Add($armor)
Set-Field $parent ([Verse.ThingWithComps]) 'comps' $parentComps
$fixtureJob = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([Verse.AI.Job])
Set-Field $fixtureJob ([Verse.AI.Job]) 'loadID' 123
$fixtureJob.SetTarget([Verse.AI.TargetIndex]::A,[Verse.LocalTargetInfo]::new($parent))
$driver = [Helodrace.ModernWar.JobDriver_CustomizeModularArmor]::new()
Set-Field $driver ([Verse.AI.JobDriver]) 'job' $fixtureJob
Assert (!$driver.TryMakePreToilReservations($false)) 'Headless fixture unexpectedly reserved a bench.'
$driverType = $driver.GetType()
Assert ([object]::ReferenceEquals($driverType.GetField('plan',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($driver),$plan)) 'A fresh job driver lost the edit plan.'
Assert ($driverType.GetField('workTicks',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($driver) -eq 420) 'A fresh job driver lost batch work time.'
Write-Output 'PASS: a freshly created job driver retrieves the saved apparel work order.'
