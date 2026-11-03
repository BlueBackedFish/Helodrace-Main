param([Parameter(Mandatory)][string]$RuntimeRoot,[switch]$Recreation,[switch]$State,[switch]$Pistols,[switch]$GeneAudit,[switch]$Xenotypes,[switch]$TextureItems)
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
if($TextureItems){$arguments+='-hdKTextureItems=true'}
if($Xenotypes){$arguments+='-hdKXenotypes=true'}
$process=Start-Game17Runtime -Runtime $runtime -Arguments $arguments -ResultFile (Join-Path $runtime.Evidence 'checks.txt') -CompletionPattern '(?m)^COMPLETE\r?$'
Write-Output ('Observe with Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot "'+$runtime.Root+'" -TimeoutSeconds 45')
