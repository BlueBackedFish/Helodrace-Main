param([string]$GameRoot='C:/Program Files (x86)/Steam/steamapps/common/RimWorld', [int]$TimeoutSeconds=240, [switch]$Visible, [switch]$Interactive)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$root=Join-Path $repo '.codex-staging/ResearchWorkspaceGame'
$game=Join-Path $root 'Game'
if(Get-Process RimWorldWin64 -ErrorAction SilentlyContinue){throw 'Preserve the running game; run this test after it exits.'}
# All writes and junctions are inside the workspace; original game/mod files are only read.
foreach($dir in @('Profile/Config','Screenshots','Game/Mods/Helodrace/Assemblies','Game/Mods/WorkspaceSmoke/About')) {
    New-Item -ItemType Directory -Path (Join-Path $root $dir) -Force | Out-Null
}
foreach($dir in @('Data','MonoBleedingEdge','RimWorldWin64_Data')) {
    $path=Join-Path $game $dir
    if(-not(Test-Path -LiteralPath $path)){New-Item -ItemType Junction -Path $path -Target (Join-Path $GameRoot $dir) | Out-Null}
}
foreach($file in @('RimWorldWin64.exe','UnityPlayer.dll','steam_appid.txt')) {
    Copy-Item -LiteralPath (Join-Path $GameRoot $file) -Destination (Join-Path $game $file) -Force
}
foreach($dir in @('About','Defs','Patches','Languages','Textures','Sounds')) {
    $source=Join-Path $repo $dir; $path=Join-Path $game ('Mods/Helodrace/'+$dir)
    if((Test-Path $source) -and -not(Test-Path $path)){New-Item -ItemType Junction -Path $path -Target $source | Out-Null}
}
$harmony=Join-Path $game 'Mods/Harmony'
$harmonySource=[IO.Path]::GetFullPath((Join-Path $GameRoot '../../workshop/content/294100/2009463077'))
if(-not(Test-Path $harmony)){New-Item -ItemType Junction -Path $harmony -Target $harmonySource | Out-Null}
foreach($file in @('Helodrace.dll','Helodrace.pdb')) {
    Copy-Item -LiteralPath (Join-Path $repo ('.codex-staging/ResearchWorkspace/'+$file)) -Destination (Join-Path $game ('Mods/Helodrace/Assemblies/'+$file)) -Force
}
$encoding=[Text.UTF8Encoding]::new($false)
$screenshots=Join-Path $root ('Screenshots/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $screenshots -Force | Out-Null
$screenshots | Set-Content (Join-Path $root 'last-run.txt')
[IO.File]::WriteAllText((Join-Path $game 'Mods/WorkspaceSmoke/About/About.xml'),
    '<ModMetaData><name>Research workspace smoke test</name><author>Helodrace tests</author><packageId>helodrace.tests.researchworkspace</packageId><supportedVersions><li>1.6</li><li>1.7</li></supportedVersions><loadAfter><li>BlueBackedFish.Helodrace.Main</li></loadAfter></ModMetaData>',$encoding)
[IO.File]::WriteAllText((Join-Path $root 'Profile/Config/ModsConfig.xml'),
    '<ModsConfigData><version>1.6.4871 rev590</version><activeMods><li>brrainz.harmony</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>bluebackedfish.helodrace.main</li><li>helodrace.tests.researchworkspace</li></activeMods></ModsConfigData>',$encoding)
[IO.File]::WriteAllText((Join-Path $root 'Profile/Config/Prefs.xml'),
    '<PrefsData><screenWidth>1600</screenWidth><screenHeight>900</screenHeight><fullscreen>False</fullscreen><uiScale>1</uiScale><devMode>True</devMode><langFolderName>Korean (한국어)</langFolderName><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic></PrefsData>',$encoding)
$arguments=@('-quicktest',('"-savedatafolder='+$root+'/Profile"'),('"-hdResearchUiSmoke='+$screenshots+'"'),
    '-hdMethodProfileDisabled=true','-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',('"'+$root+'/Player.log"'))
if($Interactive){$arguments += '-hdResearchUiInteractive=true'}
$windowStyle=if($Visible){'Normal'}else{'Hidden'}
$process=Start-Process -FilePath (Join-Path $game 'RimWorldWin64.exe') -WorkingDirectory $game -WindowStyle $windowStyle -PassThru -ArgumentList $arguments
$process.Id | Set-Content (Join-Path $root 'process-id.txt')
"Isolated native UI smoke process: $($process.Id)"
$timer=[Diagnostics.Stopwatch]::StartNew()
while(-not $process.HasExited -and $timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    Start-Sleep -Milliseconds 500
}
if(-not $process.HasExited) {
    $process.Kill()
    throw "Native UI smoke timed out; isolated process stopped. Inspect $screenshots and $(Join-Path $root 'Player.log')."
}
$checks=Join-Path $screenshots 'checks.txt'
if(-not(Test-Path $checks) -or -not(Select-String -Path $checks -Pattern '^PASS: five native canvas panels rendered' -Quiet)) {
    throw "Native UI smoke did not complete; inspect $(Join-Path $root 'Player.log')."
}
if(Select-String -Path $checks -Pattern '^FAIL:' -Quiet){throw "Native UI interaction or routing failed; inspect $checks."}
Get-Content $checks
"Screenshots: $screenshots"
