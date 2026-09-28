param(
    [string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [switch]$PatchSmokeTest
)
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path $PSScriptRoot -Parent
function Assert-Squad($condition, [string]$message) { if (!$condition) { throw $message } }
$taskFiles = Get-ChildItem -LiteralPath "$taskRepo/Defs", "$taskRepo/Languages" -Recurse -Filter *.xml
foreach ($taskFile in $taskFiles) { $null = [xml][IO.File]::ReadAllText($taskFile.FullName) }
Write-Output "PASS: $($taskFiles.Count) XML files parse."
$taskFormationXml = [xml][IO.File]::ReadAllText("$taskRepo/Defs/Organization/Organization_GreatWar.xml")
$taskPawnXml = [xml][IO.File]::ReadAllText("$taskRepo/Defs/Helod/Pawns/PawnKinds_GreatWar.xml")
$taskPawnNames = @($taskPawnXml.Defs.PawnKindDef | ForEach-Object { $_.defName } | Where-Object { $_ })
$taskRoles = @($taskFormationXml.Defs.'Helodrace.Squads.RoleDef' | ForEach-Object { $_.defName } | Where-Object { $_ })
$taskFormations = @($taskFormationXml.Defs.'Helodrace.Squads.FormationDef')
$taskFormationNames = @($taskFormations | ForEach-Object { $_.defName })
$taskUsedPawnKinds = @()
foreach ($taskFormation in $taskFormations) {
    Assert-Squad ($taskRoles -contains $taskFormation.commanderRole) "Unresolved commander role: $($taskFormation.commanderRole)"
    foreach ($taskSlot in @($taskFormation.requiredRoles.li) + @($taskFormation.optionalRoles.li)) {
        if (!$taskSlot) { continue }
        Assert-Squad ($taskPawnNames -contains $taskSlot.pawnKind) "Unresolved Great War PawnKind: $($taskSlot.pawnKind)"
        Assert-Squad ($taskSlot.pawnKind -like 'HD_GW_*') 'Non-Great War formation member.'
        Assert-Squad ($taskRoles -contains $taskSlot.combatRole) "Unresolved combat role: $($taskSlot.combatRole)"
        if ($taskSlot.commandRole) { Assert-Squad ($taskRoles -contains $taskSlot.commandRole) 'Unresolved command role.' }
        foreach ($taskQualification in @($taskSlot.commandQualifications.li)) {
            if ($taskQualification) { Assert-Squad ($taskRoles -contains $taskQualification) 'Unresolved qualification.' }
        }
        $taskUsedPawnKinds += $taskSlot.pawnKind
    }
    foreach ($taskChild in @($taskFormation.childFormations.li)) {
        if ($taskChild) { Assert-Squad ($taskFormationNames -contains $taskChild.formation) 'Unresolved child formation.' }
    }
}
Assert-Squad (@($taskUsedPawnKinds | Select-Object -Unique).Count -eq 8) 'The organization definitions must use all eight Great War PawnKinds.'
$taskFactionXml = [xml][IO.File]::ReadAllText("$taskRepo/Defs/Factions/Factions_Helod.xml")
$taskFaction = $taskFactionXml.Defs.FactionDef | Where-Object { $_.defName -eq 'HD_HelodCivilLowFaction' }
Assert-Squad ($taskFaction.modExtensions.li.doctrine -eq 'HD_Doctrine_GreatWar') 'Faction doctrine is not connected.'
$taskRaceXml = [xml][IO.File]::ReadAllText("$taskRepo/Defs/Helod/Race/HelodRace.xml")
$taskBaseXml = [xml][IO.File]::ReadAllText("$taskRepo/Defs/Helod/Pawns/PawnBase.xml")
Assert-Squad ($taskRaceXml.Defs.ThingDef.ParentName -eq 'HD_PawnBase') 'Helod race does not inherit the organization component.'
Assert-Squad (@($taskBaseXml.Defs.ThingDef.comps.li | Where-Object { $_.Class -eq 'Helodrace.Squads.CompProperties_PawnOrganization' }).Count -eq 1) 'Organization component missing or duplicated.'
Write-Output 'PASS: formation/role references, all eight Great War PawnKinds, faction doctrine and pawn component.'
$taskManaged = Join-Path $RimWorldPath 'RimWorldWin64_Data/Managed'
foreach ($taskName in 'UnityEngine.CoreModule', 'UnityEngine', 'Assembly-CSharp') {
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $taskManaged "$taskName.dll"))
}
$null = [Reflection.Assembly]::LoadFrom("$taskRepo/Assemblies/0Harmony.dll")
$taskAssembly = [Reflection.Assembly]::LoadFrom("$taskRepo/Assemblies/Helodrace.dll")
foreach ($taskName in 'FormationDef', 'RoleDef', 'DoctrineDef', 'CombatOrganization', 'CombatGroup', 'PawnOrganizationComponent', 'GameComponent_CombatOrganizations') {
    Assert-Squad ($null -ne $taskAssembly.GetType("Helodrace.Squads.$taskName")) "Compiled type missing: $taskName"
}
foreach ($taskNode in $taskFormationXml.Defs.ChildNodes | Where-Object { $_.NodeType -eq 'Element' }) {
    $taskType = $taskAssembly.GetType($taskNode.LocalName)
    Assert-Squad ($null -ne $taskType) "Unknown XML definition class: $($taskNode.LocalName)"
    foreach ($taskField in $taskNode.ChildNodes | Where-Object { $_.NodeType -eq 'Element' }) {
        Assert-Squad ($null -ne $taskType.GetField($taskField.LocalName)) "Unknown XML field: $($taskNode.LocalName).$($taskField.LocalName)"
    }
}
Write-Output 'PASS: compiled organization classes and XML field names.'
if ($PatchSmokeTest) {
    $taskHarmony = [HarmonyLib.Harmony]::new('Helodrace.Squads.SmokeTest')
    try {
        foreach ($taskName in 'Patch_PawnGroupMaker_Organization', 'Patch_PawnGroupMaker_OrganizationPreview', 'Patch_DebugSettings_OrganizationOverlay') {
            $null = $taskHarmony.CreateClassProcessor($taskAssembly.GetType("Helodrace.Squads.$taskName")).Patch()
            Write-Output "PASS: Harmony installed $taskName"
        }
    } finally { $taskHarmony.UnpatchAll('Helodrace.Squads.SmokeTest') }
}
Write-Output 'PASS: static integration checks complete. In-game generation/save/load testing remains separate.'
