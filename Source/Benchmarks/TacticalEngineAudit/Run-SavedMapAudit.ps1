param(
    [Parameter(Mandatory)][string]$SaveFile,
    [Parameter(Mandatory)][string]$AuditRoot,
    [ValidateRange(1,20000)][int]$Ticks=6000,
    [int]$SpawnX=90,
    [int]$SpawnZ=169,
    [switch]$High,
    [switch]$MethodProfile,
    [switch]$DownProbe,
    [switch]$RetreatProbe,
    [int]$GoalX=-1,
    [int]$GoalZ=132,
    [string]$ProfileTargets='Helodrace.Tactics.JobDriver_TacticalActor::Step;Helodrace.Tactics.MapComponent_TacticalCommands::ActorValid;Helodrace.Tactics.MapComponent_TacticalCommands::ActorMovementAllowed;Helodrace.Tactics.MapComponent_TacticalCommands::AdvanceAssault;Helodrace.Tactics.MapComponent_TacticalCommands::AdvanceEntryPosts;Helodrace.Tactics.MapComponent_TacticalCommands::TryPostFire;Helodrace.Tactics.MapComponent_TacticalCommands::TryLocalEntryCover;Verse.AI.Pawn_PathFollower::StartPath;Verse.AI.Pawn_PathFollower::StopDead;Verse.PathFinder::ForceCompleteScheduledJobs',
    [string]$GameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyRoot='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077',
    [string]$BlancasRoot,
    [string[]]$ExtraModRoots=@(),
    [switch]$Wait,
    [int]$TimeoutSeconds=300
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../Tests/Game17SaveRuntime.ps1')
$runtime=New-Game17SaveRuntime -SaveFile $SaveFile -RuntimeRoot $AuditRoot -GameRoot $GameRoot -HarmonyRoot $HarmonyRoot -BlancasRoot $BlancasRoot -ExtraModRoots $ExtraModRoots
$arguments=@(('-hdCqbSavedMapAudit='+(Join-Path $runtime.Root 'audit.json')),'-hdTacticalEngine=new',"-hdTacticalAuditSample=$Ticks",
    "-hdCqbSavedSpawnX=$SpawnX","-hdCqbSavedSpawnZ=$SpawnZ",'-screen-fullscreen','0','-screen-width','800','-screen-height','600')
if($High){$arguments+='-hdCqbSavedHigh=true'}
if($DownProbe){$arguments+='-hdCqbSavedDownProbe=true'}
if($RetreatProbe){$arguments+='-hdCqbSavedRetreatProbe=true'}
if($GoalX -ge 0){$arguments+="-hdCqbSavedGoalX=$GoalX","-hdCqbSavedGoalZ=$GoalZ"}
if($MethodProfile){$arguments+=('-hdMethodProfile='+(Join-Path $runtime.Root 'profiles')),'-hdMethodProfilePreset=coarse','-hdMethodProfileSpikes=true',('-hdMethodProfileTargets='+$ProfileTargets)}
$manifest=@{saveSource=$runtime.SourceSave;sourceSha256=$runtime.SourceSha256;assemblySha256=(Get-FileHash -LiteralPath (Join-Path $runtime.Game 'Mods/Main/Assemblies/Helodrace.dll')).Hash;originalMapPreserved=$true;profilerDisabled=(-not $MethodProfile);high=[bool]$High;ticks=$Ticks}
$process=Start-Game17Runtime -Runtime $runtime -Arguments $arguments -SkipQuicktest -EnableMethodProfile:$MethodProfile -Metadata $manifest
$process.Id | Set-Content -LiteralPath (Join-Path $runtime.Root 'process-id.txt')
if((Get-FileHash -LiteralPath $runtime.SourceSave).Hash -ne $runtime.SourceSha256){throw 'Source save changed unexpectedly.'}
if($Wait){
    Wait-Game17Host -RuntimeRoot $runtime.Root -TimeoutSeconds $TimeoutSeconds
    $result=Get-Content -LiteralPath (Join-Path $runtime.Root 'audit.json') -Raw | ConvertFrom-Json
    if(-not $result.complete -or $result.error){throw ('Saved-map replay failed: '+$result.error)}
    if((Get-FileHash -LiteralPath $runtime.SourceSave).Hash -ne $runtime.SourceSha256){throw 'Source save changed during the replay.'}
    Write-Output ('Saved-map replay completed: '+$runtime.Root)
}
