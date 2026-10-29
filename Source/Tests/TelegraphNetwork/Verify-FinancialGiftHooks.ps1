param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$RimWorldPath = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
)

# Register against the real installed game without starting a Unity game session.
$ErrorActionPreference = 'Stop'
$null = & (Join-Path $PSScriptRoot '../../Import-Game17.ps1') -RimWorldPath $RimWorldPath
$managedDirectory = $RimWorldPath
$resolverScript = {
    param($sender, $eventArgs)
    $path = Join-Path $managedDirectory (([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll')
    if (Test-Path -LiteralPath $path) { return [Reflection.Assembly]::LoadFrom($path) }
}.GetNewClosure()
$resolver = [ResolveEventHandler]$resolverScript
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$harmony = $null
try {
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $RimWorldPath '../../workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll'))
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
    Write-Output 'PASS: Economic reward hook registers, including financial-only safe-conduct trade; obsolete gift valuation, UI and baseline patches are absent.'

}
finally {
    if ($null -ne $harmony) { $harmony.UnpatchAll('Helodrace.Tests.FinancialGifts') }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
