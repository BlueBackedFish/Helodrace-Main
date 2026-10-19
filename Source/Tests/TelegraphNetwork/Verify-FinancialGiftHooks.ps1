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
    $harmony = [HarmonyLib.Harmony]::new('Helodrace.Tests.FinancialGifts')
    $type = $mod.GetType('Helodrace.Economy.Patch_FinancialEconomicReward', $true)
    $patched = $harmony.CreateClassProcessor($type).Patch()
    if ($patched.Count -ne 1) { throw 'Expected the vanilla goodwill award method' }
    foreach ($name in @('Patch_FinancialGiftValuation', 'Patch_FinancialGiftCapMessage', 'Patch_FinancialNaturalRecovery',
        'Patch_FinancialGiftTradeText', 'GoodwillSituationWorker_Financial')) {
        if ($null -ne $mod.GetType('Helodrace.Economy.' + $name)) { throw "Obsolete financial replacement remains: $name" }
    }
    Write-Output 'PASS: Half-sized financial rewards supplement vanilla goodwill; obsolete gift valuation, UI and baseline patches are absent.'

}
finally {
    if ($null -ne $harmony) { $harmony.UnpatchAll('Helodrace.Tests.FinancialGifts') }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
