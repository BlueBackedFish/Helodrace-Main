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
    $names = @('Patch_HamRadioMenu', 'Patch_HamRadioTargets', 'Patch_HamRadioJob',
        'Patch_HamRadioExistingJob', 'Patch_LowFactionVanillaCommsOption', 'Patch_LowFactionVanillaCommsDialog')
    foreach ($name in $names) {
        $type = $mod.GetType('Helodrace.' + $name, $true)
        $patched = $harmony.CreateClassProcessor($type).Patch()
        if ($patched.Count -ne 1) { throw "Expected one installed game method for $name" }
    }
    Write-Output 'PASS: All six HAM/LOW communication patches apply to the installed RimWorld methods.'
}
finally {
    if ($null -ne $harmony) { $harmony.UnpatchAll('Helodrace.Tests.HamRouting') }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
