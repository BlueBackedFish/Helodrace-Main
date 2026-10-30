param(
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [string]$GameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyRoot='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077',
    [string]$BlancasRoot,
    [switch]$ImpactAudit,
    [switch]$PrecisionAudit,
    [switch]$Wait,
    [int]$TimeoutSeconds=300
)
$ErrorActionPreference='Stop'
if($ImpactAudit -and $PrecisionAudit){throw 'Select one audit mode per runtime.'}
. (Join-Path $PSScriptRoot '../Game17Runtime.ps1')
$runtime=New-Game17Runtime -RuntimeRoot $RuntimeRoot -GameRoot $GameRoot -HarmonyRoot $HarmonyRoot -BlancasRoot $BlancasRoot
dotnet build (Join-Path $runtime.Repository 'Source/Helodrace/Helodrace.csproj') -p:DefineConstants=SHOTGUN_TESTS -p:IntermediateOutputPath=obj/ShotgunGame17/ ("-p:OutputPath="+(Join-Path $runtime.Game 'Mods/Main/Assemblies/')) ("-p:RimWorldPath="+$GameRoot) ("-p:HarmonyAssemblyPath="+$runtime.HarmonyAssembly) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Shotgun in-game validation build failed.'}
$arguments=@()
if($ImpactAudit){$arguments+='-hdShotgunImpactAudit=true'}
if($PrecisionAudit){$arguments+='-hdShotgunPrecisionAudit=true'}
$result=Join-Path $runtime.Profile $(if($ImpactAudit){'shotgun-impact-audit.txt'}else{'shotgun-results.txt'})
$process=Start-Game17Runtime -Runtime $runtime -Arguments $arguments -Headless -ResultFile $result
if($Wait){Wait-Game17Runtime -Runtime $runtime -TimeoutSeconds $TimeoutSeconds -Process $process}
