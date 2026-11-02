param(
    [string]$RuntimeRoot,
    [switch]$Baseline,
    [switch]$FuelOnly,
    [switch]$SustainOnly,
    [string]$ResumeSave
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Game17Runtime.ps1')
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(-not $RuntimeRoot){$RuntimeRoot=Join-Path $repository ('.codex-staging/ProductionStage1/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))}
$runtime=New-Game17Runtime -RuntimeRoot $RuntimeRoot -ExtraPackageIds @('helodrace.tests.productionstage1')
$checks=Join-Path $runtime.Game 'Mods/ProductionChecks'
New-Item -ItemType Directory -Path (Join-Path $checks 'About') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $checks 'About/About.xml'),'<ModMetaData><name>Production stage 1 checks</name><author>Helodrace tests</author><packageId>helodrace.tests.productionstage1</packageId><supportedVersions><li>1.7</li></supportedVersions><loadAfter><li>BlueBackedFish.Helodrace.Main</li></loadAfter></ModMetaData>')
New-Item -ItemType Directory -Path (Join-Path $checks 'Defs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Defs/Fixture.xml') -Destination (Join-Path $checks 'Defs/Fixture.xml')
$mainOutput=Join-Path $runtime.Game 'Mods/Main/Assemblies/'
dotnet build (Join-Path $repository 'Source/Helodrace/Helodrace.csproj') -p:IntermediateOutputPath=obj/ProductionStage1/ ("-p:OutputPath="+$mainOutput) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Main build failed.'}
dotnet build (Join-Path $PSScriptRoot 'ProductionStage1.csproj') ("-p:OutputPath="+(Join-Path $checks 'Assemblies/')) ("-p:HelodAssembly="+(Join-Path $mainOutput 'Helodrace.dll')) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Production checks build failed.'}
if($ResumeSave){
    $save=(Get-Item -LiteralPath $ResumeSave).FullName
    New-Item -ItemType Directory -Path (Join-Path $runtime.Profile 'Saves') -Force | Out-Null
    Copy-Item -LiteralPath $save -Destination (Join-Path $runtime.Profile 'Saves/Autostart.rws')
}
$arguments=@('-hdProductionStage1='+$runtime.Evidence)
if($Baseline){$arguments+='-hdProductionBaseline=true'}
if($FuelOnly){$arguments+='-hdProductionFuelOnly=true'}
if($SustainOnly){
    if(-not $ResumeSave){throw 'SustainOnly requires a completed production fixture save.'}
    $arguments+='-hdProductionSustainOnly=true'
}
$process=Start-Game17Runtime -Runtime $runtime -Arguments $arguments -ResultFile (Join-Path $runtime.Evidence 'checks.txt') -CompletionPattern '(?m)^COMPLETE\r?$' -SkipQuicktest:([bool]$ResumeSave)
Write-Output ('Observe the same host with Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot "'+$runtime.Root+'" -TimeoutSeconds 45')
