param(
    [Parameter(Mandatory=$true)][ValidateSet('baseline','load','blancas-drug-fixture','blancas-drug-load','blancas-drug-main','blancas-sleep-fixture','blancas-sleep-load','blancas-sleep-main','blancas-mkultra-fixture','blancas-mkultra-load','blancas-mkultra-main','main','blancas','blancas-integration','blancas-generation','main-state','native-runtime','native-math','native-geometry','native-lra7','native-grenades','native-anti-air','native-tow','native-m47','native-m167','native-shotgun','native-switchblade','native-cas','native-mh60','native-support-helicopter','native-telegraph-ui','native-economy-ui','native-research-ui','native-overlays','native-cigarette','native-preview-images','native-armor-ui','native-weapon-ui','native-attachment-ui','native-preemptive','native-small-ui','native-tactical-geometry','native-tactical-presentation','native-howitzer')][string]$Case,
    [Parameter(Mandatory=$true)][string]$RuntimeRoot,
    [string]$GameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$BlancasRoot='C:\Users\human\Documents\GitHub\Helodrace-blancasdrugs',
    [string]$HarmonyRoot='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077',
    [string]$BridgeAssembly,
    [string]$FixtureRoot,
    [switch]$Legacy16,
    [switch]$NoWindow,
    [switch]$Wait,
    [switch]$KeepCaches,
    [int]$TimeoutSeconds=300
)
$ErrorActionPreference='Stop'
$mainCases=@('blancas-drug-main','blancas-mkultra-main','blancas-sleep-main','main','blancas-integration','main-state','native-runtime','native-math','native-geometry','native-lra7','native-grenades','native-anti-air','native-tow','native-m47','native-m167','native-shotgun','native-switchblade','native-cas','native-mh60','native-support-helicopter','native-telegraph-ui','native-economy-ui','native-research-ui','native-overlays','native-cigarette','native-preview-images','native-armor-ui','native-weapon-ui','native-attachment-ui','native-preemptive','native-small-ui','native-tactical-geometry','native-tactical-presentation','native-howitzer')
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runtime=[IO.Path]::GetFullPath($RuntimeRoot)
$allowed=[IO.Path]::GetFullPath((Join-Path $repository '.codex-staging'))+[IO.Path]::DirectorySeparatorChar
if(-not $runtime.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Use a dedicated runtime under .codex-staging.'}
if(Test-Path -LiteralPath $runtime){throw 'Use a fresh runtime to preserve earlier evidence.'}
# Early Waylith logging uses a shared host log even with -savedatafolder.
if(Get-Process -Name RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue){throw 'Wait for the running game to exit before collecting a migration log.'}
$gameVersion=(Get-Content -LiteralPath (Join-Path $GameRoot 'Version.txt') -Raw).Trim()
if($Case -in @('blancas-sleep-fixture','blancas-mkultra-fixture','blancas-drug-fixture') -and -not $Legacy16){throw 'Create legacy compatibility fixtures with the frozen 1.6 release.'}
if($Legacy16 -and -not $gameVersion.StartsWith('1.6.')){throw 'Legacy fixture creation requires the preserved 1.6 runtime.'}
if(-not $Legacy16 -and -not $gameVersion.StartsWith('1.7.')){throw 'A real 1.7 runtime is required.'}
if(-not $BridgeAssembly){
    if($Legacy16){throw 'Supply the preserved net48 fixture bridge with BridgeAssembly.'}
    $bridgeFolder=if($Case -in $mainCases){'bin/Debug/net10.0'}else{'bin/Blancas/Debug/net10.0'}
    $BridgeAssembly=Join-Path $PSScriptRoot ($bridgeFolder+'/Helodrace.EngineMigrationChecks.dll')
}
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
if($Case -in $mainCases){
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
$main=if($Case -in $mainCases){'<li>bluebackedfish.helodrace.main</li>'}else{''}
$active='<li>brrainz.harmony</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>bluebackedfish.helod.blancasdrugs</li>'+$main+'<li>helodrace.tests.migration</li>'
[IO.File]::WriteAllText((Join-Path $profile 'Config/ModsConfig.xml'),"<ModsConfigData><version>$gameVersion</version><activeMods>$active</activeMods></ModsConfigData>",$encoding)
[IO.File]::WriteAllText((Join-Path $profile 'Config/Prefs.xml'),'<PrefsData><devMode>True</devMode><runInBackground>True</runInBackground><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic></PrefsData>',$encoding)
if($FixtureRoot){
    New-Item -ItemType Directory -Path (Join-Path $profile 'Saves') -Force | Out-Null
    $sleepFixture=$Case -in @('blancas-sleep-load','blancas-sleep-main')
    $mkFixture=$Case -in @('blancas-mkultra-load','blancas-mkultra-main')
    $drugFixture=$Case -in @('blancas-drug-load','blancas-drug-main')
    $fixtureSave=if($drugFixture){'BlancasDrugFixture16.rws'}elseif($mkFixture){'BlancasMkultraFixture16.rws'}elseif($sleepFixture){'BlancasSleepFixture16.rws'}else{'BlancasMigrationFixture16.rws'}
    $fixtureSnapshot=if($drugFixture){'blancas-drug-state.xml'}elseif($mkFixture){'blancas-mkultra-state.xml'}elseif($sleepFixture){'blancas-sleep.xml'}else{'fixture.xml'}
    Copy-Item -LiteralPath (Join-Path $FixtureRoot ('Profile/Saves/'+$fixtureSave)) -Destination (Join-Path $profile ('Saves/'+$fixtureSave))
    Copy-Item -LiteralPath (Join-Path $FixtureRoot ('Evidence/'+$fixtureSnapshot)) -Destination (Join-Path $evidence $fixtureSnapshot)
    if($drugFixture){Copy-Item -LiteralPath (Join-Path $FixtureRoot 'Evidence/blancas-drug-actions.xml') -Destination (Join-Path $evidence 'blancas-drug-actions.xml')}
    if($mkFixture){Copy-Item -LiteralPath (Join-Path $FixtureRoot 'Evidence/blancas-mkultra-actions.xml') -Destination (Join-Path $evidence 'blancas-mkultra-actions.xml')}
}
$arguments=@('-quicktest',('"-savedatafolder='+$profile+'"'),('-hdMigrationCase='+$Case),('"-hdMigrationOutput='+$evidence+'"'))
if($Legacy16){$arguments+=@('-batchmode','-nographics');$executable='RimWorldWin64.exe'}
else{
    $ids='brrainz.harmony;BlueBackedFish.Helod.BlancasDrugs;helodrace.tests.migration'
    if($Case -in $mainCases){$ids+=';BlueBackedFish.Helodrace.Main'}
    $arguments+=('"-mod='+$ids+'"')
    $arguments+=$(if($NoWindow){'-NoWindow'}else{'-Headless'})
    $executable='RimWorld.exe'
}
if($Legacy16){$arguments+=@('-logFile',('"'+(Join-Path $runtime 'Player.log')+'"'));$engineLog=Join-Path $runtime 'Player.log'}
else{$engineLog=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log'}
$manifest=@{version=$gameVersion;fixtureCase=$Case;gameRoot=$GameRoot;blancasRoot=$BlancasRoot;arguments=$arguments;bridgeSha256=(Get-FileHash -LiteralPath $BridgeAssembly).Hash;engineLog=$engineLog;startedUtc=[DateTime]::UtcNow.ToString('o')}
$versionFolder=if($Legacy16){'1.6'}else{'1.7'}
$engineAssembly=if($Legacy16){Join-Path $GameRoot 'RimWorldWin64_Data/Managed/Assembly-CSharp.dll'}else{Join-Path $GameRoot 'Assembly-CSharp.dll'}
$manifest.engineAssemblySha256=(Get-FileHash -LiteralPath $engineAssembly).Hash
$manifest.blancasAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $BlancasRoot ($versionFolder+'/Assemblies/HelodBlancasDrug.dll'))).Hash
$manifest.harmonyAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $HarmonyRoot ($versionFolder+'/Assemblies/0Harmony.dll'))).Hash
if($Case -in $mainCases){$manifest.mainAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $game 'Mods/Main/Assemblies/Helodrace.dll')).Hash}
$process=Start-Process -FilePath (Join-Path $game $executable) -WorkingDirectory $game -WindowStyle Hidden -PassThru -ArgumentList $arguments
$manifest.processId=$process.Id
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runtime 'launcher.json') -Encoding UTF8
Write-Output ("Migration started: pid="+$process.Id+" root="+$runtime)
if($Wait){& (Join-Path $PSScriptRoot 'Wait-Migration.ps1') -RuntimeRoot $runtime -TimeoutSeconds $TimeoutSeconds -KeepCaches:$KeepCaches}
