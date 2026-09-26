param([string]$AssemblyPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Assemblies/Helodrace.dll'),
    [string]$RimWorldPath = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
function Assert($condition, $message) { if (!$condition) { throw $message } }
foreach ($file in Get-ChildItem "$repo/Defs", "$repo/Patches", "$repo/Languages" -Filter *.xml -Recurse) {
    $null = [xml](Get-Content $file.FullName -Raw -Encoding UTF8)
}
[xml]$vests = Get-Content "$repo/Defs/WildWest/Items/Apparel_WildWest.xml" -Raw
foreach ($name in 'HD_WildWestLeatherVest', 'HD_WildWestMetalVest') {
    Assert ($vests.SelectSingleNode('/Defs/ThingDef[defName="' + $name + '"]/apparel/layers/li').InnerText -eq 'Shell') "$name is not outerwear."
}
[xml]$apparel = Get-Content "$repo/Defs/ModernWar/Items/Apparel_ModernWar.xml" -Raw
$belt = $apparel.SelectSingleNode('/Defs/ThingDef[defName="HD_Apparel_MOLLEBattleBelt"]/apparel/wornGraphicPath').InnerText
foreach ($direction in 'north','east','south') {
    Assert (Test-Path "$repo/Textures/${belt}_${direction}.png") "Missing MOLLE belt texture: $direction"
}
Write-Output 'PASS: XML parses, both vests use Shell, MOLLE pack paths exist.'

[xml]$armorDefs = Get-Content "$repo/Defs/ModernWar/ModularArmorParts.xml" -Raw -Encoding UTF8
[xml]$partItems = Get-Content "$repo/Defs/ModernWar/Items/ModularArmorPartItems.xml" -Raw -Encoding UTF8
foreach ($definitions in $armorDefs,$partItems) {
    foreach ($node in $definitions.SelectNodes('//uiIconPath | //graphicData/texPath | //northUnderGraphicData/texPath')) {
        $path = [string]$node.InnerText
        $exists = Test-Path -LiteralPath "$repo/Textures/$path.png"
        foreach ($suffix in '_north','_east','_south','_west') {
            $exists = $exists -or (Test-Path -LiteralPath "$repo/Textures/${path}${suffix}.png")
        }
        Assert $exists "Missing standalone armor texture: $path"
    }
}
Assert (!(Test-Path "$repo/Defs/ModernWar/ModularArmorTextures.xml")) 'Merged armor sheets are still configured.'
Write-Output 'PASS: armor graphics and icons resolve to individual PNG files.'

$managed = Join-Path $RimWorldPath 'RimWorldWin64_Data/Managed'
foreach ($name in 'UnityEngine.CoreModule','UnityEngine','Assembly-CSharp') {
    $null = [Reflection.Assembly]::LoadFrom("$managed/$name.dll")
}
$null = [Reflection.Assembly]::LoadFrom("$repo/Assemblies/0Harmony.dll")
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
function Fixture([type]$type) { [Runtime.Serialization.FormatterServices]::GetUninitializedObject($type) }
function Set-Field($object, [type]$owner, $name, $value) { $owner.GetField($name,[Reflection.BindingFlags]'Instance,Public,NonPublic').SetValue($object,$value) }
# Exercise actual runtime path selection for every configured part, panel,
# mounting position and facing, including the split front/back graphics.
$textureRoutes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$panels = @{}
foreach ($panelXml in $armorDefs.SelectNodes('/Defs/Helodrace.ModernWar.ModularArmorPalsPanelDef')) {
    $panel = [Helodrace.ModernWar.ModularArmorPalsPanelDef]::new()
    foreach ($child in $panelXml.ChildNodes) {
        $field = $panel.GetType().GetField($child.Name)
        if ($field -and $field.FieldType -in [string],[int],[bool]) {
            $value = [Convert]::ChangeType($child.InnerText,$field.FieldType)
            $field.SetValue($panel,$value)
        }
    }
    $panels[$panelXml.defName] = $panel
}
$textureComp = Fixture ([Helodrace.ModernWar.CompModularArmor])
$texturePathMethod = [Helodrace.ModernWar.CompModularArmor].GetMethod('AuthoredPalsTexturePath', [type[]]@([Helodrace.ModernWar.InstalledModularArmorPart],[bool],[Verse.Rot4]))
foreach ($partXml in $armorDefs.SelectNodes('/Defs/Helodrace.ModernWar.ModularArmorPartDef[authoredPalsTextureKey]')) {
    $part = [Helodrace.ModernWar.ModularArmorPartDef]::new()
    $part.authoredPalsTextureKey = [string]$partXml.authoredPalsTextureKey
    $part.installMode = [Helodrace.ModernWar.ModularArmorInstallMode]::Positionable
    $part.palsWidth = [int]$partXml.palsWidth
    $part.fillNarrowPalsPanel = [string]$partXml.fillNarrowPalsPanel -eq 'true'
    foreach ($panelName in $partXml.allowedPalsPanels.li) {
        $panel = $panels[[string]$panelName]
        for ($x = 0; $x -le $panel.columns - $part.PalsWidthFor($panel); $x++) {
            $record = [Helodrace.ModernWar.InstalledModularArmorPart]::new()
            $record.part = $part; $record.palsPanel = $panel; $record.palsX = $x
            $equipment = [Collections.Generic.List[Helodrace.ModernWar.InstalledModularArmorPart]]::new()
            $equipment.Add($record)
            Set-Field $textureComp ([Helodrace.ModernWar.CompModularArmor]) 'installedParts' $equipment
            foreach ($facing in [Verse.Rot4]::North,[Verse.Rot4]::East,[Verse.Rot4]::South,[Verse.Rot4]::West) {
                foreach ($back in $false,$true) {
                    $path = $texturePathMethod.Invoke($textureComp,[object[]]@($record,$back,$facing))
                    if ($path) {
                        Assert (Test-Path -LiteralPath "$repo/Textures/$path.png") "Broken runtime PALS path: $path"
                        $null = $textureRoutes.Add($path)
                    }
                }
            }
        }
    }
}
Write-Output "PASS: $($textureRoutes.Count) runtime PALS texture paths resolve across parts, panels, positions and facings."

$race = Fixture ([Verse.ThingDef]); Set-Field $race ([Verse.Def]) 'defName' 'Helod'
[Helodrace.HelodRace].GetField('<RaceDef>k__BackingField',[Reflection.BindingFlags]'Static,NonPublic').SetValue($null,$race)
$pawn = Fixture ([Verse.Pawn]); Set-Field $pawn ([Verse.Thing]) 'def' $race
[RimWorld.DefOfHelper].GetField('bindingNow',[Reflection.BindingFlags]'Static,NonPublic').SetValue($null,$true)
$shell = Fixture ([Verse.ApparelLayerDef]); $shell.defName = 'Shell'
[RimWorld.ApparelLayerDefOf]::Shell = $shell
$rigDef = Fixture ([Verse.ThingDef]); Set-Field $rigDef ([Verse.Def]) 'defName' 'HD_Apparel_GreatWarStormFrontChestRig'
[Verse.DefDatabase[Verse.ThingDef]]::Add($rigDef)
$beltDef = Fixture ([Verse.ThingDef]); Set-Field $beltDef ([Verse.Def]) 'defName' 'HD_Apparel_MOLLEBattleBelt'
[Verse.DefDatabase[Verse.ThingDef]]::Add($beltDef)
[Helodrace.PatchHelodShellApparelLayer].GetMethod('RebuildCache',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@())
$tree = Fixture ([Verse.PawnRenderTree]); $tree.pawn = $pawn
function Node([type]$type, [type]$worker, [single]$layer, $def) {
    $node = Fixture $type
    $props = [Verse.PawnRenderNodeProperties]::new(); $props.workerClass = $worker; $props.baseLayer = $layer
    [Verse.PawnRenderNode].GetField('props',[Reflection.BindingFlags]'Instance,NonPublic').SetValue($node,$props)
    Set-Field $node ([Verse.PawnRenderNode]) 'tree' $tree
    if ($def) {
        $gear = Fixture ([RimWorld.Apparel]); Set-Field $gear ([Verse.Thing]) 'def' $def
        Set-Field $node ([Verse.PawnRenderNode]) 'apparel' $gear
    }
    return $node
}
$coatDef = Fixture ([Verse.ThingDef]); $coatApparel = [RimWorld.ApparelProperties]::new()
$coatApparel.layers = [Collections.Generic.List[Verse.ApparelLayerDef]]::new(); $coatApparel.layers.Add($shell)
Set-Field $coatDef ([Verse.ThingDef]) 'apparel' $coatApparel
$root = Node ([Verse.PawnRenderNode]) ([Verse.PawnRenderNodeWorker]) 0 $null
$hair = Node ([Helodrace.PawnRenderNode_HelodHair]) ([Verse.PawnRenderNodeWorker_FlipWhenCrawling]) 62 $null
$coat = Node ([Verse.PawnRenderNode_Apparel]) ([Verse.PawnRenderNodeWorker_Apparel_Body]) 22 $coatDef
$coatProps = [Verse.PawnRenderNode].GetProperty('Props').GetValue($coat)
$coatProps.drawData = [Verse.DrawData]::NewWithData([Verse.DrawData+RotationalData]::new([Verse.Rot4]::North,88))
$rig = Node ([Verse.PawnRenderNode_Apparel]) ([Verse.PawnRenderNodeWorker_Apparel_Body]) 23 $rigDef
$beltNode = Node ([Verse.PawnRenderNode_Apparel]) ([Verse.PawnRenderNodeWorker_Apparel_Body]) 24 $beltDef
$beltProps = [Verse.PawnRenderNode].GetProperty('Props').GetValue($beltNode)
$beltProps.drawData = [Verse.DrawData]::NewWithData([Verse.DrawData+RotationalData]::new([Verse.Rot4]::North,93))
$beltPart = Node ([Helodrace.ModernWar.PawnRenderNode_ModularArmorPart]) ([Helodrace.ModernWar.PawnRenderNodeWorker_ModularArmorPart]) 93 $beltDef
$partDef = [Helodrace.ModernWar.ModularArmorPartDef]::new()
$partDef.fixedPosition = [Helodrace.ModernWar.ModularArmorPositionDef]::new(); $partDef.fixedPosition.drawLayer = 93
$installed = [Helodrace.ModernWar.InstalledModularArmorPart]::new(); $installed.part = $partDef
Set-Field $beltPart ([Helodrace.ModernWar.PawnRenderNode_ModularArmorPart]) 'Installed' $installed
Set-Field $beltPart ([Helodrace.ModernWar.PawnRenderNode_ModularArmorPart]) 'Comp' (Fixture ([Helodrace.ModernWar.CompModularArmor]))
Set-Field $root ([Verse.PawnRenderNode]) 'children' ([Verse.PawnRenderNode[]]@($coat,$hair,$rig,$beltNode,$beltPart)); $tree.rootNode = $root
$parms = [Verse.PawnDrawParms]::new(); $parms.pawn = $pawn
$harmony = [HarmonyLib.Harmony]::new('Helodrace.ApparelRegression')
$null = $harmony.CreateClassProcessor([Helodrace.PatchHelodShellApparelLayer]).Patch()
$null = $harmony.CreateClassProcessor([Helodrace.HelodApparelLayerDiagnostics]).Patch()
foreach ($facing in [Verse.Rot4]::North,[Verse.Rot4]::East,[Verse.Rot4]::West,[Verse.Rot4]::South) {
    $parms.facing = $facing
    $coatWorker = [Verse.PawnRenderNode].GetProperty('Worker').GetValue($coat)
    $rigWorker = [Verse.PawnRenderNode].GetProperty('Worker').GetValue($rig)
    $hairWorker = [Verse.PawnRenderNode].GetProperty('Worker').GetValue($hair)
    $beltWorker = [Verse.PawnRenderNode].GetProperty('Worker').GetValue($beltNode)
    $beltPartWorker = [Verse.PawnRenderNode].GetProperty('Worker').GetValue($beltPart)
    $coatLayer = $coatWorker.LayerFor($coat,$parms)
    $rigLayer = $rigWorker.LayerFor($rig,$parms)
    Assert ($rigLayer -gt $coatLayer) "Chest rig is below coat: $facing"
    Assert ($beltWorker.LayerFor($beltNode,$parms) -lt $coatLayer) "MOLLE belt is above coat: $facing"
    Assert ($beltPartWorker.LayerFor($beltPart,$parms) -lt $coatLayer) "MOLLE attachment is above coat: $facing"
    if ($facing -eq [Verse.Rot4]::North) {
        $hairLayer = $hairWorker.LayerFor($hair,$parms)
        $layerAltitude = 0.0003658537
        $meshBackLift = 0.0018292684
        # Use the vanilla backLift triangles and a representative overlapping
        # point, with adult head/body Z offset 0.465 and plane height 1.2.
        # This reproduces the old one-layer failure without a GPU or renderer.
        $coatV = (0.3 / 1.2) + 0.5
        $hairV = ((0.3 - 0.465) / 1.2) + 0.5
        $coatVertexY = $meshBackLift * $coatV
        $hairVertexY = $meshBackLift * $hairV + 0.0007317074 * (0.5 - $hairV)
        Assert ($coatVertexY - $hairVertexY -gt $layerAltitude) 'Fixture does not reproduce the previous surface overlap.'
        Assert (($hairLayer - $coatLayer) * $layerAltitude -gt $meshBackLift) 'North coat mesh can intersect the hair depth range.'
        Assert (($hairLayer - $rigLayer) * $layerAltitude -gt $meshBackLift) 'Raised chest rig mesh can intersect the hair depth range.'
    }
}
Write-Output 'PASS: old one-layer gap reproduces surface overlap; north hair clears the entire coat/rig mesh depth range; chest rig stays above coats in all four directions.'
$parms.facing = [Verse.Rot4]::North
$hairMatrix = (Fixture ([UnityEngine.Matrix4x4])); $hairMatrix.m13 = 0.03
$coatMatrix = (Fixture ([UnityEngine.Matrix4x4])); $coatMatrix.m13 = 0.02
[Helodrace.HelodApparelLayerDiagnostics]::Postfix($tree,$hair,$parms,$hairMatrix,$true)
[Helodrace.HelodApparelLayerDiagnostics]::Postfix($tree,$coat,$parms,$coatMatrix,$true)
$snapshots = [Helodrace.HelodApparelLayerDiagnostics].GetField('Snapshots',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$snapshotArgs = [object[]]@($tree,$null)
Assert ($snapshots.GetType().GetMethod('TryGetValue').Invoke($snapshots,$snapshotArgs)) 'Final-matrix diagnostic did not capture the pawn.'
$rows = $snapshotArgs[1].GetType().GetField('rows').GetValue($snapshotArgs[1])
Assert ($rows.Count -eq 2) 'Final-matrix diagnostic missed hair or coat.'
Write-Output 'PASS: final-matrix diagnostic captures both hair and coat without changing the renderer.'
Set-Field $root ([Verse.PawnRenderNode]) 'children' ([Verse.PawnRenderNode[]]@($hair,$beltNode,$beltPart))
$parms.facing = [Verse.Rot4]::North
Assert ($beltWorker.LayerFor($beltNode,$parms) -eq 93) 'MOLLE depth changed without a coat.'
Write-Output 'PASS: MOLLE belt and attachment remain below coats in all four directions; no-coat depth is preserved; matrix diagnostic patch installs.'
