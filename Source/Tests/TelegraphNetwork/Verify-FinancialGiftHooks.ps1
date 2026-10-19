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
    $targets = @{
        Patch_FinancialEconomicReward = 1
        Patch_FinancialGiftValuation = 3
        Patch_FinancialGiftCapMessage = 1
        Patch_FinancialNaturalRecovery = 1
    }
    $count = 0
    foreach ($name in $targets.Keys) {
        $type = $mod.GetType('Helodrace.Economy.' + $name, $true)
        $patched = $harmony.CreateClassProcessor($type).Patch()
        if ($patched.Count -ne $targets[$name]) { throw "Unexpected installed game method count for $name" }
        $count += $patched.Count
    }
    # Reading/transpiling UI instructions is safe outside Unity. Creating a live
    # Dialog_Trade wrapper here would require Unity's native ECall methods.
    $textTargets = [Helodrace.Economy.Patch_FinancialGiftTradeText]::TargetMethods()
    $expectedTextCounts = @(2, 2, 1)
    $readInstructions = [HarmonyLib.PatchProcessor].GetMethods() | Where-Object {
        $_.Name -eq 'GetOriginalInstructions' -and $_.GetParameters()[1].ParameterType -eq [Reflection.Emit.ILGenerator]
    }
    $index = 0
    foreach ($method in $textTargets) {
        $instructions = $readInstructions.Invoke($null, [object[]]@($method, $null))
        $rewritten = @([Helodrace.Economy.Patch_FinancialGiftTradeText]::Transpiler($instructions))
        $textCount = @($rewritten | Where-Object {
            $_.opcode -eq [Reflection.Emit.OpCodes]::Ldstr -and [string]$_.operand -like 'HD_FinancialGift_*'
        }).Count
        if ($textCount -ne $expectedTextCounts[$index]) { throw "Gift UI text does not match $method" }
        $index++
    }
    if ($index -ne 3) { throw 'Expected three financial gift UI methods' }
    Write-Output "PASS: Four financial goodwill patches apply to $count installed RimWorld method targets; all three gift UI instruction rewrites match."
}
finally {
    if ($null -ne $harmony) { $harmony.UnpatchAll('Helodrace.Tests.FinancialGifts') }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
