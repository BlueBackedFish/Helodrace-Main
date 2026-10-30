param([string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld', [switch]$PatchSmokeTest)
$ErrorActionPreference = 'Stop'
$null = & (Join-Path $PSScriptRoot 'Import-Game17.ps1') -RimWorldPath $RimWorldPath
$repo = Split-Path $PSScriptRoot -Parent
function Assert($condition, [string]$message) { if (!$condition) { throw $message } }
$files = Get-ChildItem -LiteralPath "$repo/About", "$repo/Defs", "$repo/Patches", "$repo/Languages" -Recurse -Filter *.xml
foreach ($file in $files) { $null = [xml](Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8) }
Write-Output "PASS: $($files.Count) XML files parse."
[xml]$wildWestApparel = Get-Content -LiteralPath "$repo/Defs/WildWest/Items/Apparel_WildWest.xml" -Raw -Encoding UTF8
$formalShirt = $wildWestApparel.SelectSingleNode('/Defs/ThingDef[defName="HD_Apparel_WildWestFormalShirt"]')
Assert (@($formalShirt.apparel.bodyPartGroups.li) -contains 'Legs') 'Formal shirt does not cover the lower body.'
[xml]$coldWarApparel = Get-Content -LiteralPath "$repo/Defs/ColdWar/Items/Apparel_ColdWar.xml" -Raw -Encoding UTF8
$flakJacket = $coldWarApparel.SelectSingleNode('/Defs/ThingDef[defName="HD_Apparel_M1952AFlakJacket"]')
Assert (@($flakJacket.thingCategories.li) -contains 'ApparelArmor') 'M1952 flak jacket is not classified as armor.'
Assert (@($flakJacket.apparel.layers.li) -contains 'Shell') 'M1952 flak jacket is not on the Shell layer.'
Write-Output 'PASS: formal-shirt coverage and M1952 armor/layer classification.'
$active = Get-ChildItem -LiteralPath "$repo/About", "$repo/Defs", "$repo/Patches", "$repo/Source/Helodrace", "$repo/Languages" -Recurse -File |
    Where-Object { $_.Extension -in '.xml', '.cs', '.csproj' -and $_.FullName -notmatch '\\obj\\' }
Assert (!(Select-String -LiteralPath $active.FullName -Pattern 'AlienRace|humanoidalienraces')) 'Active framework reference found.'
[xml]$race = Get-Content -LiteralPath "$repo/Defs/Helod/Race/HelodRace.xml" -Raw -Encoding UTF8
$extension = $race.Defs.ThingDef.modExtensions.li
Assert ($extension.Class -eq 'Helodrace.HelodRaceExtension') 'Standalone extension missing.'
[xml]$settingsXml = Get-Content "$repo/Defs/Helod/Race/HelodRaceSettings.xml" -Raw -Encoding UTF8
$settings = $settingsXml.Defs.'Helodrace.HelodRaceSettingsDef'
Assert ($extension.settingsDef -eq $settings.defName) 'Settings cross-reference mismatch.'
$baseline = Get-Content "$repo/Source/Tests/StandaloneRacePolicy16Baseline.json" -Raw | ConvertFrom-Json
# Frozen from the pre-1.7 revision recorded in the fixture, including intentional
# KidHelmet support (2099d568) and deleted-head removal (58a2e208).
foreach ($property in $baseline.policy.PSObject.Properties) {
    $actual = @(foreach ($entry in $settings.SelectNodes("$($property.Name)/li")) {
        $text = if ($property.Name -eq 'apparelGraphics') { $entry.key + '=' + $entry.value } else { $entry.InnerText }
        $attributes = @($entry.Attributes | Sort-Object Name | ForEach-Object { $_.Name + '=' + $_.Value })
        $text + '|' + ($attributes -join '|')
    })
    Assert (($property.Value -join "`n") -ceq ($actual -join "`n")) "Pre-1.7 policy values/order/DLC conditions changed: $($property.Name)"
}
foreach ($pair in @(@('drawScale','0.8'), @('maleProbability','0.0000001'), @('socialFightDamageLimit','6'), @('refugeeChance','0.15'), @('slaveChance','0.15'), @('wandererChance','0.15'))) {
    Assert ($settings.SelectSingleNode($pair[0]).InnerText -eq $pair[1]) "Policy value changed: $($pair[0])"
}
Write-Output 'PASS: pre-1.7 policy values, ordering, apparel paths and DLC conditions preserved.'
$managed = $RimWorldPath
foreach ($name in 'Waylith', 'UnityStubs', 'Assembly-CSharp') {
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $managed "$name.dll"))
}
$null = [Reflection.Assembly]::LoadFrom("$RimWorldPath/../../workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll")
$assembly = [Reflection.Assembly]::LoadFrom("$repo/Assemblies/Helodrace.dll")
Assert (!($assembly.GetReferencedAssemblies() | Where-Object Name -Match 'AlienRace')) 'Compiled dependency found.'
$types = @($assembly.GetTypes() | Where-Object Name -Like 'Patch_Helod*')
Assert ($types.Count -ge 15) 'Standalone patches missing from build.'
Write-Output "PASS: compiled assembly has no framework reference; $($types.Count) standalone patch classes loaded."
# Minimal managed fixtures; no game session, native graphics, or save files are touched.
function New-Fixture([Type]$type) { [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($type) }
Assert (![Helodrace.HelodRace]::IsHelod($null)) 'Uninitialized/null race check failed.'
$earlyRequest = [Activator]::CreateInstance([Verse.PawnGenerationRequest])
[Helodrace.Patch_HelodPawnRequest]::Prefix([ref]$earlyRequest)
Assert ($null -eq $earlyRequest.KindDef) 'Uninitialized generation request was changed.'
$fixtureRace = New-Fixture ([Verse.ThingDef])
[Verse.Def].GetField('defName').SetValue($fixtureRace, 'Helod')
$fixtureSettings = [Helodrace.HelodRaceSettingsDef]::new()
$fixtureSettings.defName = 'HD_HelodRaceSettings'
[Verse.DefDatabase[Helodrace.HelodRaceSettingsDef]]::Add($fixtureSettings)
$fixtureExtension = [Helodrace.HelodRaceExtension]::new()
$fixtureExtension.settingsDef = $fixtureSettings
$extensions = [Collections.Generic.List[Verse.DefModExtension]]::new()
$extensions.Add($fixtureExtension)
[Verse.Def].GetField('modExtensions').SetValue($fixtureRace, $extensions)
[Verse.DefDatabase[Verse.ThingDef]]::Add($fixtureRace)
$fixtureKind = New-Fixture ([Verse.PawnKindDef])
$fixtureKind.defName = 'HD_WW_HelodColonist'
$fixtureKind.race = $fixtureRace
[Verse.DefDatabase[Verse.PawnKindDef]]::Add($fixtureKind)
$fixtureXenotype = New-Fixture ([RimWorld.XenotypeDef])
$fixtureXenotype.defName = 'TestHelod'
$fixtureSettings.xenotypeList.Add($fixtureXenotype)
$baseliner = New-Fixture ([RimWorld.XenotypeDef])
$baseliner.defName = 'Baseliner'
[Helodrace.HelodRace]::RebuildRuntimeCaches()
Assert ($null -ne [Helodrace.HelodRace]::RaceDef -and $null -ne [Helodrace.HelodRace]::Settings) 'Runtime caches missing.'
Assert ([object]::ReferenceEquals([Helodrace.HelodRace]::Settings, $fixtureSettings)) 'Wrong cached settings.'
$request = [Activator]::CreateInstance([Verse.PawnGenerationRequest])
$request.KindDef = $fixtureKind
$request.FixedGender = [Verse.Gender]::Female
$request.AllowedDevelopmentalStages = [Verse.DevelopmentalStage]::Newborn
$request.ForcedXenotype = $baseliner
[Helodrace.Patch_HelodPawnRequest]::Prefix([ref]$request)
Assert ([object]::ReferenceEquals($request.ForcedXenotype, $baseliner)) 'Newborn inherited xenotype request was overwritten.'
$fixturePawn = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([Verse.Pawn])
[Verse.Thing].GetField('def').SetValue($fixturePawn, $fixtureRace)
$traitDef = New-Fixture ([RimWorld.TraitDef])
$traitDef.defName = 'SpeedOffset'
$slow = [RimWorld.Trait]::new($traitDef, -1, $false)
$fast = [RimWorld.Trait]::new($traitDef, 1, $false)
Assert (![Helodrace.HelodRace]::CanHaveTrait($fixturePawn, $slow)) 'Forbidden slow trait accepted.'
Assert ([Helodrace.HelodRace]::CanHaveTrait($fixturePawn, $fast)) 'Allowed fast trait rejected.'
$geneDef = New-Fixture ([Verse.GeneDef])
$fixtureSettings.blackGeneList.Add($geneDef)
[Helodrace.HelodRace]::RebuildRuntimeCaches()
Assert (![Helodrace.HelodRace]::CanHaveGene($fixturePawn, $geneDef)) 'Forbidden gene accepted.'
$choices = [Collections.Generic.Dictionary[RimWorld.XenotypeDef, single]]::new()
$choices.Add($fixtureXenotype, 0.5)
$choices.Add($baseliner, 0.5)
[Helodrace.Patch_HelodXenotypeChoices]::Postfix($fixtureKind, $choices)
Assert ($choices.Count -eq 1 -and $choices.ContainsKey($fixtureXenotype)) 'Helod xenotype filtering failed.'
$humanKind = New-Fixture ([Verse.PawnKindDef])
$humanKind.race = New-Fixture ([Verse.ThingDef])
$choices.Add($baseliner, 0.5)
[Helodrace.Patch_HelodXenotypeChoices]::Postfix($humanKind, $choices)
Assert ($choices.Count -eq 1 -and $choices.ContainsKey($baseliner)) 'Human xenotype filtering failed.'
Write-Output 'PASS: newborn inheritance, trait degree, gene blocking, and bidirectional xenotype filtering regressions.'
$energyMethod = [Helodrace.CompMechanicalTemperatureControl].GetMethod('EnergyPerRareTick', [Reflection.BindingFlags]'Static,NonPublic')
$rareEnergy = $energyMethod.Invoke($null, @([single]2, [single]1))
Assert ([Math]::Abs($rareEnergy - (2 * 250 / 60)) -lt 0.000001) 'Mechanical heat pump did not convert per-second transfer to rare-tick energy.'
Assert ($energyMethod.Invoke($null, @([single]-2, [single]1)) -eq 0) 'Negative heat-removal setting must remain clamped to zero.'
$refillMethod = [Helodrace.BTXUtility].GetMethod('ShouldForceAutomaticRefill', [Reflection.BindingFlags]'Static,NonPublic')
Assert ($refillMethod.Invoke($null, @([Helodrace.BTXUtility]::ChemicalNeedDefName, [single]0.34))) 'BTX AI did not seek a source before the deficiency threshold.'
Assert (!$refillMethod.Invoke($null, @([Helodrace.BTXUtility]::ChemicalNeedDefName, [single]0.36))) 'BTX AI refill threshold expanded beyond its intended buffer.'
Assert (!$refillMethod.Invoke($null, @('Chemical_Chemical', [single]0.01))) 'BTX threshold affected another chemical need.'
Write-Output 'PASS: rare-tick heat transfer units and early BTX refill threshold.'
$offsetMethod = [Helodrace.Patch_HelodHeadOffset].GetMethod('OffsetForAge', [Reflection.BindingFlags]'Static,NonPublic')
Assert ($null -eq $offsetMethod.Invoke($null, @([single]20))) 'Missing head position settings must preserve vanilla position.'
function Head-Value([Verse.Gender]$gender, [single]$age) {
    $entry = $offsetMethod.Invoke($null, @($age))
    if ($gender -eq [Verse.Gender]::Female) { return $entry.female }
    return $entry.male
}
foreach ($entry in $settings.headOffsets.li) {
    $offsetEntry = [Helodrace.HelodHeadOffset]::new()
    foreach ($field in 'minAge', 'female', 'male') {
        $offsetEntry.$field = [single]::Parse($entry.$field, [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($entry.SelectSingleNode('absolute')) { $offsetEntry.absolute = [bool]::Parse($entry.absolute) }
    $fixtureSettings.headOffsets.Add($offsetEntry)
}
foreach ($entry in $settings.appendageOffsets.li) {
    $offsetEntry = [Helodrace.HelodAppendageOffset]::new()
    $offsetEntry.appendage = [Helodrace.HelodAppendage]::$($entry.appendage)
    foreach ($field in @(
        'north', 'south', 'east', 'west',
        'childNorth', 'childSouth', 'childEast', 'childWest',
        'babyNorth', 'babySouth', 'babyEast', 'babyWest')) {
        $node = $entry.SelectSingleNode($field)
        if ($null -eq $node) { continue }
        $parts = $node.InnerText.Trim('(', ')').Split(',')
        $offsetEntry.$field = [UnityEngine.Vector3]::new(
            [single]::Parse($parts[0], [Globalization.CultureInfo]::InvariantCulture),
            [single]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture),
            [single]::Parse($parts[2], [Globalization.CultureInfo]::InvariantCulture))
    }
    $fixtureSettings.appendageOffsets.Add($offsetEntry)
}
foreach ($case in @(@(0,-0.5), @(2.99,-0.5), @(3,-0.125), @(12.99,-0.125), @(13,0.265), @(18,0.265), @(40,0.265))) {
    $actual = Head-Value ([Verse.Gender]::Female) ([single]$case[0])
    Assert ([Math]::Abs($actual - $case[1]) -lt 0.000001) "Female head offset regression at age $($case[0])."
}
foreach ($case in @(@(0,-0.07), @(3,-1.2), @(13,0.265), @(18,0.265))) {
    $actual = Head-Value ([Verse.Gender]::Male) ([single]$case[0])
    Assert ([Math]::Abs($actual - $case[1]) -lt 0.000001) "Male head offset regression at age $($case[0])."
}
$customOffset = [Helodrace.HelodHeadOffset]::new()
$customOffset.minAge = 7
$customOffset.female = 0.42
$fixtureSettings.headOffsets.Add($customOffset)
Assert ([Math]::Abs((Head-Value ([Verse.Gender]::Female) 8) - 0.42) -lt 0.000001) 'Custom XML-style age/offset ignored.'
Assert ([Math]::Abs((Head-Value ([Verse.Gender]::Female) 18) - 0.265) -lt 0.000001) 'Unsorted entry overrode a later age threshold.'
$null = $fixtureSettings.headOffsets.Remove($customOffset)
# Exercise the rendering postfix, not just age lookup: existing base offsets must
# be replaced while X/Y survive; missing data and explicit zero are distinct.
$renderAge = New-Fixture ([Verse.Pawn_AgeTracker])
[Verse.Pawn].GetField('ageTracker').SetValue($fixturePawn, $renderAge)
[Verse.Pawn].GetField('gender').SetValue($fixturePawn, [Verse.Gender]::Female)
$ageTicksField = [Verse.Pawn_AgeTracker].GetField('ageBiologicalTicksInt', [Reflection.BindingFlags]'Instance,NonPublic')
foreach ($renderCase in @(@(13,0.304105), @(18,0.34))) {
    $ageTicksField.SetValue($renderAge, [long]($renderCase[0] * 3600000))
    $position = [UnityEngine.Vector3]::new(0.1, 0.02, [single]$renderCase[1])
    [Helodrace.Patch_HelodHeadOffset]::Postfix($fixturePawn, [ref]$position)
    Assert ([Math]::Abs($position.z - 0.265) -lt 0.000001) 'Head Z still depends on vanilla age/body-size offset.'
    Assert ([Math]::Abs($position.x - 0.1) -lt 0.000001 -and [Math]::Abs($position.y - 0.02) -lt 0.000001) 'Head X/Y were changed.'
}
$customOffset.minAge = 18
$customOffset.female = 0
$customOffset.absolute = $true
$fixtureSettings.headOffsets.Add($customOffset)
$position = [UnityEngine.Vector3]::new(0, 0, 0.34)
[Helodrace.Patch_HelodHeadOffset]::Postfix($fixturePawn, [ref]$position)
Assert ($position.z -eq 0) 'Explicit zero position fell back to vanilla.'
$savedOffsets = $fixtureSettings.headOffsets.ToArray()
$fixtureSettings.headOffsets.Clear()
$position = [UnityEngine.Vector3]::new(0, 0, 0.34)
[Helodrace.Patch_HelodHeadOffset]::Postfix($fixturePawn, [ref]$position)
Assert ([Math]::Abs($position.z - 0.34) -lt 0.000001) 'Missing configuration overwrote vanilla position.'
$fixtureSettings.headOffsets.AddRange($savedOffsets)
$null = $fixtureSettings.headOffsets.Remove($customOffset)
Write-Output 'PASS: direct head Z at 13/18, preserved X/Y, explicit zero and missing configuration.'
$appendageMethod = [Helodrace.PawnRenderNodeWorker_HelodAppendage].GetMethod('TryGetConfiguredOffset', [Reflection.BindingFlags]'Static,NonPublic')
foreach ($case in @(
    @([Helodrace.HelodAppendage]::Tail, [Verse.Rot4]::East, -0.095),
    @([Helodrace.HelodAppendage]::LeftEar, [Verse.Rot4]::West, -0.115),
    @([Helodrace.HelodAppendage]::RightEar, [Verse.Rot4]::South, -0.13))) {
    $args = @($case[0], $case[1], [UnityEngine.Vector3]::zero)
    Assert ($appendageMethod.Invoke($null, $args)) "Missing appendage offset for $($case[0])/$($case[1])."
    Assert ([Math]::Abs($args[2].x - $case[2]) -lt 0.000001) "Appendage X offset regression for $($case[0])/$($case[1])."
}
$tailOffset = $null
foreach ($candidate in $fixtureSettings.appendageOffsets) {
    if ($candidate.appendage -eq [Helodrace.HelodAppendage]::Tail) {
        $tailOffset = $candidate
        break
    }
}
Assert ($null -ne $tailOffset) 'Tail offset fixture missing.'
$developmentalMethod = [Helodrace.PawnRenderNodeWorker_HelodAppendage].GetMethod('DevelopmentalOffsetFor', [Reflection.BindingFlags]'Static,NonPublic')
foreach ($case in @(
    @([Verse.DevelopmentalStage]::Baby, [Verse.Rot4]::East, 0.07, -0.05),
    @([Verse.DevelopmentalStage]::Child, [Verse.Rot4]::West, -0.025, 0.02),
    @([Verse.DevelopmentalStage]::Adult, [Verse.Rot4]::North, 0, 0))) {
    $actual = $developmentalMethod.Invoke($null, @($tailOffset, $case[0], $case[1]))
    Assert ([Math]::Abs($actual.x - $case[2]) -lt 0.000001 -and [Math]::Abs($actual.z - $case[3]) -lt 0.000001) "Developmental tail offset regression for $($case[0])/$($case[1])."
}
$args = @([Helodrace.HelodAppendage]::Tail, [Verse.Rot4]::North, [UnityEngine.Vector3]::zero)
$fixtureSettings.appendageOffsets.Clear()
Assert (!$appendageMethod.Invoke($null, $args)) 'Missing appendage settings should not create an offset.'
Write-Output 'PASS: XML appendage offsets, developmental tail offsets, and missing-configuration fallback.'
$earMethod = [Helodrace.PawnRenderNodeWorker_HelodAppendage].GetMethod('MatchesEar', [Reflection.BindingFlags]'Static,NonPublic')
$ear = [Verse.BodyPartRecord]::new()
$ear.def = New-Fixture ([Verse.BodyPartDef])
[Verse.Def].GetField('defName').SetValue($ear.def, 'Ear')
foreach ($side in 'left', 'right') {
    $ear.customLabel = 'translated ear label'
    $ear.untranslatedCustomLabel = "$side ear"
    Assert ($earMethod.Invoke($null, @($ear, "$side ear"))) 'Translated ear must remain visible.'
    Assert (!$earMethod.Invoke($null, @($ear, 'other ear'))) 'Opposite ear must not match.'
    $ear.customLabel = "$side ear"
    $ear.untranslatedCustomLabel = $null
    Assert ($earMethod.Invoke($null, @($ear, "$side ear"))) 'Untranslated ear fallback failed.'
}
Write-Output 'PASS: head offsets at age boundaries and language-independent left/right ear matching.'
# Populate policy fixtures from the shipped XML and exercise the real cache builder.
$thingFixtures = @{}
foreach ($listName in 'apparelList','whiteApparelList','earCoveringApparel') {
    $list = $fixtureSettings.$listName
    $list.Clear()
    foreach ($entry in $settings.SelectNodes("$listName/li")) {
        $name = $entry.InnerText
        if (!$thingFixtures.ContainsKey($name)) {
            $item = New-Fixture ([Verse.ThingDef])
            [Verse.Def].GetField('defName').SetValue($item, $name)
            [Verse.DefDatabase[Verse.ThingDef]]::Add($item)
            $thingFixtures[$name] = $item
        }
        $list.Add($thingFixtures[$name])
    }
}
foreach ($listName in 'headTypes','xenotypeList','blackGeneList') {
    $list = $fixtureSettings.$listName
    $list.Clear()
    $type = $list.GetType().GetGenericArguments()[0]
    foreach ($entry in $settings.SelectNodes("$listName/li")) {
        $item = New-Fixture $type
        [Verse.Def].GetField('defName').SetValue($item, $entry.InnerText)
        $list.Add($item)
    }
}
$fixtureSettings.blackEndoCategories.Clear()
$categoryType = $fixtureSettings.blackEndoCategories.GetType().GetGenericArguments()[0]
foreach ($entry in $settings.SelectNodes('blackEndoCategories/li')) {
    $fixtureSettings.blackEndoCategories.Add([Enum]::Parse($categoryType, $entry.InnerText))
}
$fixtureSettings.apparelGraphics.Clear()
foreach ($entry in $settings.apparelGraphics.li) {
    $graphic = [Helodrace.HelodApparelGraphic]::new()
    $graphic.key = $thingFixtures[$entry.key]
    if ($null -eq $graphic.key) {
        $graphic.key = New-Fixture ([Verse.ThingDef])
        [Verse.Def].GetField('defName').SetValue($graphic.key, $entry.key)
    }
    $graphic.value = $entry.value
    $fixtureSettings.apparelGraphics.Add($graphic)
}
$hairType = $assembly.GetType('Helodrace.HelodRace').GetField('HelodHairs', [Reflection.BindingFlags]'Static,NonPublic').FieldType.GetGenericArguments()[0]
foreach ($name in 'TestHairA','TestHairB') {
    $hair = New-Fixture $hairType
    [Verse.Def].GetField('defName').SetValue($hair, $name)
    $hair.styleTags = [Collections.Generic.List[string]]::new()
    $hair.styleTags.Add('HelodHair')
    [Verse.DefDatabase[RimWorld.HairDef]]::Add($hair)
}
function Get-RaceCache([string]$name) {
    return ,([Helodrace.HelodRace].GetField($name, [Reflection.BindingFlags]'Static,NonPublic').GetValue($null))
}
$permittedNames = @(@($baseline.policy.apparelList) + @($baseline.policy.whiteApparelList) | ForEach-Object { $_.Split('|')[0] } | Sort-Object -Unique)
for ($repeat = 0; $repeat -lt 2; $repeat++) {
    [Helodrace.HelodRace]::RebuildRuntimeCaches()
    foreach ($pair in @(@('permittedApparel',$permittedNames.Count), @('exclusiveApparel',$baseline.policy.apparelList.Count), @('EarCoveringApparel',$baseline.policy.earCoveringApparel.Count), @('HeadTypes',$baseline.policy.headTypes.Count), @('ForbiddenGenes',$baseline.policy.blackGeneList.Count), @('ForbiddenCategories',$baseline.policy.blackEndoCategories.Count), @('HelodXenotypes',$baseline.policy.xenotypeList.Count), @('apparelPaths',$baseline.policy.apparelGraphics.Count))) {
        Assert ((Get-RaceCache $pair[0]).Count -eq $pair[1]) "Cache count mismatch: $($pair[0])"
    }
    Assert (((Get-RaceCache 'HelodHairs') | ForEach-Object defName) -join '|' -eq 'TestHairA|TestHairB') 'Hair cache order changed.'
    Assert ([Math]::Abs([Helodrace.HelodRace]::DrawScale - 0.8) -lt 0.000001) 'Draw scale changed.'
}
$fixtureSettings.earCoveringApparel.Clear()
[Helodrace.HelodRace]::RebuildRuntimeCaches()
Assert ((Get-RaceCache 'EarCoveringApparel').Count -eq 0) 'Empty policy retained stale cache entries.'
Assert (![Helodrace.HelodCoveredEarsUtility]::IsWearingEarCoveringApparel($null)) 'Null pawn ear-cover check failed.'
Write-Output 'PASS: XML policy cache counts, ordered hair choices, repeated rebuild and empty policy.'
if ($PatchSmokeTest) {
    $harmony = [HarmonyLib.Harmony]::new('Helodrace.Standalone.SmokeTest')
    try {
        foreach ($type in $types) {
            $null = $harmony.CreateClassProcessor($type).Patch()
            Write-Output "PASS: Harmony installed $($type.Name)"
        }
    } finally { $harmony.UnpatchAll('Helodrace.Standalone.SmokeTest') }
}
Write-Output 'PASS: standalone static verification complete. In-game visual/play testing is still required.'
