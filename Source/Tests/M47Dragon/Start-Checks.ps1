param([Parameter(Mandatory)][string]$RuntimeRoot,[ValidateSet('Checks','Trace')][string]$Mode='Checks')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Game17Runtime.ps1')
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runtime=New-Game17Runtime -RuntimeRoot $RuntimeRoot -ExtraPackageIds @('helodrace.tests.m47dragon')
$checks=Join-Path $runtime.Game 'Mods/M47DragonChecks'
New-Item -ItemType Directory -Path (Join-Path $checks 'About') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $checks 'About/About.xml'),'<ModMetaData><name>M47 Dragon checks</name><author>Helodrace tests</author><packageId>helodrace.tests.m47dragon</packageId><supportedVersions><li>1.7</li></supportedVersions><loadAfter><li>BlueBackedFish.Helodrace.Main</li></loadAfter></ModMetaData>')
$mainOutput=Join-Path $runtime.Game 'Mods/Main/Assemblies/'
dotnet build (Join-Path $repository 'Source/Helodrace/Helodrace.csproj') -p:IntermediateOutputPath=obj/M47Dragon/ ("-p:OutputPath="+$mainOutput) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Main build failed.'}
dotnet build (Join-Path $PSScriptRoot 'M47Dragon.csproj') ("-p:OutputPath="+(Join-Path $checks 'Assemblies/')) ("-p:HelodAssembly="+(Join-Path $mainOutput 'Helodrace.dll')) -v:minimal
if($LASTEXITCODE -ne 0){throw 'M47 Dragon checks build failed.'}
$arguments=@('-hd'+$(if($Mode -eq 'Trace'){'M47Trace'}else{'M47Dragon'})+'='+$runtime.Evidence)
$process=Start-Game17Runtime -Runtime $runtime -Arguments $arguments -ResultFile (Join-Path $runtime.Evidence $(if($Mode -eq 'Trace'){'trace.txt'}else{'checks.txt'})) -CompletionPattern '(?m)^COMPLETE\r?$'
Write-Output ('Observe with Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot "'+$runtime.Root+'" -TimeoutSeconds 45')
