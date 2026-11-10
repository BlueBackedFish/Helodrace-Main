param(
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [string]$GameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyRoot='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077',
    [string]$BlancasRoot,
    [switch]$Wait,
    [int]$TimeoutSeconds=600
)
# Phase 2 protection-system checks in the real 1.7 engine (Docs/Ballistic_Protection_System.md).
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Game17Runtime.ps1')
$runtime=New-Game17Runtime -RuntimeRoot $RuntimeRoot -GameRoot $GameRoot -HarmonyRoot $HarmonyRoot -BlancasRoot $BlancasRoot
dotnet build (Join-Path $runtime.Repository 'Source/Helodrace/Helodrace.csproj') -p:DefineConstants=BALLISTICS_TESTS -p:IntermediateOutputPath=obj/BallisticsGame17/ ("-p:OutputPath="+(Join-Path $runtime.Game 'Mods/Main/Assemblies/')) ("-p:RimWorldPath="+$GameRoot) ("-p:HarmonyAssemblyPath="+$runtime.HarmonyAssembly) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Ballistics in-game validation build failed.'}
$result=Join-Path $runtime.Profile 'ballistics-results.txt'
$process=Start-Game17Runtime -Runtime $runtime -Headless -ResultFile $result
if($Wait){Wait-Game17Runtime -Runtime $runtime -TimeoutSeconds $TimeoutSeconds -Process $process}
