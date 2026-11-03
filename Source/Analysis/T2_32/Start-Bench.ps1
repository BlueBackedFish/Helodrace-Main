param([Parameter(Mandatory)][string]$RuntimeRoot,[ValidateRange(8,1024)][int]$Samples=128)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../Tests/Game17Runtime.ps1')
$runtime=New-Game17Runtime -RuntimeRoot $RuntimeRoot -ExtraPackageIds @('helodrace.analysis.t232')
$module=Join-Path $runtime.Game 'Mods/T232Bench'
New-Item -ItemType Directory -Path (Join-Path $module 'About') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $module 'About/About.xml'),'<ModMetaData><name>T2-32 shotgun chart benchmark</name><author>Helodrace analysis</author><packageId>helodrace.analysis.t232</packageId><supportedVersions><li>1.7</li></supportedVersions><loadAfter><li>BlueBackedFish.Helodrace.Main</li></loadAfter></ModMetaData>')
dotnet build (Join-Path $PSScriptRoot 'DamageBench.csproj') ("-p:OutputPath="+(Join-Path $module 'Assemblies/')) -v:minimal
if($LASTEXITCODE -ne 0){throw 'Chart benchmark build failed'}
$arguments=@(('-hdT232='+$runtime.Evidence),('-hdT232Samples='+$Samples))
if($arguments.Count -ne 2){throw 'Expected separate output and sample-count options'}
$metadata=@{analysis='T2-32';samples=$Samples;sourceCommit=(git -C $runtime.Repository rev-parse HEAD);benchmarkAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $module 'Assemblies/Helodrace.T232Bench.dll')).Hash;benchmarkSourceSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'DamageBench.cs')).Hash}
$process=Start-Game17Runtime -Runtime $runtime -Arguments $arguments -Metadata $metadata -ResultFile (Join-Path $runtime.Evidence 'status.txt') -CompletionPattern '(?m)^COMPLETE\r?$'
Write-Output ('Observe same PID using Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot "'+$runtime.Root+'" -TimeoutSeconds 45')
