param(
    [string]$GameRoot='C:/Program Files (x86)/Steam/steamapps/common/RimWorld',
    [string]$HarmonyRoot='C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/2009463077',
    [string]$BlancasRoot,
    [string]$RuntimeRoot,
    [int]$TimeoutSeconds=240,
    [switch]$Visible,
    [switch]$Interactive
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Game17Runtime.ps1')
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(-not $RuntimeRoot){$RuntimeRoot=Join-Path $repository ('.codex-staging/ResearchWorkspaceGame17/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))}
$runtime=New-Game17Runtime -RuntimeRoot $RuntimeRoot -GameRoot $GameRoot -HarmonyRoot $HarmonyRoot -BlancasRoot $BlancasRoot -ExtraPackageIds @('helodrace.tests.researchworkspace')
$smoke=Join-Path $runtime.Game 'Mods/WorkspaceSmoke'
New-Item -ItemType Directory -Path (Join-Path $smoke 'About') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $smoke 'About/About.xml'),'<ModMetaData><name>Research workspace smoke test</name><author>Helodrace tests</author><packageId>helodrace.tests.researchworkspace</packageId><supportedVersions><li>1.7</li></supportedVersions><loadAfter><li>BlueBackedFish.Helodrace.Main</li></loadAfter></ModMetaData>')
$mainOutput=Join-Path $runtime.Game 'Mods/Main/Assemblies/'
dotnet build (Join-Path $repository 'Source/Helodrace/Helodrace.csproj') -p:IntermediateOutputPath=obj/ResearchSmoke17/ ("-p:OutputPath="+$mainOutput) ("-p:RimWorldPath="+$GameRoot) ("-p:HarmonyAssemblyPath="+$runtime.HarmonyAssembly) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Research smoke Main build failed.'}
dotnet build (Join-Path $PSScriptRoot 'ResearchWorkspaceGame.csproj') ("-p:OutputPath="+(Join-Path $smoke 'Assemblies/')) ("-p:HelodAssembly="+(Join-Path $mainOutput 'Helodrace.dll')) ("-p:RimWorldPath="+$GameRoot) ("-p:HarmonyAssemblyPath="+$runtime.HarmonyAssembly) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Research smoke bridge build failed.'}
$screenshots=Join-Path $runtime.Evidence 'Screenshots'
New-Item -ItemType Directory -Path $screenshots -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $runtime.Profile 'Config/Prefs.xml'),'<PrefsData><screenWidth>1600</screenWidth><screenHeight>900</screenHeight><fullscreen>False</fullscreen><uiScale>1</uiScale><devMode>True</devMode><runInBackground>True</runInBackground><langFolderName>Korean (한국어)</langFolderName><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic></PrefsData>')
$arguments=@(('-hdResearchUiSmoke='+$screenshots),'-screen-fullscreen','0','-screen-width','1600','-screen-height','900')
if($Interactive){$arguments+='-hdResearchUiInteractive=true'}
$process=Start-Game17Runtime -Runtime $runtime -Arguments $arguments -Visible:$Visible -ResultFile (Join-Path $screenshots 'checks.txt') -CompletionPattern '(?m)^PASS: nine native canvas panels rendered\r?$'
Wait-Game17Runtime -Runtime $runtime -TimeoutSeconds $TimeoutSeconds -Process $process
Get-Content (Join-Path $screenshots 'checks.txt')
Write-Output ('Screenshots: '+$screenshots)
