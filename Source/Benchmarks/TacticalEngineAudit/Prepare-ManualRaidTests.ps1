param(
    [Parameter(Mandatory)][string]$SaveFile,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$GameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyRoot='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077',
    [string]$BlancasRoot,
    [string[]]$ExtraModRoots=@(),
    [switch]$Launch
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../Tests/Game17SaveRuntime.ps1')
$runtime=New-Game17SaveRuntime -SaveFile $SaveFile -RuntimeRoot $OutputRoot -GameRoot $GameRoot -HarmonyRoot $HarmonyRoot -BlancasRoot $BlancasRoot -ExtraModRoots $ExtraModRoots -PrepareOnly:(-not $Launch)
# Copy preferences into the private profile; retain the generated 1.7 mod list.
$sourceConfig=Join-Path (Split-Path (Split-Path $runtime.SourceSave)) 'Config'
if(Test-Path -LiteralPath $sourceConfig){
    Get-ChildItem -LiteralPath $sourceConfig -File -Filter '*.xml' | Where-Object {$_.Name -ne 'ModsConfig.xml'} | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $runtime.Profile 'Config') -Force
    }
}
$prefsPath=Join-Path $runtime.Profile 'Config/Prefs.xml'
[xml]$prefs=Get-Content -LiteralPath $prefsPath -Raw
foreach($setting in @{devMode='True';runInBackground='False'}.GetEnumerator()){
    $node=$prefs.DocumentElement.SelectSingleNode($setting.Key)
    if($null -eq $node){$node=$prefs.CreateElement($setting.Key);$null=$prefs.DocumentElement.AppendChild($node)}
    $node.InnerText=$setting.Value
}
$prefs.Save($prefsPath)
$hashes=@{}
foreach($relative in @('Assemblies/Helodrace.dll','Defs/Helod/Pawns/PawnKinds.xml','Defs/Organization/NewTacticalJobs.xml','Defs/GreatWar/Items/Grenades_GreatWar.xml')){
    $hash=(Get-FileHash -LiteralPath (Join-Path $runtime.Repository $relative)).Hash
    if((Get-FileHash -LiteralPath (Join-Path $runtime.Game ('Mods/Main/'+$relative))).Hash -ne $hash){throw ('Private content differs: '+$relative)}
    $hashes[$relative]=$hash
}
$runtime | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runtime.Root 'runtime.json') -Encoding UTF8
$manifest=[ordered]@{preparedUtc=[DateTime]::UtcNow.ToString('o');sourceSave=$runtime.SourceSave;sourceSha256=$runtime.SourceSha256;
    baseSave=(Join-Path $runtime.Profile 'Saves/Base.rws');outputRoot=$runtime.Root;profile=$runtime.Profile;contentSha256=$hashes;
    checklist=(Join-Path $runtime.Repository 'Docs/전술/습격 기능 약식 테스트 목록.md');methodProfilerEnabled=$false;raidSpawned=$false;gameLaunched=[bool]$Launch}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runtime.Root 'preparation.json') -Encoding UTF8
$launchText=@'
$ErrorActionPreference='Stop'
$runtime=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime.json') -Raw | ConvertFrom-Json
$prepared=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'preparation.json') -Raw | ConvertFrom-Json
foreach($entry in $prepared.contentSha256.PSObject.Properties){
    if((Get-FileHash -LiteralPath (Join-Path $runtime.Repository $entry.Name)).Hash -ne $entry.Value){throw 'Content changed; prepare a fresh manual test runtime.'}
}
. (Join-Path $runtime.Repository 'Source/Tests/Game17Runtime.ps1')
$process=Start-Game17Runtime -Runtime $runtime -Arguments @('-hdTacticalEngine=new') -SkipQuicktest -Visible
$process.Id | Set-Content -LiteralPath (Join-Path $runtime.Root 'process-id.txt')
'@
[IO.File]::WriteAllText((Join-Path $runtime.Root 'Start-TestGame.ps1'),$launchText)
$collectText=@'
param([int]$TimeoutSeconds=60)
$ErrorActionPreference='Stop'
$runtime=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime.json') -Raw | ConvertFrom-Json
& (Join-Path $runtime.Repository 'Source/Tests/Wait-Game17Host.ps1') -RuntimeRoot $runtime.Root -TimeoutSeconds $TimeoutSeconds
'@
[IO.File]::WriteAllText((Join-Path $runtime.Root 'Collect-Log.ps1'),$collectText)
$instructions=@"
Manual raid tests - private RimWorld 1.7 runtime

1. Read the batches in: $($manifest.checklist)
2. Run Start-TestGame.ps1 with PowerShell 7.6 or newer.
3. Developer mode loads Autostart.rws; Base is the preserved checkpoint.
4. No audit, raid, forced speed, invulnerability or automatic quit is enabled.
5. Save checkpoints under separate names; reload Base for incompatible branches.
6. Enable the tactical plan-node View switch once per session if required.
7. Record only completed F IDs or observed problems.
8. After quitting the game, run Collect-Log.ps1 to copy the native host log.

Original save: $($runtime.SourceSave)
New saves and preferences: $($runtime.Profile)
The live native log remains in the host's LocalLow Logs directory until collected.
Collected logs and evidence: $($runtime.Root)
Main, Blancas and Harmony are loaded from this isolated Game directory.
Reprepare in a fresh directory after changing the DLL or definitions.
"@
[IO.File]::WriteAllText((Join-Path $runtime.Root 'START-HERE.txt'),$instructions)
if($Launch){& (Join-Path $runtime.Root 'Start-TestGame.ps1')}
Write-Output ('Manual test environment prepared: '+$runtime.Root+' (launched='+[bool]$Launch+')')
