param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$RimWorldPath = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
)

$ErrorActionPreference = 'Stop'
$null = & (Join-Path $PSScriptRoot '../../Import-Game17.ps1') -RimWorldPath $RimWorldPath
$managedDirectory = $RimWorldPath
$resolverScript = {
    param($sender, $eventArgs)
    $name = ([System.Reflection.AssemblyName]::new($eventArgs.Name)).Name
    $path = Join-Path $managedDirectory ($name + '.dll')
    if (Test-Path -LiteralPath $path) { return [System.Reflection.Assembly]::LoadFrom($path) }
}.GetNewClosure()
$resolver = [System.ResolveEventHandler]$resolverScript
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $null = [System.Reflection.Assembly]::LoadFrom((Join-Path $RimWorldPath '../../workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll'))
    $game = [System.Reflection.Assembly]::LoadFrom((Join-Path $managedDirectory 'Assembly-CSharp.dll'))
    $mod = [System.Reflection.Assembly]::LoadFrom((Join-Path $RepositoryRoot 'Assemblies/Helodrace.dll'))
    $installation = [Activator]::CreateInstance($mod.GetType('Helodrace.FloatMenuOptionProvider_TelegraphInstallation', $true))
    $trade = [Activator]::CreateInstance($game.GetType('RimWorld.FloatMenuOptionProvider_Trade', $true))
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance'

    # Read the actual game/mod properties, without invoking Unity or a fabricated
    # game state. These flags are consumed by RimWorld's menu filtering.
    foreach ($name in @('Drafted', 'Undrafted', 'RequiresManipulation', 'Multiselect')) {
        $actual = $installation.GetType().GetProperty($name, $flags).GetValue($installation)
        $expected = $trade.GetType().GetProperty($name, $flags).GetValue($trade)
        if ($actual -ne $expected) { throw "Telegraph menu eligibility differs from vanilla trading: $name" }
    }
    Write-Output 'PASS: Built telegraph menu eligibility matches vanilla trading for drafted/undrafted operators, manipulation, and multiselect.'
}
finally {
    [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
