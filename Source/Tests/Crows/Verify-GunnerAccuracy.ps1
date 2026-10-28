param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$ModAssembly = '',
    [string]$RimWorldPath = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
)
$ErrorActionPreference = 'Stop'
if (!$ModAssembly) { $ModAssembly = Join-Path $RepositoryRoot 'Assemblies/Helodrace.dll' }
$game = [Reflection.Assembly]::LoadFrom((Join-Path $RimWorldPath 'RimWorldWin64_Data/Managed/Assembly-CSharp.dll'))
$null = [Reflection.Assembly]::LoadFrom((Join-Path $RepositoryRoot 'Assemblies/0Harmony.dll'))
$mod = [Reflection.Assembly]::LoadFrom($ModAssembly)
$harmony = [HarmonyLib.Harmony]::new('Helodrace.Tests.HmgGunnerAccuracy')
try {
    $type = $mod.GetType('Helodrace.Patch_HeavyMachineGunGunnerAccuracy', $true)
    $patched = $harmony.CreateClassProcessor($type).Patch()
    $expected = [HarmonyLib.AccessTools]::Method($game.GetType('Verse.ShotReport'), 'HitFactorFromShooter')
    $owned = @($harmony.GetPatchedMethods() | Where-Object {
        [HarmonyLib.Harmony]::GetPatchInfo($_).Owners -contains 'Helodrace.Tests.HmgGunnerAccuracy'
    })
    if ($patched.Count -ne 1 -or $owned.Count -ne 1 -or $owned[0] -ne $expected) { throw 'Accuracy must patch the shared shooter-factor function.' }
    if ($mod.GetType('Helodrace.Patch_MannedTurretGunnerAccuracy')) { throw 'Old unbounded skill/distance override still exists.' }
    $null = $expected.Invoke($null, @($null, [single]10, $null))
    $correct = $mod.GetType('Helodrace.HeavyMachineGunAccuracy').GetMethod('Correct')
    foreach ($case in @(@(0.8,0.5,0.9),@(1.03,0.5,1),@(0.8,-1,0),@(0.8,2,1))) {
        $actual = [single]$correct.Invoke($null, @([single]$case[0], [single]$case[1]))
        if ([Math]::Abs($actual-$case[2]) -gt 0.00001) { throw "Actual game Mathf failed: $case -> $actual" }
    }
    $getter = $mod.GetType('Helodrace.Building_Crows').GetProperty('CanSetForcedTarget', [Reflection.BindingFlags]'NonPublic,Instance').GetGetMethod($true)
    if ($getter.DeclaringType.FullName -ne 'Helodrace.Building_Crows' -or $getter.GetBaseDefinition().DeclaringType.FullName -ne 'RimWorld.Building_TurretGun') {
        throw 'Remote CROWS must override the vanilla forced-target permission.'
    }
    foreach ($file in @('Defs/GreatWar/Buildings/Defense_GreatWar.xml','Defs/ModernWar/Buildings/Crows.xml')) {
        [xml]$defs = Get-Content -LiteralPath (Join-Path $RepositoryRoot $file) -Raw
        foreach ($extension in $defs.SelectNodes('//modExtensions/li[@Class="Helodrace.HeavyMachineGunAccuracyExtension"]')) {
            if ($extension.gunnerBaseAccuracy -ne '0.5') { throw 'Unexpected default x.' }
            if (!$mod.GetType('Helodrace.HeavyMachineGunAccuracyExtension').GetField('gunnerBaseAccuracy')) { throw 'Unknown extension field.' }
        }
    }
    Write-Output 'PASS: real RimWorld shooter-factor Harmony registration/invocation, bounded formula using game Mathf, obsolete patch removal, CROWS forced-target override and XML configuration.'
}
finally { $harmony.UnpatchAll('Helodrace.Tests.HmgGunnerAccuracy') }
