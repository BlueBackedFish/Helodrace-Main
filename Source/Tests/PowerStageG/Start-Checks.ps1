param([Parameter(Mandatory)][string]$RuntimeRoot, [string]$ResumeSave)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Game17Runtime.ps1')
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runtime=New-Game17Runtime -RuntimeRoot $RuntimeRoot -ExtraPackageIds @('helodrace.tests.powerstageg')
$checks=Join-Path $runtime.Game 'Mods/PowerChecks'
New-Item -ItemType Directory -Path (Join-Path $checks 'About') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $checks 'About/About.xml'),'<ModMetaData><name>Power stage G checks</name><author>Helodrace tests</author><packageId>helodrace.tests.powerstageg</packageId><supportedVersions><li>1.7</li></supportedVersions><loadAfter><li>BlueBackedFish.Helodrace.Main</li></loadAfter></ModMetaData>')
$mainOutput=Join-Path $runtime.Game 'Mods/Main/Assemblies/'
dotnet build (Join-Path $repository 'Source/Helodrace/Helodrace.csproj') -p:IntermediateOutputPath=obj/PowerStageG/ ("-p:OutputPath="+$mainOutput) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Main build failed.'}
dotnet build (Join-Path $PSScriptRoot 'PowerStageG.csproj') ("-p:OutputPath="+(Join-Path $checks 'Assemblies/')) ("-p:HelodAssembly="+(Join-Path $mainOutput 'Helodrace.dll')) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Power checks build failed.'}
$arguments=@('-hdPowerStageG='+$runtime.Evidence)
if($ResumeSave){
    New-Item -ItemType Directory -Path (Join-Path $runtime.Profile 'Saves') -Force | Out-Null
    Copy-Item -LiteralPath (Get-Item -LiteralPath $ResumeSave).FullName -Destination (Join-Path $runtime.Profile 'Saves/Autostart.rws')
}
$process=Start-Game17Runtime -Runtime $runtime -Arguments $arguments -ResultFile (Join-Path $runtime.Evidence 'checks.txt') -CompletionPattern '(?m)^COMPLETE\r?$' -SkipQuicktest:([bool]$ResumeSave)
Write-Output ('Observe with Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot "'+$runtime.Root+'" -TimeoutSeconds 45')
