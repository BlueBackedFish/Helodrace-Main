param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$RimWorldPath = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
)

# Register against the real installed game without starting a Unity game session.
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
    $harmony = [HarmonyLib.Harmony]::new('Helodrace.Tests.SafeConduct')
    $names = @('Patch_FinancialCaravanGenerationHostility', 'Patch_FinancialCaravanLord', 'Patch_FinancialCaravanThingHostility',
        'Patch_FinancialCaravanFactionHostility', 'Patch_FinancialCaravanCanTrade', 'Patch_FinancialCaravanPlayerDamage', 'Patch_FinancialCaravanDeparture')
    foreach ($name in $names) {
        $type = $mod.GetType('Helodrace.Economy.' + $name, $true)
        $patched = $harmony.CreateClassProcessor($type).Patch()
        if ($patched.Count -ne 1) { throw "Expected one method for $name" }
    }
    function New-FactionFixture([bool]$player) {
        $faction = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([RimWorld.Faction])
        $def = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([RimWorld.FactionDef])
        [RimWorld.FactionDef].GetField('isPlayer').SetValue($def, $player)
        [RimWorld.Faction].GetField('def').SetValue($faction, $def)
        return ,$faction
    }
    $source = New-FactionFixture $false
    $player = New-FactionFixture $true
    $other = New-FactionFixture $false
    if ([Helodrace.Economy.FinancialCaravanScope]::Covers($source, $player)) { throw 'Generation scope leaked before entering' }
    $scope = [Helodrace.Economy.FinancialCaravanScope]::Enter($source)
    try {
        if (![Helodrace.Economy.FinancialCaravanScope]::Covers($source, $player) -or
            ![Helodrace.Economy.FinancialCaravanScope]::Covers($player, $source) -or
            [Helodrace.Economy.FinancialCaravanScope]::Covers($other, $player) -or
            [Helodrace.Economy.FinancialCaravanScope]::Covers($source, $other)) { throw 'Generation scope covers the wrong faction pairs' }
        [bool]$hostile = $true
        if ([Helodrace.Economy.Patch_FinancialCaravanGenerationHostility]::Prefix($source, $player, [ref]$hostile) -or $hostile) {
            throw 'Safe-conduct generation did not bypass hostility'
        }
        if (![Helodrace.Economy.Patch_FinancialCaravanGenerationHostility]::Prefix($other, $player, [ref]$hostile)) {
            throw 'Unrelated faction hostility was overridden'
        }
        $nested = [Helodrace.Economy.FinancialCaravanScope]::Enter($null)
        try {
            if ([Helodrace.Economy.FinancialCaravanScope]::Covers($source, $player)) { throw 'Inactive nested scope is not isolated' }
        }
        finally { $nested.Dispose() }
        if (![Helodrace.Economy.FinancialCaravanScope]::Covers($source, $player)) { throw 'Outer generation scope was not restored' }
    }
    finally { $scope.Dispose() }
    if ([Helodrace.Economy.FinancialCaravanScope]::Covers($source, $player)) { throw 'Generation scope leaked after disposal' }
    Write-Output 'PASS: Seven safe-conduct hooks register; generation overrides are limited to the requested faction, restore nested state, and do not leak.'

}
finally {
    if ($null -ne $harmony) { $harmony.UnpatchAll('Helodrace.Tests.SafeConduct') }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
