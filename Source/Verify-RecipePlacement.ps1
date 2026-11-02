param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))

$ErrorActionPreference = 'Stop'

function Assert([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}

function Values($Node, [string]$XPath) {
    return @($Node.SelectNodes($XPath) | ForEach-Object { $_.InnerText.Trim() })
}

$xmlFiles = Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'Defs') -Recurse -Filter *.xml
$documents = foreach ($file in $xmlFiles) {
    [pscustomobject]@{ File = $file; Xml = [xml](Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8) }
}

$thingDefs = @{}
$recipes = @{}
foreach ($document in $documents) {
    foreach ($node in @($document.Xml.SelectNodes('/Defs/ThingDef[defName]'))) {
        $name = $node.SelectSingleNode('defName').InnerText.Trim()
        Assert (!$thingDefs.ContainsKey($name)) "Duplicate ThingDef: $name"
        $thingDefs[$name] = $node
    }

    foreach ($node in @($document.Xml.SelectNodes('/Defs/RecipeDef[defName]'))) {
        $name = $node.SelectSingleNode('defName').InnerText.Trim()
        Assert (!$recipes.ContainsKey($name)) "Duplicate RecipeDef: $name"
        $recipes[$name] = [pscustomobject]@{
            Name = $name
            Users = @(Values $node 'recipeUsers/li')
            Research = @((Values $node 'researchPrerequisites/li') + (Values $node 'researchPrerequisite')) | Where-Object { $_ }
            File = $document.File.FullName
            Node = $node
        }
    }
}

foreach ($recipe in $recipes.Values) {
    Assert ($recipe.Users.Count -gt 0) "Orphan RecipeDef without recipeUsers: $($recipe.Name)"
    $duplicateUsers = @($recipe.Users | Group-Object | Where-Object Count -gt 1)
    Assert ($duplicateUsers.Count -eq 0) "Duplicate recipeUsers in $($recipe.Name): $($duplicateUsers.Name -join ', ')"
    foreach ($user in $recipe.Users | Where-Object { $_ -like 'HD_*' }) {
        Assert ($thingDefs.ContainsKey($user)) "Unknown Helod worktable '$user' in $($recipe.Name)"
        $worktableClass = [string]$thingDefs[$user].thingClass
        Assert ($worktableClass -match 'WorkTable') "Recipe user '$user' is not a concrete worktable ($($recipe.Name))"
    }
}

foreach ($document in $documents) {
    Assert ($document.Xml.OuterXml -notmatch '\bHD_(?:LowQualityBarrel|ShotgunBarrel|WildWestBarrel)\b|\bHD_Make(?:LowQuality|Shotgun|WildWest)Barrel(?:_BasicWorkbench)?\b') "Removed barrel reference remains in $($document.File.FullName)"
}
$foldedGunCosts = @{
    HD_Make_Gun_SprMOne_Weapon = @(50, 1, 37500)
    HD_Make_Gun_WinM1887_Weapon = @(75, 3, 58800)
    HD_Make_Gun_Henry_Weapon = @(75, 4, 56100)
}
foreach ($entry in $foldedGunCosts.GetEnumerator()) {
    $node = $recipes[$entry.Key].Node
    Assert ($node.SelectSingleNode('ingredients/li[filter/thingDefs/li="HD_CarbonSteel"]/count').InnerText -eq $entry.Value[0]) "Folded barrel steel mismatch in $($entry.Key)"
    Assert ($node.SelectSingleNode('ingredients/li[filter/thingDefs/li="HD_GeneralMachinePart"]/count').InnerText -eq $entry.Value[1]) "Folded barrel machine part mismatch in $($entry.Key)"
    Assert ($node.workAmount -eq $entry.Value[2]) "Folded barrel machining work mismatch in $($entry.Key)"
}

$componentUsers = [ordered]@{
    HD_ConvertIndustrialComponentToGeneralMachinePart = @('HD_BasicWorkbench')
    HD_MakePressComponent = @('HD_LineShaftHydraulicPress')
    HD_MillGeneralMachinePart = @('HD_LineShaftMillingMachine')
    HD_MakeShaft = @('HD_TreadleLathe', 'HD_LineShaftTurretLathe')
    HD_RollUniformSteelPlate = @('HD_LineShaftRollingMachine')
    HD_MakeBasicElectricalComponents = @('HD_BasicWorkbench')
    HD_MakeBasicElectronicComponents = @('HD_BasicWorkbench')
}
foreach ($baseName in $componentUsers.Keys) {
    $baseline = $recipes[$baseName]
    Assert ($null -ne $baseline) "Missing main recipe: $baseName"
    foreach ($quantity in @(1, 5, 10)) {
        $name = if ($quantity -eq 1) { $baseName } else { $baseName + 'Bulk' + $quantity }
        $candidate = $recipes[$name]
        Assert ($null -ne $candidate) "Missing family recipe: $name"
        Assert (($candidate.Users -join '|') -ceq ($baseline.Users -join '|')) "recipeUsers drift in $name"
        Assert (($candidate.Research -join '|') -ceq ($baseline.Research -join '|')) "research prerequisite drift in $name"
        Assert (($candidate.Users -join '|') -ceq ($componentUsers[$baseName] -join '|')) "Wrong primary worktable in $name"
        Assert ($candidate.Node.unfinishedThingDef -eq 'HD_UnfinishedIndustrialBatch') "Component progress is not preserved in $name"
        $workFactor = if ($quantity -eq 1) { 1 } elseif ($quantity -eq 5) { 4.5 } else { 8 }
        Assert ([double]$candidate.Node.workAmount -eq [double]$baseline.Node.workAmount * $workFactor) "Batch labor discount mismatch in $name"
        $baseIngredients = @($baseline.Node.SelectNodes('ingredients/li'))
        $batchIngredients = @($candidate.Node.SelectNodes('ingredients/li'))
        Assert ($baseIngredients.Count -eq $batchIngredients.Count) "Batch ingredient count mismatch in $name"
        for ($i=0; $i -lt $baseIngredients.Count; $i++) {
            Assert ($baseIngredients[$i].filter.InnerText -ceq $batchIngredients[$i].filter.InnerText) "Batch material filter mismatch in $name"
            Assert ([double]$batchIngredients[$i].count -eq [double]$baseIngredients[$i].count * $quantity) "Batch material discount is forbidden in $name"
        }
        Assert ([double]$candidate.Node.products.FirstChild.InnerText -eq [double]$baseline.Node.products.FirstChild.InnerText * $quantity) "Batch output mismatch in $name"
    }
}

$expectedUsers = [ordered]@{
    HD_MakePressComponent = @('HD_LineShaftHydraulicPress')
    HD_MakePressComponentBulk5 = @('HD_LineShaftHydraulicPress')
    HD_MakePressComponentBulk10 = @('HD_LineShaftHydraulicPress')
    HD_MillGeneralMachinePart = @('HD_LineShaftMillingMachine')
    HD_MillGeneralMachinePartBulk5 = @('HD_LineShaftMillingMachine')
    HD_MillGeneralMachinePartBulk10 = @('HD_LineShaftMillingMachine')
    HD_RollUniformSteelPlate = @('HD_LineShaftRollingMachine')
}
foreach ($entry in $expectedUsers.GetEnumerator()) {
    Assert ($recipes.ContainsKey($entry.Key)) "Missing mapped recipe: $($entry.Key)"
    Assert (($recipes[$entry.Key].Users -join '|') -ceq ($entry.Value -join '|')) "Wrong worktable mapping: $($entry.Key)"
}

$blastFurnace = $thingDefs['HD_BlastFurnace']
$converter = $thingDefs['HD_Converter']
Assert ($null -ne $blastFurnace -and $null -ne $converter) 'Blast furnace and converter must exist.'
Assert ((Values $blastFurnace 'comps/li[@Class="CompProperties_AffectedByFacilities"]/linkableFacilities/li' -join '|') -ceq 'HD_Converter') 'Blast furnace must link to converters.'
Assert ($null -ne $converter.SelectSingleNode('comps/li[@Class="CompProperties_Facility"]')) 'Converter must be a vanilla facility.'
foreach ($size in @('Small', 'Medium', 'Large')) {
    $pig = $recipes["HD_CastPigIron$size"]
    $steel = $recipes["HD_RefineCarbonSteel$size"]
    Assert ($null -ne $pig -and $null -ne $steel) "Missing $size ironmaking recipes."
    Assert (($pig.Users -join '|') -ceq 'HD_BlastFurnace') "Pig iron $size must be made at the blast furnace."
    Assert ($null -ne $pig.Node.SelectSingleNode('products/HD_PigIron')) "Pig iron $size has the wrong product."
    Assert (($steel.Users -join '|') -ceq 'HD_BlastFurnace') "Carbon steel $size must run at the fueled blast furnace."
    foreach ($recipe in @($pig, $steel)) {
        Assert ((Values $recipe.Node 'unfinishedThingDef' -join '|') -ceq 'HD_UnfinishedSmeltingBatch') "$($recipe.Name) must preserve smelting progress across interruptions."
    }
    Assert ($null -ne $steel.Node.SelectSingleNode('products/HD_CarbonSteel')) "Carbon steel $size has the wrong product."
    Assert ((Values $steel.Node 'workerClass' -join '|') -ceq 'Helodrace.RecipeWorker_RequiresLinkedFacility') "Carbon steel $size must check the live facility connection."
    Assert ((Values $steel.Node 'modExtensions/li/workTable' -join '|') -ceq 'HD_BlastFurnace') "Carbon steel $size has the wrong worktable requirement."
    Assert ((Values $steel.Node 'modExtensions/li/facility' -join '|') -ceq 'HD_Converter') "Carbon steel $size has the wrong facility requirement."
}

$m1 = $thingDefs['HD_Apparel_GreatWarM1Helmet']
Assert ($null -ne $m1) 'Missing M1 helmet ThingDef.'
$m1Users = @(Values $m1 'recipeMaker/recipeUsers/li')
Assert (($m1Users -join '|') -ceq 'HD_LineShaftHydraulicPress|HD_HydraulicPress1970Bench') 'M1 helmet must be made at the authored hydraulic presses.'

$mappedRecipeNames = @($expectedUsers.Keys)
$chemicalJobs = [xml](Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Defs/GreatWar/Jobs_GreatWar.xml') -Raw -Encoding UTF8)
$reactorGiver = $chemicalJobs.SelectSingleNode('/Defs/WorkGiverDef[defName="DoBillsHD_HaberBoschHighPressureReactor"]')
Assert ($reactorGiver.giverClass -eq 'WorkGiver_DoBill' -and $reactorGiver.workType -eq 'Crafting' -and $reactorGiver.fixedBillGiverDefs.li -eq 'HD_HaberBoschHighPressureReactor') 'High-pressure reactor must have a Crafting bill work giver.'
Assert (($recipes['HD_SynthesizeAmmoniumNitrate'].Users -join '|') -ceq 'HD_HaberBoschHighPressureReactor') 'Ammonium nitrate must run at the high-pressure reactor.'
Assert ($recipes['HD_SynthesizeAmmoniumNitrate'].Node.requiredGiverWorkType -eq $reactorGiver.workType) 'Reactor recipe and work giver work types must agree.'
Write-Output 'PASS: high-pressure reactor recipe, work type and bill work giver are connected.'
Write-Output "PASS: $($recipes.Count) explicit RecipeDefs and $($thingDefs.Count) concrete ThingDefs are unique."
Write-Output "PASS: no orphan recipes; all Helod recipeUsers resolve to concrete worktables and contain no duplicate entries."
Write-Output "PASS: base/bulk recipe families share worktables and research prerequisites."
Write-Output "PASS: $($mappedRecipeNames.Count) process recipes and the M1 helmet match Docs/Recipe_Worktable_Mapping.md."
Write-Output 'PASS: three pig iron and facility-gated carbon steel recipes have the expected products and worktable listings.'
