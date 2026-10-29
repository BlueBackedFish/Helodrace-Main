param(
    [Parameter(Mandatory=$true)][ValidateSet('baseline','load','main','blancas','main-state')][string]$Case,
    [Parameter(Mandatory=$true)][string]$RuntimeRoot,
    [string]$GameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$BlancasRoot='C:\Users\human\Documents\GitHub\Helodrace-blancasdrugs',
    [string]$HarmonyRoot='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077',
    [string]$BridgeAssembly,
    [string]$FixtureRoot,
    [switch]$Legacy16,
    [switch]$NoWindow,
    [switch]$Wait,
    [int]$TimeoutSeconds=300
)
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runtime=[IO.Path]::GetFullPath($RuntimeRoot)
$allowed=[IO.Path]::GetFullPath((Join-Path $repository '.codex-staging'))+[IO.Path]::DirectorySeparatorChar
if(-not $runtime.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Use a dedicated runtime under .codex-staging.'}
if(Test-Path -LiteralPath $runtime){throw 'Use a fresh runtime to preserve earlier evidence.'}
# Early Waylith logging uses a shared host log even with -savedatafolder.
if(Get-Process -Name RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue){throw 'Wait for the running game to exit before collecting a migration log.'}
$gameVersion=(Get-Content -LiteralPath (Join-Path $GameRoot 'Version.txt') -Raw).Trim()
if($Legacy16 -and -not $gameVersion.StartsWith('1.6.')){throw 'Legacy fixture creation requires the preserved 1.6 runtime.'}
if(-not $Legacy16 -and -not $gameVersion.StartsWith('1.7.')){throw 'A real 1.7 runtime is required.'}
if(-not $BridgeAssembly){$BridgeAssembly=Join-Path $PSScriptRoot 'bin/Debug/net10.0/Helodrace.EngineMigrationChecks.dll'}
$game=Join-Path $runtime 'Game'
$profile=Join-Path $runtime 'Profile'
$evidence=Join-Path $runtime 'Evidence'
foreach($directory in @('Game/Mods/MigrationChecks/Assemblies','Game/Mods/MigrationChecks/About','Profile/Config','Evidence')){
    New-Item -ItemType Directory -Path (Join-Path $runtime $directory) -Force | Out-Null
}
foreach($directory in Get-ChildItem -LiteralPath $GameRoot -Directory | Where-Object {$_.Name -ne 'Mods'}){
    New-Item -ItemType Junction -Path (Join-Path $game $directory.Name) -Target $directory.FullName | Out-Null
}
foreach($file in Get-ChildItem -LiteralPath $GameRoot -File){
    # Game binaries are read-only inputs; avoid copying the private CLR for every run.
    New-Item -ItemType HardLink -Path (Join-Path $game $file.Name) -Target $file.FullName | Out-Null
}
New-Item -ItemType Junction -Path (Join-Path $game 'Mods/Blancas') -Target $BlancasRoot | Out-Null
New-Item -ItemType Junction -Path (Join-Path $game 'Mods/Harmony') -Target $HarmonyRoot | Out-Null
if($Case -in @('main','main-state')){
    New-Item -ItemType Directory -Path (Join-Path $game 'Mods/Main/Assemblies') -Force | Out-Null
    foreach($directory in @('About','Defs','Patches','Languages','Textures','Sounds')){
        New-Item -ItemType Junction -Path (Join-Path $game ('Mods/Main/'+$directory)) -Target (Join-Path $repository $directory) | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $repository 'Assemblies/Helodrace.dll') -Destination (Join-Path $game 'Mods/Main/Assemblies/Helodrace.dll')
}
Copy-Item -LiteralPath $BridgeAssembly -Destination (Join-Path $game 'Mods/MigrationChecks/Assemblies/Helodrace.EngineMigrationChecks.dll')
$encoding=[Text.UTF8Encoding]::new($false)
$support=if($Legacy16){'1.6'}else{'1.7'}
[IO.File]::WriteAllText((Join-Path $game 'Mods/MigrationChecks/About/About.xml'),
    "<ModMetaData><name>Engine migration checks</name><author>Helodrace tests</author><packageId>helodrace.tests.migration</packageId><supportedVersions><li>$support</li></supportedVersions><loadAfter><li>BlueBackedFish.Helod.BlancasDrugs</li><li>BlueBackedFish.Helodrace.Main</li></loadAfter></ModMetaData>",$encoding)
$main=if($Case -in @('main','main-state')){'<li>bluebackedfish.helodrace.main</li>'}else{''}
$active='<li>brrainz.harmony</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>bluebackedfish.helod.blancasdrugs</li>'+$main+'<li>helodrace.tests.migration</li>'
[IO.File]::WriteAllText((Join-Path $profile 'Config/ModsConfig.xml'),"<ModsConfigData><version>$gameVersion</version><activeMods>$active</activeMods></ModsConfigData>",$encoding)
[IO.File]::WriteAllText((Join-Path $profile 'Config/Prefs.xml'),'<PrefsData><devMode>True</devMode><runInBackground>True</runInBackground><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic></PrefsData>',$encoding)
if($FixtureRoot){
    New-Item -ItemType Directory -Path (Join-Path $profile 'Saves') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $FixtureRoot 'Profile/Saves/BlancasMigrationFixture16.rws') -Destination (Join-Path $profile 'Saves/BlancasMigrationFixture16.rws')
    Copy-Item -LiteralPath (Join-Path $FixtureRoot 'Evidence/fixture.xml') -Destination (Join-Path $evidence 'fixture.xml')
}
$arguments=@('-quicktest',('"-savedatafolder='+$profile+'"'),('-hdMigrationCase='+$Case),('"-hdMigrationOutput='+$evidence+'"'))
if($Legacy16){$arguments+=@('-batchmode','-nographics');$executable='RimWorldWin64.exe'}
else{
    $ids='brrainz.harmony;BlueBackedFish.Helod.BlancasDrugs;helodrace.tests.migration'
    if($Case -in @('main','main-state')){$ids+=';BlueBackedFish.Helodrace.Main'}
    $arguments+=('"-mod='+$ids+'"')
    $arguments+=$(if($NoWindow){'-NoWindow'}else{'-Headless'})
    $executable='RimWorld.exe'
}
if($Legacy16){$arguments+=@('-logFile',('"'+(Join-Path $runtime 'Player.log')+'"'));$engineLog=Join-Path $runtime 'Player.log'}
else{$engineLog=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log'}
$manifest=@{version=$gameVersion;fixtureCase=$Case;gameRoot=$GameRoot;blancasRoot=$BlancasRoot;arguments=$arguments;bridgeSha256=(Get-FileHash -LiteralPath $BridgeAssembly).Hash;engineLog=$engineLog;startedUtc=[DateTime]::UtcNow.ToString('o')}
$process=Start-Process -FilePath (Join-Path $game $executable) -WorkingDirectory $game -WindowStyle Hidden -PassThru -ArgumentList $arguments
$manifest.processId=$process.Id
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runtime 'launcher.json') -Encoding UTF8
Write-Output ("Migration started: pid="+$process.Id+" root="+$runtime)
if($Wait){& (Join-Path $PSScriptRoot 'Wait-Migration.ps1') -RuntimeRoot $runtime -TimeoutSeconds $TimeoutSeconds}
