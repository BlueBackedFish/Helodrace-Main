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

$families = @(
    @('HD_MakePressComponent', 'HD_MakePressComponentBulk5', 'HD_MakePressComponentBulk10'),
    @('HD_MillGeneralMachinePart', 'HD_MillGeneralMachinePartBulk5', 'HD_MillGeneralMachinePartBulk10'),
    @('HD_TurnGeneralMachinePart', 'HD_TurnGeneralMachinePartBulk5', 'HD_TurnGeneralMachinePartBulk10')
)
foreach ($family in $families) {
    $baseline = $recipes[$family[0]]
    Assert ($null -ne $baseline) "Missing main recipe: $($family[0])"
    foreach ($name in $family) {
        $candidate = $recipes[$name]
        Assert ($null -ne $candidate) "Missing family recipe: $name"
        Assert (($candidate.Users -join '|') -ceq ($baseline.Users -join '|')) "recipeUsers drift in $name"
        Assert (($candidate.Research -join '|') -ceq ($baseline.Research -join '|')) "research prerequisite drift in $name"
    }
}

$expectedUsers = [ordered]@{
    HD_MakePressComponent = @('HD_LineShaftHydraulicPress')
    HD_MakePressComponentBulk5 = @('HD_LineShaftHydraulicPress')
    HD_MakePressComponentBulk10 = @('HD_LineShaftHydraulicPress')
    HD_MillGeneralMachinePart = @('HD_LineShaftMillingMachine')
    HD_MillGeneralMachinePartBulk5 = @('HD_LineShaftMillingMachine')
    HD_MillGeneralMachinePartBulk10 = @('HD_LineShaftMillingMachine')
    HD_TurnGeneralMachinePart = @('HD_TreadleLathe', 'HD_LineShaftTurretLathe')
    HD_TurnGeneralMachinePartBulk5 = @('HD_TreadleLathe', 'HD_LineShaftTurretLathe')
    HD_TurnGeneralMachinePartBulk10 = @('HD_TreadleLathe', 'HD_LineShaftTurretLathe')
    HD_RollUniformSteelPlate = @('HD_LineShaftRollingMachine')
}
foreach ($entry in $expectedUsers.GetEnumerator()) {
    Assert ($recipes.ContainsKey($entry.Key)) "Missing mapped recipe: $($entry.Key)"
    Assert (($recipes[$entry.Key].Users -join '|') -ceq ($entry.Value -join '|')) "Wrong worktable mapping: $($entry.Key)"
}

$m1 = $thingDefs['HD_Apparel_GreatWarM1Helmet']
Assert ($null -ne $m1) 'Missing M1 helmet ThingDef.'
$m1Users = @(Values $m1 'recipeMaker/recipeUsers/li')
Assert (($m1Users -join '|') -ceq 'HD_LineShaftHydraulicPress') 'M1 helmet must be made at the hydraulic press.'

$mappedRecipeNames = @($expectedUsers.Keys)
Write-Output "PASS: $($recipes.Count) explicit RecipeDefs and $($thingDefs.Count) concrete ThingDefs are unique."
Write-Output "PASS: no orphan recipes; all Helod recipeUsers resolve to concrete worktables and contain no duplicate entries."
Write-Output "PASS: base/bulk recipe families share worktables and research prerequisites."
Write-Output "PASS: $($mappedRecipeNames.Count) process recipes and the M1 helmet match Docs/Recipe_Worktable_Mapping.md."
