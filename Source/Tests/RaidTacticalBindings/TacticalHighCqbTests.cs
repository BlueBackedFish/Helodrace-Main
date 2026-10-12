using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Squads;
using Helodrace.Tactics;
using RimWorld;
using Verse;

internal static class TacticalHighCqbTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(1, 1, 1) };
        var service = new MapComponent_TacticalCommands(map);
        var group = new CombatGroup { id = "squad", formation = new FormationDef { unitLevel = "Squad" } };
        var organization = new CombatOrganization { id = "raid", faction = new Faction { def = new FactionDef { defName = "HD_HelodCivilHighFaction" } },
            rootGroups = new List<CombatGroup> { group } };
        organization.RestoreTreeLinks();
        var parent = new TacticalSquadCommand { Id = "root", Owner = service,
            Goal = new IntVec3(50,0,50), Plan = new TacticalLocalPlan { Direct = true, Opening = new IntVec3(10,0,10), Inward = IntVec3.North } };
        parent.Link.Unit = RaidTacticalUnit.ForGroup(group);
        for (int i = 0; i < 13; i++)
        {
            var definition = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)); definition.defName = "fixture";
            var pawn = new Pawn { thingIDNumber = 1000 + i, def = definition };
            pawn.health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, PawnHealthState.Mobile);
            parent.Members.Add(new TacticalMemberCommand { Pawn = pawn, Fireteam = i == 0 ? -1 : (i - 1) / 4 });
            parent.Plan.Positions.Add(new IntVec3(i + 10,0,10));
        }
        var order = TacticalHighCqbPolicy.GroupOrder(parent, Enumerable.Range(0,13).ToList());
        Check(order.Count == 13 && order.Distinct().Count() == 13 && order.IndexOf(0) == 6,
            "Single-mouth order must keep three real teams and place the commander midway.");
        parent.Plan.Direct = false; parent.Plan.EntryOrder = order;
        Check(TacticalHighCqbPolicy.EntryReleased(parent,1) && !TacticalHighCqbPolicy.EntryReleased(parent,5), "Second team must guard until first crosses.");
        Check(!TacticalHighCqbPolicy.EntryReleased(parent,0), "Middle-wave commander must not jump ahead of a blocked second team.");
        Check(TacticalHighCqbPolicy.EntryReleased(parent,5,parent.Members.Skip(5).ToList()),
            "Earlier members outside the live assault roster must not permanently block the next entry wave.");
        for (int i=1;i<=4;i++) parent.Members[i].Crossed = true;
        Check(TacticalHighCqbPolicy.EntryReleased(parent,5) && !TacticalHighCqbPolicy.EntryReleased(parent,9), "Crossing releases only the next team, without waiting for final seats.");
        Check(TacticalHighCqbPolicy.EntryReleased(parent,0), "Commander must depart with the middle team once the first team has crossed.");
        var start = AccessTools.Method(typeof(MapComponent_TacticalCommands), "StartHighCqb");
        start.Invoke(service,new object[]{parent,parent.Members.ToList(),100});
        Check(parent.CqbTeams.Count == 3 && parent.CqbTeams.SelectMany(t=>t.Members).Count()==13
            && parent.CqbTeams.SelectMany(t=>t.Members).Distinct().Count()==13, "Every member must have one child execution owner.");
        Check(parent.CqbTeams.All(t => ReferenceEquals(t.Contacts,parent.Contacts)
            && ReferenceEquals(t.SecuredCells,parent.SecuredCells) && ReferenceEquals(t.BreachTools,parent.BreachTools)),
            "Children must share contacts, secured history and recovery gear without independent services.");
        var byPawn=(Dictionary<Pawn,TacticalSquadCommand>)AccessTools.Field(typeof(MapComponent_TacticalCommands),"byPawn").GetValue(service);
        Check(parent.CqbTeams.All(t=>t.Members.All(m=>byPawn[m.Pawn]==t)),"Job callbacks must resolve to their actual CQB owner.");
        var context=AccessTools.Method(typeof(MapComponent_TacticalCommands),"RootContext");
        object[] call={parent.Members[5].Pawn,null};
        Check((bool)context.Invoke(service,call)&&ReferenceEquals(call[1],parent),"Shared medical/focus/smoke callbacks must resolve to the squad.");
        var helper = parent.Members[5];
        helper.Pawn.jobs = (Verse.AI.Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Verse.AI.Pawn_JobTracker));
        var owned = new Verse.AI.Job { count = 42 }; helper.Job = owned;
        AccessTools.Field(typeof(Verse.AI.Pawn_JobTracker), "curJob").SetValue(helper.Pawn.jobs, owned);
        parent.MedicalCare = new TacticalMedicalCare { Helper = helper, Job = owned };
        Check((bool)AccessTools.Method(service.GetType(), "OwnsMedicalJob").Invoke(service, new object[]{helper.Pawn, owned}),
            "CQB medic job must retain the squad's medical completion/resource ownership.");
        parent.FocusFire = new TacticalFocusFire { Stage = TacticalFocusStage.Focused, TargetId = 42 };
        Check((bool)AccessTools.Method(service.GetType(), "OwnsFocusTarget").Invoke(service, new object[]{helper.Pawn, owned}),
            "A squad-wide focus shot must stay authorized with a child CQB owner.");
        parent.FieldResponse = new TacticalFieldResponse { Screen = new TacticalFieldSmoke { Thrower = helper.Pawn, Job = owned } };
        Check((bool)AccessTools.Method(service.GetType(), "FieldSmokeLaunched").Invoke(service, new object[]{helper.Pawn, owned, new Bullet()})
            && parent.FieldResponse.Screen.Launched, "CQB member smoke callback must update the squad's one screen.");
        var area=TacticalHighCqbPolicy.LocalArea(new IntVec3(0,0,0),c=>c.x>=0&&c.x<3&&c.z>=0&&c.z<3);
        Check(area.Count==9 && TacticalLocalPlanner.Connected(area.ToList()),"Task area must be bounded connected floor, excluding walls.");
        Check(TacticalHighCqbPolicy.LocalArea(IntVec3.Zero,c=>true).Count==64,"Task-region work must have a hard 64-cell bound.");
        var first=new TacticalRoomFrontier {Opening=new IntVec3(20,0,20),Inward=IntVec3.North};
        Check(TacticalHighCqbPolicy.Conflicts(first,new TacticalRoomFrontier {Opening=first.Opening,Inward=IntVec3.South}),"One mouth cannot have two concurrent owners.");
        var oldGame=Current.Game;
        try
        {
            var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            game.components=new List<GameComponent>{new GameComponent_TacticalCommands(game)}; Current.Game=game;
            parent.Frontiers.Add(first); parent.Frontiers.Add(new TacticalRoomFrontier {Opening=new IntVec3(30,0,20),Inward=IntVec3.North});
            var assign=AccessTools.Method(typeof(MapComponent_TacticalCommands),"AssignCqbFrontier");
            Check((bool)assign.Invoke(service,new object[]{parent,parent.CqbTeams[0],100}),"First team gets a known boundary.");
            Check((IntVec3)AccessTools.Method(typeof(MapComponent_TacticalCommands), "PendingGoal").Invoke(null,
                new object[]{parent.CqbTeams[0]}) == first.Inside,
                "Interrupted/replanned child work must retain its assigned room instead of reverting to the bed.");
            parent.CqbSearchCursor=0;
            Check((bool)assign.Invoke(service,new object[]{parent,parent.CqbTeams[1],101})
                && parent.CqbTeams[0].CqbTask.Opening!=parent.CqbTeams[1].CqbTask.Opening,"Sibling assignment must skip a claimed boundary.");
            var budget = game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            parent.CqbSearchCursor = 0;
            Check(!(bool)assign.Invoke(service, new object[]{parent,parent.CqbTeams[2],110}) && budget.TryPlan(110),
                "Idle guard selection must not consume the plan slice needed by a busy sibling.");
            var visited = new TacticalRoomFrontier { Opening = new IntVec3(40,0,20), Inward = IntVec3.North };
            var traversed = new TacticalRoomFrontier { Opening = new IntVec3(45,0,20), Inward = IntVec3.North };
            parent.Frontiers.Add(visited); parent.Frontiers.Add(traversed);
            parent.SecuredCells.Add(visited.Inside); parent.Mission.Traversed.Add(traversed.Opening);
            Check(!(bool)assign.Invoke(service, new object[]{parent,parent.CqbTeams[2],111})
                && parent.CqbTeams[2].CqbTask == null && budget.TryPlan(111),
                "Local entered history must prevent reassignment even without a shared clear report.");
            var team = parent.CqbTeams[0];
            parent.SecuredCells.Add(team.CqbTask.Inside);
            AccessTools.Method(service.GetType(), "AdvanceRoomClear").Invoke(service,
                new object[]{team,new List<TacticalMemberCommand>(),112});
            Check(team.Phase == TacticalCommandPhase.Complete && team.CqbTask == null && team.CqbArea.Count == 0,
                "A room cleared by a sibling during assignment must release the stale task rather than retry it forever.");
            parent.SecuredCells.Remove(visited.Inside); parent.CqbSearchCursor = 2;
            Check(budget.TryPlan(113) && (bool)assign.Invoke(service,new object[]{parent,parent.CqbTeams[2],113})
                && parent.CqbSearchCursor == 2 && parent.CqbTeams[2].CqbTask == null,
                "A deferred assignment must retain its exact unentered candidate.");
            Check((bool)assign.Invoke(service,new object[]{parent,parent.CqbTeams[2],114})
                && ReferenceEquals(parent.CqbTeams[2].CqbTask,visited), "The retained candidate must be allocated on the next available slice.");
            AccessTools.Method(typeof(MapComponent_TacticalCommands),"BeginReturn").Invoke(null,new object[]{parent,102,true,false});
            Check(parent.CqbCancelRequested && parent.ReleaseAfterReturn && parent.Phase!=TacticalCommandPhase.Returning,
                "Squad release must drain child safety/return instead of bypassing live subtasks.");
        }
        finally { Current.Game=oldGame; }
        CheckSave(Check);
        CheckDoctrine(Check);
        AccessTools.Method(typeof(MapComponent_TacticalCommands),"EndHighCqb").Invoke(service,new object[]{parent});
        Check(parent.CqbTeams.Count==0 && parent.Members.All(m=>byPawn[m.Pawn]==parent),"Join/retirement must return each member to the squad owner.");
        CheckRegroup(service,parent,Check);
        Console.WriteLine("PASS: "+checks+" HIGH CQB ownership, grouping, bounded area, task allocation, release and native Scribe checks.");
    }
    private static void CheckRegroup(MapComponent_TacticalCommands service,TacticalSquadCommand source,Action<bool,string> check)
    {
        var parent=new TacticalSquadCommand {Id="narrow",Owner=service,Phase=TacticalCommandPhase.Clear,Goal=source.Goal,
            Plan=new TacticalLocalPlan {Opening=new IntVec3(115,0,169),Inward=IntVec3.East}};
        parent.Link.Unit=source.Link.Unit;parent.Members.AddRange(source.Members);
        for(int i=0;i<13;i++)parent.Plan.Positions.Add(new IntVec3(116+i/2,0,168+i%2));
        parent.Plan.RetainedOutside.Add(12);
        var mouth=new TacticalRoomFrontier {Opening=new IntVec3(122,0,170),Inward=IntVec3.North};
        parent.Frontiers.Add(mouth);parent.SecuredCells.Add(mouth.Opening-mouth.Inward);
        AccessTools.Method(service.GetType(),"StartHighCqb").Invoke(service,new object[]{parent,parent.Members.ToList(),100});
        var team=parent.CqbTeams[0];team.CqbTask=mouth;team.Phase=TacticalCommandPhase.Clear;
        team.PlanRetryAt=700;team.LastPlanFailure=TacticalPlanFailure.Busy;
        check(TacticalHighCqbPolicy.ShouldRegroup(parent,200),"An exhausted narrow-room task blocked by guard claims must not remain on permanent child retries.");
        team.LastPlanFailure=TacticalPlanFailure.Tool;
        check(!TacticalHighCqbPolicy.ShouldRegroup(parent,200),"Missing tools require recovery, not a false space regroup.");
        team.LastPlanFailure=TacticalPlanFailure.Busy;var other=parent.CqbTeams[1];other.Phase=TacticalCommandPhase.BlastWait;
        other.OpeningAction=new TacticalOpeningAction {Launched=true};
        check(!TacticalHighCqbPolicy.ShouldRegroup(parent,200),"A live sibling grenade must prevent regrouping.");
        other.Phase=TacticalCommandPhase.Complete;
        check(!TacticalHighCqbPolicy.ShouldRegroup(parent,200),"A completion flag must not hide a still-live explosive from regroup safety.");
        other.OpeningAction=null;other.Phase=TacticalCommandPhase.Stack;
        check(!TacticalHighCqbPolicy.ShouldRegroup(parent,200),"Do not cancel a sibling that is already approaching its working entrance.");
        other.Phase=TacticalCommandPhase.Complete;
        check((bool)AccessTools.Method(service.GetType(),"TryRegroupCqb").Invoke(service,new object[]{parent,200})
            &&parent.CqbTeams.Count==0&&parent.CqbTogether&&parent.Phase==TacticalCommandPhase.Clear
            &&parent.PlanRetryAt==0&&parent.FrontierCursor==0&&parent.Members.Count==13,
            "Safe regroup must preserve the boundary and all 13 members while releasing child ownership.");
        var owners=(Dictionary<Pawn,TacticalSquadCommand>)AccessTools.Field(service.GetType(),"byPawn").GetValue(service);
        var claims=(Dictionary<IntVec3,TacticalSquadCommand>)AccessTools.Field(service.GetType(),"claims").GetValue(service);
        check(parent.Members.All(m=>owners[m.Pawn]==parent)&&parent.Plan.Positions.Distinct().Count()==13
            &&parent.Plan.Positions.All(p=>claims[p]==parent)&&parent.Plan.RetainedOutside.Contains(12),
            "Combined planning must retain unique posts and exterior guard metadata under one owner instead of ignoring conflicting claims.");
        check(!(bool)AccessTools.Method(service.GetType(),"AdvanceHighCqb").Invoke(service,new object[]{parent,parent.Members.ToList(),201})
            &&parent.CqbTeams.Count==0,"A regrouped corridor must not immediately split and recreate the same deadlock.");
        AccessTools.Method(service.GetType(),"EndHighCqb").Invoke(service,new object[]{parent});
    }
    private static void CheckSave(Action<bool,string> check)
    {
        var types = AccessTools.Field(typeof(GenTypes), "allTypesCached");
        var originalTypes = types.GetValue(null);
        var oldLog = UnityEngine.Debug.unityLogger.logHandler;
        var log = new ConsoleLog(); UnityEngine.Debug.unityLogger.logHandler = log;
        types.SetValue(null, typeof(TacticalSquadCommand).Assembly.GetTypes().Concat(typeof(Scribe).Assembly.GetTypes()).ToList());
        var cacheField = AccessTools.Field(typeof(GenTypes), "typeCache");
        var oldCache = cacheField.GetValue(null);
        var cache = (System.Collections.IDictionary)Activator.CreateInstance(cacheField.FieldType);
        var keyType = typeof(GenTypes).GetNestedType("TypeCacheKey", System.Reflection.BindingFlags.NonPublic);
        var keyConstructor = AccessTools.Constructor(keyType, new[]{typeof(string),typeof(string)});
        foreach (var type in typeof(TacticalSquadCommand).Assembly.GetTypes())
            cache[keyConstructor.Invoke(new object[]{type.FullName,null})] = type;
        cacheField.SetValue(null, cache);
        // The console has no Unity editor/mod-loading environment. Supply the
        // mod type catalogue and bypass old-version converters, not Scribe.
        bool oldChanged = ScribeMetaHeaderUtility.modListChanged;
        ScribeMetaHeaderUtility.modListChanged = true;
        var converters = AccessTools.Field(typeof(BackCompatibility), "conversionChain");
        var oldConverters = converters.GetValue(null);
        converters.SetValue(null, new List<BackCompatibilityConverter>());
        var root=new TacticalSquadCommand {Id="root",CqbCursor=2,CqbCancelRequested=true,CqbPassageSequence=5,CqbTogether=true};
        root.Phase=TacticalCommandPhase.Enter;
        root.Plan=new TacticalLocalPlan {Direct=true,Opening=new IntVec3(9,0,9),Inward=IntVec3.North};
        var previewType=typeof(TacticalSquadCommand).Assembly.GetType("Helodrace.Tactics.TacticalEntryPreview",true);
        var previewScan=new TacticalRoomScan(IntVec3.Zero,IntVec3.Invalid,c=>c==IntVec3.Zero,_=>false);
        var preview=AccessTools.Constructor(previewType,new[]{typeof(TacticalSquadCommand),typeof(TacticalRoomScan)})
            .Invoke(new object[]{root,previewScan});
        AccessTools.Field(root.GetType(),"EntryPreview").SetValue(root,preview);
        root.Members.Add(new TacticalMemberCommand());
        var team=new TacticalSquadCommand {Id="root::cqb:1",CqbTeam=1,CqbEntered=true,Phase=TacticalCommandPhase.BlastWait,
            CqbTask=new TacticalRoomFrontier {Opening=new IntVec3(4,0,4),Inward=IntVec3.North},
            CqbFollow=true,CqbFollowSeen=4,CqbFollowGoal=new IntVec3(4,0,6),
            OpeningAction=new TacticalOpeningAction {Launched=true,Returned=false,Target=new IntVec3(4,0,7),KnownHelodPrisoner=true,
                SupportCursor=3,SupportSkip=TacticalSupportSkip.UnsafeFriendly,
                GrenadeChecks=4,TrajectoryChecks=2,UnsafeChecks=2,SupportRoomReady=true,
                SupportRoomCells=new HashSet<IntVec3>{new IntVec3(11,0,11),new IntVec3(12,0,11)},
                SupportTargets=new List<IntVec3>{new IntVec3(11,0,11),new IntVec3(12,0,11)}}};
        AccessTools.Field(typeof(TacticalSquadCommand),"CqbRoster").SetValue(team,new List<int>{0}); root.CqbTeams.Add(team);
        string path=Path.Combine(Path.GetTempPath(),"hd-cqb-"+Guid.NewGuid()+".xml");
        try
        {
            Scribe.saver.InitSaving(path,"root"); root.ExposeData(); Scribe.saver.FinalizeSaving();
            var restored=new TacticalSquadCommand(); Scribe.loader.InitLoading(path); restored.ExposeData(); Scribe.loader.ForceStop();
            var child=restored.CqbTeams.Single();
            check(restored.CqbCursor==2 && restored.CqbCancelRequested && child.CqbTeam==1 && child.CqbEntered
                && child.Phase==TacticalCommandPhase.BlastWait && child.OpeningAction.Launched && !child.OpeningAction.Returned,
                "Save must preserve team assignment, cancellation and live grenade safety.");
            check(restored.CqbPassageSequence==5 && child.CqbFollow && child.CqbFollowSeen==4
                && child.CqbFollowGoal==new IntVec3(4,0,6) && child.OpeningAction.KnownHelodPrisoner,
                "Save must retain ordered cleared-passage follow and actually observed prisoner knowledge.");
            check(child.OpeningAction.SupportCursor==3&&child.OpeningAction.SupportSkip==TacticalSupportSkip.UnsafeFriendly,
                "Save must retain bounded support selection progress and skip diagnostics without resetting launched safety.");
            check(child.OpeningAction.SupportTargets.SequenceEqual(team.OpeningAction.SupportTargets),
                "Save must preserve bounded target alternatives and selection cursor together.");
            check(child.OpeningAction.SupportRoomReady && child.OpeningAction.SupportRoomCells.SetEquals(team.OpeningAction.SupportRoomCells),
                "Save must retain the sealed grenade safety footprint with its readiness state.");
            check(child.OpeningAction.GrenadeChecks==4&&child.OpeningAction.TrajectoryChecks==2&&child.OpeningAction.UnsafeChecks==2,
                "Save must retain aggregate support rejection evidence rather than report only the last grenade-less carrier.");
            check(restored.Members.Count==1 && child.Members.Count==0
                && ((List<int>)AccessTools.Field(typeof(TacticalSquadCommand),"CqbRoster").GetValue(child)).Single()==0,
                "Child save must store root member indices rather than duplicate pawn/job records.");
            check(log.Errors == 0, "Native nested Scribe must load without errors.");
            check(restored.CqbTogether&&restored.Phase==TacticalCommandPhase.Enter&&restored.SecuredCells.Count==0
                &&AccessTools.Field(restored.GetType(),"EntryPreview").GetValue(restored)==null
                &&!(bool)AccessTools.Field(restored.GetType(),"SurveyPending").GetValue(restored),
                "Saving during speculative entry must retain Enter without serializing the preview or inventing pending/secured survey state.");
        }
        finally
        {
            Scribe.ForceStop(); if(File.Exists(path))File.Delete(path); types.SetValue(null, originalTypes);
            ScribeMetaHeaderUtility.modListChanged = oldChanged; converters.SetValue(null, oldConverters);
            UnityEngine.Debug.unityLogger.logHandler = oldLog;
            cacheField.SetValue(null, oldCache);
        }
    }
    private static void CheckDoctrine(Action<bool,string> check)
    {
        int gap=TacticalCqbDoctrine.EntryPenalty(true,true,false,false,false);
        int open=TacticalCqbDoctrine.EntryPenalty(true,false,true,true,false);
        int door=TacticalCqbDoctrine.EntryPenalty(true,false,false,true,false);
        int wall=TacticalCqbDoctrine.EntryPenalty(true,false,false,false,false);
        check(gap<open && open<door && door<wall, "HIGH local priority must be passage, observed open door, closed door, wall.");
        check(TacticalCqbDoctrine.EntryPenalty(false,false,false,false,false)<TacticalCqbDoctrine.EntryPenalty(false,false,false,true,false)
            && TacticalCqbDoctrine.EntryPenalty(false,false,false,false,true)<0,"LOW prefers a fresh wall over risky doors but reuses secured passages.");
        check(!TacticalCqbDoctrine.Support(false,false,16,false,false,false)
            && TacticalCqbDoctrine.Support(false,false,16,true,false,false), "Small-room conservation must retain the actually observed threat exception.");
        check(!TacticalCqbDoctrine.Support(false,false,40,true,true,true)
            && TacticalCqbDoctrine.Support(false,true,1,false,true,false), "Observed captives suppress indoor support while safe outdoor HC remains allowed.");
        check(!TacticalCqbDoctrine.Lethal(true,false,false,false) && TacticalCqbDoctrine.Lethal(true,true,false,false)
            && TacticalCqbDoctrine.Lethal(true,false,true,false) && !TacticalCqbDoctrine.Lethal(true,true,true,true),
            "HIGH lethal support requires threat/combat evidence and must preserve the prisoner exception.");
        check(TacticalCqbDoctrine.GrenadePreference(true,false,"HD_Grenade_M67_Item")<0
            && TacticalCqbDoctrine.GrenadePreference(true,false,"HD_Grenade_M84_Item")>0
            && TacticalCqbDoctrine.GrenadePreference(true,true,"HD_Grenade_M67_Item")>TacticalCqbDoctrine.GrenadePreference(true,true,"HD_Grenade_M84_Item")
            && TacticalCqbDoctrine.GrenadePreference(true,true,"HD_Grenade_M7A2_Item")<0,
            "HIGH must select flash when unknown, allow/prefer frag with evidence and reject CS.");
    }
    private sealed class ConsoleLog : UnityEngine.ILogHandler
    {
        public int Errors;
        public void LogException(Exception error, UnityEngine.Object context) => Console.WriteLine(error);
        public void LogFormat(UnityEngine.LogType type, UnityEngine.Object context, string format, params object[] args)
        {
            if (type == UnityEngine.LogType.Error || type == UnityEngine.LogType.Exception) Errors++;
            Console.WriteLine(type + ": " + string.Format(format, args));
        }
    }
}
