param([Parameter(Mandatory)][string]$RuntimeRoot, [switch]$Baseline)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Game17Runtime.ps1')
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runtime=New-Game17Runtime -RuntimeRoot $RuntimeRoot -ExtraPackageIds @('helodrace.tests.stage2')
$checks=Join-Path $runtime.Game 'Mods/Stage2Checks'
New-Item -ItemType Directory -Path (Join-Path $checks 'About') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $checks 'About/About.xml'),'<ModMetaData><name>Stage 2 checks</name><author>Helodrace tests</author><packageId>helodrace.tests.stage2</packageId><supportedVersions><li>1.7</li></supportedVersions><loadAfter><li>BlueBackedFish.Helodrace.Main</li></loadAfter></ModMetaData>')
$mainOutput=Join-Path $runtime.Game 'Mods/Main/Assemblies/'
dotnet build (Join-Path $repository 'Source/Helodrace/Helodrace.csproj') -p:IntermediateOutputPath=obj/Stage2/ ("-p:OutputPath="+$mainOutput) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Main build failed.'}
dotnet build (Join-Path $PSScriptRoot 'Stage2.csproj') ("-p:OutputPath="+(Join-Path $checks 'Assemblies/')) ("-p:HelodAssembly="+(Join-Path $mainOutput 'Helodrace.dll')) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Stage 2 checks build failed.'}
$arguments=@('-hdStage2='+$runtime.Evidence)
if($Baseline){$arguments+='-hdStage2Baseline=true'}
$process=Start-Game17Runtime -Runtime $runtime -Arguments $arguments -ResultFile (Join-Path $runtime.Evidence 'checks.txt') -CompletionPattern '(?m)^COMPLETE\r?$'
Write-Output ('Observe with Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot "'+$runtime.Root+'" -TimeoutSeconds 45')
