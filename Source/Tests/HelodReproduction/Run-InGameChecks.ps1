param(
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [string]$GameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyRoot='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077',
    [string]$BlancasRoot,
    [switch]$Wait,
    [int]$TimeoutSeconds=300
)
# Helod-only conception, cross-race romance and the temporary male Helod kind.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Game17Runtime.ps1')
$runtime=New-Game17Runtime -RuntimeRoot $RuntimeRoot -GameRoot $GameRoot -HarmonyRoot $HarmonyRoot -BlancasRoot $BlancasRoot
dotnet build (Join-Path $runtime.Repository 'Source/Helodrace/Helodrace.csproj') -p:DefineConstants=HELOD_REPRODUCTION_TESTS -p:IntermediateOutputPath=obj/ReproductionGame17/ ("-p:OutputPath="+(Join-Path $runtime.Game 'Mods/Main/Assemblies/')) ("-p:RimWorldPath="+$GameRoot) ("-p:HarmonyAssemblyPath="+$runtime.HarmonyAssembly) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Reproduction in-game validation build failed.'}
$result=Join-Path $runtime.Profile 'reproduction-results.txt'
$process=Start-Game17Runtime -Runtime $runtime -Headless -ResultFile $result
if($Wait){Wait-Game17Runtime -Runtime $runtime -TimeoutSeconds $TimeoutSeconds -Process $process}
