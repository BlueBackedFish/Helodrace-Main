param(
    [Parameter(Mandatory=$true)][string]$RimWorldPath,
    [string]$HarmonyAssemblyPath
)
$ErrorActionPreference='Stop'
if([Environment]::Version.Major -lt 10){throw 'Game API checks require PowerShell 7.6 or newer on .NET 10.'}
$gameRoot=[IO.Path]::GetFullPath($RimWorldPath)
$version=(Get-Content -LiteralPath (Join-Path $gameRoot 'Version.txt') -Raw).Trim()
if(-not $version.StartsWith('1.7.')){throw 'These checks require an actual RimWorld 1.7 runtime.'}
if(-not $HarmonyAssemblyPath){$HarmonyAssemblyPath=Join-Path $gameRoot '../../workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll'}
if([Reflection.AssemblyName]::GetAssemblyName($HarmonyAssemblyPath).Version.Major -ne 3){throw 'Harmony 3 is required.'}
foreach($name in @('Waylith','UnityStubs','Assembly-CSharp')){
    $null=[Reflection.Assembly]::LoadFrom((Join-Path $gameRoot ($name+'.dll')))
}
$null=[Reflection.Assembly]::LoadFrom($HarmonyAssemblyPath)
