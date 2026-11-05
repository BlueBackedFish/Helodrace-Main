param([Parameter(Mandatory)][string]$RuntimeRoot,[switch]$Perf,[switch]$Visible)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Game17Runtime.ps1')
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runtime=New-Game17Runtime -RuntimeRoot $RuntimeRoot -ExtraPackageIds @('helodrace.tests.productionplanning')
$checks=Join-Path $runtime.Game 'Mods/ProductionPlanningChecks'
New-Item -ItemType Directory -Path (Join-Path $checks 'About') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $checks 'About/About.xml'),'<ModMetaData><name>Production planning checks</name><author>Helodrace tests</author><packageId>helodrace.tests.productionplanning</packageId><supportedVersions><li>1.7</li></supportedVersions><loadAfter><li>BlueBackedFish.Helodrace.Main</li></loadAfter></ModMetaData>')
$mainOutput=Join-Path $runtime.Game 'Mods/Main/Assemblies/'
dotnet build (Join-Path $repository 'Source/Helodrace/Helodrace.csproj') -p:IntermediateOutputPath=obj/ProductionPlanning/ ("-p:OutputPath="+$mainOutput) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Main build failed.'}
dotnet build (Join-Path $PSScriptRoot 'ProductionPlanning.csproj') ("-p:OutputPath="+(Join-Path $checks 'Assemblies/')) ("-p:HelodAssembly="+(Join-Path $mainOutput 'Helodrace.dll')) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Production planning checks build failed.'}
$arguments=@($(if($Perf){'-hdProductionPlanningPerf='}else{'-hdProductionPlanning='})+$runtime.Evidence)
if($Perf){$arguments+=@('-hdProductionPlanningShots=1','-screen-fullscreen','0','-screen-width','1600','-screen-height','900')}
$process=Start-Game17Runtime -Runtime $runtime -Arguments $arguments -Visible:$Visible -ResultFile (Join-Path $runtime.Evidence 'checks.txt') -CompletionPattern '(?m)^COMPLETE\r?$'
Write-Output ('Observe with Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot "'+$runtime.Root+'" -TimeoutSeconds 45')
