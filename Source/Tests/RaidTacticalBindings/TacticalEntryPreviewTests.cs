using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Squads;
using Helodrace.Tactics;
using RimWorld;
using Verse;

internal static class TacticalEntryPreviewTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        var service = new MapComponent_TacticalCommands(null);
        var command = new TacticalSquadCommand { Owner=service,Phase=TacticalCommandPhase.Enter,
            Goal=new IntVec3(20,0,20),Plan=new TacticalLocalPlan {Direct=true,Opening=IntVec3.Zero,Inward=IntVec3.East} };
        var a = new TacticalMemberCommand { Pawn=new Pawn(),Entered=true };
        var b = new TacticalMemberCommand { Pawn=new Pawn() };
        command.Members.AddRange(new[]{a,b}); var active=command.Members.ToList();
        AccessTools.Field(service.GetType(),"explicitGoal").SetValue(service,command.Goal);
        Type previewType=typeof(TacticalSquadCommand).Assembly.GetType("Helodrace.Tactics.TacticalEntryPreview",true);
        var previewField=AccessTools.Field(command.GetType(),"EntryPreview");
        object Preview(TacticalRoomScan scan) => AccessTools.Constructor(previewType,new[]{typeof(TacticalSquadCommand),typeof(TacticalRoomScan)})
            .Invoke(new object[]{command,scan});
        void Set(object preview,string field,object value) => AccessTools.Field(previewType,field).SetValue(preview,value);
        var advance=AccessTools.Method(service.GetType(),"AdvanceEntryPreview");
        var adopt=AccessTools.Method(service.GetType(),"AdoptEntryPreview");
        var oldGame=Current.Game;
        try
        {
            var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            game.components=new List<GameComponent>{new GameComponent_TacticalCommands(game)}; Current.Game=game;
            var scan=new TacticalRoomScan(IntVec3.Zero,IntVec3.Invalid,c=>c.z==0&&c.x>=0&&c.x<300,_=>false);
            var preview=Preview(scan); previewField.SetValue(command,preview);
            advance.Invoke(service,new object[]{command,active,100});
            Check(!scan.Finished&&scan.Cells.Count<=TacticalRoomScan.CellsPerStep+1&&service.RoomScanSteps==1,
                "Early room work must retain the existing bounded slice, not flood a whole large room at once.");
            int cells=scan.Cells.Count;
            advance.Invoke(service,new object[]{command,active,100});
            Check(scan.Cells.Count==cells&&service.RoomScanSteps==1,"A second call in one tick cannot exceed the global plan allowance.");
            Check(command.Phase==TacticalCommandPhase.Enter&&command.RoomScan==null&&command.SecuredCells.Count==0
                &&service.SharedSecured.Sequence==0&&!command.GoalSecured&&command.Mission.Traversed.Count==0,
                "A first entrant must not fabricate actual room clearance or leave Enter while the rear member is moving.");
            for(int tick=101;!scan.Finished&&tick<110;tick++)advance.Invoke(service,new object[]{command,active,tick});
            Check(scan.Finished&&service.RoomScanSteps==3,"The early survey must complete once across bounded ticks.");
            a.EntryAssignmentDone=b.EntryAssignmentDone=true;b.Entered=true;
            AccessTools.Method(service.GetType(),"BeginRoomClear").Invoke(service,new object[]{command,120});
            Check(command.Phase==TacticalCommandPhase.Clear&&ReferenceEquals(command.RoomScan,scan)&&command.SecuredCells.Count==0,
                "Completed physical entry must adopt the existing survey rather than recreate it or instantly mark its floor clear.");
            AccessTools.Method(command.GetType(),"CancelEntryPreview").Invoke(command,null);

            // Use a finished small survey and a lazy candidate to exercise the
            // actual preview/normal preparation handoff without a Unity map.
            command.RoomScan=null;command.Phase=TacticalCommandPhase.Enter;
            var tiny=new TacticalRoomScan(IntVec3.Zero,IntVec3.Invalid,c=>c==IntVec3.Zero,_=>false);tiny.Step();
            var frontier=new TacticalRoomFrontier {Opening=new IntVec3(3,0,0),Inward=IntVec3.East};
            var candidate=new TacticalLocalPlan {Opening=frontier.Opening,Inward=frontier.Inward};
            candidate.EntryOrder.AddRange(new[]{0,1});candidate.Stack.AddRange(new[]{IntVec3.Zero,IntVec3.North});
            candidate.Positions.AddRange(new[]{new IntVec3(5,0,0),new IntVec3(5,0,1)});
            int searches=0;bool disposed=false;
            IEnumerable<TacticalLocalPlan> Search()
            { try { searches++;yield return null;searches++;yield return candidate; } finally {disposed=true;} }
            var work=new TacticalPlanPreparation(command,active,frontier.Inside);
            var search=new TacticalPlanSearch();search.Start(Search());
            AccessTools.Field(work.GetType(),"Search").SetValue(work,search);
            var stage=AccessTools.Field(work.GetType(),"Stage");stage.SetValue(work,Enum.Parse(stage.FieldType,"Search"));
            preview=Preview(tiny);Set(preview,"Selected",true);Set(preview,"Frontier",frontier);Set(preview,"Work",work);previewField.SetValue(command,preview);
            advance.Invoke(service,new object[]{command,active,130});
            advance.Invoke(service,new object[]{command,active,131});
            advance.Invoke(service,new object[]{command,active,132});
            Check(searches==2&&disposed&&stage.GetValue(work).ToString()=="Commit"&&command.Preparation==null
                &&command.Plan!=candidate&&command.SecuredCells.Count==0,
                "Preview may finish a private future footprint but cannot publish it, reserve seats or report clearance.");
            Check(((Dictionary<IntVec3,TacticalSquadCommand>)AccessTools.Field(service.GetType(),"claims").GetValue(service)).Count==0
                &&service.SharedSecured.Sequence==0,"Preview must leave other teams' claims and shared clearance untouched.");
            command.Phase=TacticalCommandPhase.Clear;
            Check((bool)adopt.Invoke(service,new object[]{command,active,frontier})&&ReferenceEquals(command.Preparation,work)
                &&previewField.GetValue(command)==null&&work.Matches(command,active,frontier.Inside),
                "Clear must transfer matching suspended work to the normal final-validation path without restarting it.");
            command.Plan=candidate;
            Check(!work.Matches(command,active,frontier.Inside),"Changing the source passage must invalidate transferred work.");
            AccessTools.Method(command.GetType(),"CancelPreparation").Invoke(command,null);

            command.Phase=TacticalCommandPhase.Enter;
            preview=Preview(tiny);work=new TacticalPlanPreparation(command,active,frontier.Inside);
            Set(preview,"Frontier",frontier);Set(preview,"Work",work);previewField.SetValue(command,preview);
            command.Phase=TacticalCommandPhase.Clear;
            Check(!(bool)adopt.Invoke(service,new object[]{command,new List<TacticalMemberCommand>{a},frontier})
                &&command.Preparation==null&&previewField.GetValue(command)==null,
                "Losing a member must discard precomputed role seats at handoff.");
            command.Phase=TacticalCommandPhase.Enter;
            preview=Preview(tiny);Set(preview,"Frontier",frontier);Set(preview,"Work",new TacticalPlanPreparation(command,active,frontier.Inside));
            previewField.SetValue(command,preview);command.Goal+=IntVec3.North;command.Phase=TacticalCommandPhase.Clear;
            Check(!(bool)adopt.Invoke(service,new object[]{command,active,frontier})&&previewField.GetValue(command)==null,
                "An actual goal change must discard stale early work.");
            command.Phase=TacticalCommandPhase.Enter;
            var group=new CombatGroup {id="squad",formation=new FormationDef {unitLevel="Squad"}};
            var organization=new CombatOrganization {id="raid",faction=new Faction {def=new FactionDef {defName="HD_HelodCivilHighFaction"}},
                rootGroups=new List<CombatGroup>{group}};
            organization.RestoreTreeLinks();command.Link.Unit=RaidTacticalUnit.ForGroup(group);
            preview=Preview(tiny);previewField.SetValue(command,preview);
            advance.Invoke(service,new object[]{command,active,140});
            Check(AccessTools.Field(previewType,"Work").GetValue(preview)==null
                &&game.GetComponent<GameComponent_TacticalCommands>().WorkBudget.TryPlan(140),
                "HIGH must reuse room geometry without wasting budget on the wrong future execution roster.");
            AccessTools.Method(command.GetType(),"CancelEntryPreview").Invoke(command,null);
        }
        finally { Current.Game=oldGame; }
        Console.WriteLine("PASS: "+checks+" bounded early room survey, delayed clearance, private plan handoff, invalidation and HIGH roster checks.");
    }
}
