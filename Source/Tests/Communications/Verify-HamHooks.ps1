param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$RimWorldPath = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
)

$ErrorActionPreference = 'Stop'
$managedDirectory = Join-Path $RimWorldPath 'RimWorldWin64_Data/Managed'
$resolverScript = {
    param($sender, $eventArgs)
    $path = Join-Path $managedDirectory (([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll')
    if (Test-Path -LiteralPath $path) { return [Reflection.Assembly]::LoadFrom($path) }
}.GetNewClosure()
$resolver = [ResolveEventHandler]$resolverScript
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$harmony = $null
try {
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $RepositoryRoot 'Assemblies/0Harmony.dll'))
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $managedDirectory 'Assembly-CSharp.dll'))
    $mod = [Reflection.Assembly]::LoadFrom((Join-Path $RepositoryRoot 'Assemblies/Helodrace.dll'))
    $harmony = [HarmonyLib.Harmony]::new('Helodrace.Tests.HamRouting')
    $names = @('Patch_HamFactionDialogue')
    foreach ($name in $names) {
        $type = $mod.GetType('Helodrace.' + $name, $true)
        $patched = $harmony.CreateClassProcessor($type).Patch()
        if ($patched.Count -ne 1) { throw "Expected one installed game method for $name" }
    }
    foreach ($obsolete in @('Patch_HamRadioMenu', 'Patch_HamRadioTargets', 'Patch_HamRadioExistingJob', 'Building_HamRadio',
        'Patch_LowFactionVanillaCommsOption', 'Patch_LowFactionVanillaCommsDialog', 'Patch_HamRadioJob',
        'Patch_HamFactionCommsOption', 'Patch_HamFactionCommsDialog', 'JobDriver_UseHamRadio')) {
        if ($null -ne $mod.GetType('Helodrace.' + $obsolete)) { throw "Obsolete console replacement remains: $obsolete" }
    }

    # Only defName is needed by these early guards. Avoid game/Unity constructors.
    foreach ($defName in @('HD_HelodCivilLowFaction', 'HD_HelodCivilHighFaction', 'OutlanderCivil', 'TribeCivil', 'Empire', 'HD_HelodPlayerColony')) {
        $faction = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([RimWorld.Faction])
        $factionDef = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([RimWorld.FactionDef])
        [RimWorld.FactionDef].GetField('defName').SetValue($factionDef, $defName)
        [RimWorld.Faction].GetField('def').SetValue($faction, $factionDef)
        $expectedHam = $defName -in @('HD_HelodCivilLowFaction', 'HD_HelodCivilHighFaction')
        if ([Helodrace.HamRadioUtility]::IsHamFaction($faction) -ne $expectedHam) { throw "Incorrect HAM scope for $defName" }
        if (!$expectedHam) {
            [Verse.DiaNode]$node = $null
            if (![Helodrace.Patch_HamFactionDialogue]::Prefix($null, $faction, [ref]$node) -or $null -ne $node) {
                throw "Vanilla dialogue content was replaced for $defName"
            }
        }
    }
    $ship = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([RimWorld.TradeShip])
    if ([Helodrace.HamRadioUtility]::IsHamFaction($ship)) {
        throw 'Orbital trade must not use HAM dialogue'
    }
    # Our only registration is the dialogue-content factory. Vanilla owns the
    # contact icon/goodwill, job, negotiation window and ambient sound.
    $expectedTarget = [HarmonyLib.AccessTools]::Method([RimWorld.FactionDialogMaker], 'FactionDialogFor')
    $ownedTargets = @($harmony.GetPatchedMethods() | Where-Object {
        [HarmonyLib.Harmony]::GetPatchInfo($_).Owners -contains 'Helodrace.Tests.HamRouting'
    })
    if ($ownedTargets.Count -ne 1 -or $ownedTargets[0] -ne $expectedTarget) {
        throw 'HAM must only patch faction dialogue content'
    }
    Write-Output 'PASS: HAM only patches LOW/HIGH dialogue content; vanilla contact presentation, jobs, windows, and other faction routes remain intact.'
}
finally {
    if ($null -ne $harmony) { $harmony.UnpatchAll('Helodrace.Tests.HamRouting') }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
