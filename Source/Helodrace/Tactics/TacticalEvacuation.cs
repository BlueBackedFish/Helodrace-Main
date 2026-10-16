using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Helodrace.Tactical;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed partial class TacticalSquadCommand
    {
        public List<Pawn> Evacuees = new List<Pawn>(), EvacuationUnreachable = new List<Pawn>(), EvacuationTried = new List<Pawn>();
        public Pawn EvacuationTesting;
        public Dictionary<Pawn,int> EvacuationFailures = new Dictionary<Pawn,int>();
        public int EvacuationScanCursor;
        public bool EvacuationScanDone, EvacuationRescan, EvacuationReady, EvacuateAll;
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        // Incremental native pawn-list reads and at most one native reach probe
        // per decision. No per-tick map/room search or path for every pair.
        private bool AdvanceEvacuation(TacticalSquadCommand command,List<TacticalMemberCommand> active,int tick)
        {
            command.Evacuees.RemoveAll(p=>p==null || p.Dead || !p.Spawned || p.Map!=map || !p.Downed);
            bool carrying=active.Any(m=>CasualtyJob(m.Pawn) && m.Pawn.CurJob.targetA.Thing is Pawn);
            if(command.Evacuees.Count==0 && !carrying && command.EvacuationRescan)
            { command.EvacuationScanDone=false; command.EvacuationScanCursor=0; command.EvacuationRescan=false; }
            if(!command.EvacuationScanDone)
            {
                var pawns=map.mapPawns.AllPawnsSpawned;
                for(int i=0;i<8 && command.EvacuationScanCursor<pawns.Count;i++)
                {
                    Pawn patient=pawns[command.EvacuationScanCursor++];
                    if(!patient.Dead && patient.Downed
                        && (command.EvacuateAll || patient.Faction==active[0].Pawn.Faction || TemporaryRestraint.Bound(patient))
                        && !command.Evacuees.Contains(patient)
                        && !command.EvacuationUnreachable.Contains(patient)) command.Evacuees.Add(patient);
                }
                command.EvacuationScanDone=command.EvacuationScanCursor>=pawns.Count;
            }
            command.EvacuationReady=command.EvacuationScanDone && command.Evacuees.Count==0 && !carrying;
            if(command.EvacuationScanDone && command.Evacuees.Count>0)
            {
                Pawn patient=command.Evacuees[0];
                if(command.EvacuationTesting!=patient)
                { command.EvacuationTesting=patient; command.EvacuationTried.Clear(); }
                bool danger=MedicalThreatActive(command,tick);
                if(!command.EvacuateAll && danger && active.All(m=>!TacticalCasualtyPolicy.Approach(true,
                    m.Pawn.Position.DistanceToSquared(patient.Position),CasualtyCovered(command,m.Pawn.Position,tick))))
                {
                    // Emergency flight keeps the existing covered-adjacent
                    // extraction rule; it must not turn into a map-wide raid.
                    command.Evacuees.Remove(patient); command.EvacuationUnreachable.Add(patient);
                    command.EvacuationTesting=null;
                }
                else
                {
                    foreach(var helper in active.OrderBy(m=>m.Pawn.Position.DistanceToSquared(patient.Position)))
                    {
                        Pawn pawn=helper.Pawn;
                        if(command.EvacuationTried.Contains(pawn) || CasualtyJob(pawn) && pawn.CurJob.targetA.Thing is Pawn
                            || tick<helper.RetryTick || KnownCareDanger(pawn) || KnownCareDanger(patient)
                            || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) || !pawn.CanReserve(patient)
                            || danger && !TacticalCasualtyPolicy.Approach(true,pawn.Position.DistanceToSquared(patient.Position),CasualtyCovered(command,pawn.Position,tick))) continue;
                        var budget=Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                        if(!budget.TryPlan(tick)) break;
                        bool reachable;
                        long started=Stopwatch.GetTimestamp();
                        try { reachable=pawn.CanReach(patient,PathEndMode.Touch,Danger.Deadly); }
                        finally { budget.Account(tick,Stopwatch.GetTimestamp()-started); }
                        if(!reachable) command.EvacuationTried.Add(pawn);
                        else if(CanIssue(helper))
                        {
                            var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(TacticalCasualtyPolicy.JobName),patient,command.WithdrawalGoal);
                            job.exitMapOnArrival=true;
                            if(Issue(helper,job))
                            { command.Evacuees.Remove(patient); command.EvacuationRescan=true; command.EvacuationTesting=null; }
                        }
                        break;
                    }
                    if(active.All(m=>command.EvacuationTried.Contains(m.Pawn)
                        || !m.Pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)))
                    { command.Evacuees.Remove(patient); command.EvacuationUnreachable.Add(patient); command.EvacuationTesting=null; }
                }
            }
            // Unladen members cover the exit instead of leaving the final
            // carriers behind. A carrier returns for the next casualty.
            foreach(var member in active)
            {
                if(CasualtyJob(member.Pawn) || tick<member.RetryTick || !CanIssue(member)) continue;
                var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(TacticalCasualtyPolicy.JobName),LocalTargetInfo.Invalid,command.WithdrawalGoal);
                job.exitMapOnArrival=true; Issue(member,job);
            }
            return true;
        }
        internal bool EvacuationMayLeave(Pawn pawn) => RootContext(pawn,out var c) && c.EvacuationReady;
        private static void EvacuationFailed(TacticalSquadCommand command,Job job,JobCondition condition)
        {
            if(!job.exitMapOnArrival || job.def.defName!=TacticalCasualtyPolicy.JobName
                || !(job.targetA.Thing is Pawn patient) || !TacticalCqbProgressPolicy.RealFailure(condition,false)) return;
            var root=command.CqbParent??command;
            root.EvacuationFailures.TryGetValue(patient,out int failures);
            root.EvacuationFailures[patient]=++failures;
            if(failures>=3 && !root.EvacuationUnreachable.Contains(patient))
            { root.Evacuees.Remove(patient); root.EvacuationUnreachable.Add(patient); }
        }
    }

    public sealed partial class JobDriver_TacticalCasualtyMove
    {
        private IEnumerable<Toil> EvacuationToils()
        {
            if(Patient!=null)
            {
                this.FailOn(()=>Patient.Dead || !Patient.Downed || !Patient.Spawned && pawn.carryTracker.CarriedThing!=Patient
                    || Patient.Spawned && MapComponent_TacticalCommands.KnownCareDanger(Patient));
                AddFinishAction(condition=>
                {
                    if(pawn.Spawned && pawn.carryTracker.CarriedThing==Patient)
                        pawn.carryTracker.TryDropCarriedThing(pawn.Position,ThingPlaceMode.Near,out _);
                });
                yield return AdmitMovement();
                yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
                var bind=Toils_General.Wait(TemporaryRestraint.WorkTicks,TargetIndex.A);
                // Binding is handled separately below; a fixed short attachment
                // interval also prevents instant pickup of unrestrained enemies.
                bind.FailOn(()=>!pawn.Position.AdjacentTo8WayOrInside(Patient.Position));
                yield return bind;
                yield return Toils_General.Do(()=>
                {
                    if(Patient.RaceProps.Humanlike && Patient.HostileTo(pawn)) TemporaryRestraint.Apply(Patient);
                });
                yield return Toils_General.Do(()=>
                {
                    if(pawn.carryTracker.CarriedThing!=null)
                        pawn.carryTracker.TryDropCarriedThing(pawn.Position,ThingPlaceMode.Near,out _);
                });
                yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            }
            var move=ToilMaker.MakeToil("TacticalEvacuateAndCover"); move.defaultCompleteMode=ToilCompleteMode.Never;
            move.tickAction=()=>
            {
                IntVec3 goal=job.targetB.Cell;
                var owner=pawn.Map.GetComponent<MapComponent_TacticalCommands>();
                if(pawn.Position==goal || pawn.Position.OnEdge(pawn.Map))
                {
                    var edge=CellRect.WholeMap(pawn.Map).GetClosestEdge(pawn.Position);
                    if(Patient!=null)
                    {
                        // Match native ExitMap's carried-pawn export, but keep
                        // this carrier available for additional trips.
                        if(pawn.Faction!=null && Patient.HostileTo(pawn))
                        { pawn.Faction.kidnapped.Kidnap(Patient,pawn); pawn.carryTracker.innerContainer.Remove(Patient); }
                        else { pawn.carryTracker.innerContainer.Remove(Patient); Patient.ExitMap(false,edge); }
                        EndJobWith(JobCondition.Succeeded);
                    }
                    else if(owner.EvacuationMayLeave(pawn)) pawn.ExitMap(false,edge);
                    else TryEvacuationFire(owner);
                    return;
                }
                TryEvacuationFire(owner);
                if(!pawn.stances.FullBodyBusy && !pawn.pather.Moving) StartAdmittedPath(goal);
            };
            yield return move;
        }
        private void TryEvacuationFire(MapComponent_TacticalCommands owner)
        {
            int tick=GenTicks.TicksGame;
            if(tick>=nextFire && !pawn.stances.FullBodyBusy)
            { nextFire=tick+30; if(owner.TryWithdrawalFire(pawn,job,tick)) nextFire=tick+240; }
        }
    }
}
