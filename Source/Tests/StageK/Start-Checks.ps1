param([Parameter(Mandatory)][string]$RuntimeRoot,[switch]$Recreation,[switch]$State,[switch]$Pistols,[switch]$GeneAudit,[switch]$Xenotypes,[switch]$TextureItems,[switch]$Minerals,[switch]$Fibers,[switch]$ArmorMaterials,[switch]$HelmetShields,[switch]$EquipmentMaterials,[switch]$WorktableCatalog,[switch]$Worktables,[switch]$ArmorCraft,[switch]$TextContent,[switch]$SupportWeapons,[switch]$FixedRelocation)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Game17Runtime.ps1')
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runtime=New-Game17Runtime -RuntimeRoot $RuntimeRoot -ExtraPackageIds @('helodrace.tests.stagek')
$checks=Join-Path $runtime.Game 'Mods/StageKChecks'
New-Item -ItemType Directory -Path (Join-Path $checks 'About') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $checks 'About/About.xml'),'<ModMetaData><name>Stage K life checks</name><author>Helodrace tests</author><packageId>helodrace.tests.stagek</packageId><supportedVersions><li>1.7</li></supportedVersions><loadAfter><li>BlueBackedFish.Helodrace.Main</li></loadAfter></ModMetaData>')
$mainOutput=Join-Path $runtime.Game 'Mods/Main/Assemblies/'
dotnet build (Join-Path $repository 'Source/Helodrace/Helodrace.csproj') -p:IntermediateOutputPath=obj/StageK/ ("-p:OutputPath="+$mainOutput) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Main build failed.'}
dotnet build (Join-Path $PSScriptRoot 'StageK.csproj') ("-p:OutputPath="+(Join-Path $checks 'Assemblies/')) ("-p:HelodAssembly="+(Join-Path $mainOutput 'Helodrace.dll')) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Stage K life checks build failed.'}
$arguments=@('-hdStageK='+$runtime.Evidence)
if($Recreation){$arguments+='-hdKRecreation=true'}
if($State){$arguments+='-hdKState=true'}
if($Pistols){$arguments+='-hdKPistols=true'}
if($GeneAudit){$arguments+='-hdKGeneAudit=true'}
if($FixedRelocation){$arguments+='-hdKFixedRelocation=true';$arguments+=('-hdFixedWeaponData='+[IO.Path]::GetFullPath((Join-Path $repository 'Docs/Data/FixedWeaponRelocation/weights.json')))}
if($SupportWeapons){$arguments+='-hdKSupportWeapons=true'}
if($TextContent){$arguments+='-hdKTextContent=true'}
if($ArmorCraft){$arguments+='-hdKArmorCraft=true'}
if($WorktableCatalog){$arguments+='-hdKWorktableCatalog=true'}
if($Worktables){$arguments+='-hdKWorktables=true';$arguments+=('-hdWorktableData='+[IO.Path]::GetFullPath((Join-Path $repository 'Docs/Data/Worktables')))}
if($EquipmentMaterials){$arguments+='-hdKEquipmentMaterials=true';$arguments+=('-hdEquipmentMaterialsData='+[IO.Path]::GetFullPath((Join-Path $repository 'Docs/Data/EquipmentMaterials/recipe-materials.json')))}
if($HelmetShields){$arguments+='-hdKHelmetShields=true'}
if($ArmorMaterials){$arguments+='-hdKArmorMaterials=true'}
if($Fibers){$arguments+='-hdKFibers=true'}
if($Minerals){$arguments+='-hdKMinerals=true'}
if($TextureItems){$arguments+='-hdKTextureItems=true'}
if($Xenotypes){$arguments+='-hdKXenotypes=true'}
$process=Start-Game17Runtime -Runtime $runtime -Arguments $arguments -ResultFile (Join-Path $runtime.Evidence 'checks.txt') -CompletionPattern '(?m)^COMPLETE\r?$'
Write-Output ('Observe with Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot "'+$runtime.Root+'" -TimeoutSeconds 45')
