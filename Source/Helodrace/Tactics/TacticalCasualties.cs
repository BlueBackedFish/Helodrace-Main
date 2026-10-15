using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Helodrace.Squads;
using Helodrace.Tactical;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace.Tactics
{
    public static class TacticalCasualtyPolicy
    {
        public const string JobName="HD_NewTacticalCasualtyMove";
        public static bool Approach(bool danger,int distanceSquared,bool covered) =>
            distanceSquared<=2 && covered || !danger && distanceSquared<=32*32;
        public static bool RetreatPhase(LordToil toil) => toil is LordToil_ExitMap || toil is LordToil_ExitMapAndDefendSelf;
    }
    public sealed partial class TacticalSquadCommand
    {
        public bool Withdrawing;
        public int NextCasualtyCheck,CasualtyCursor;
        public IntVec3 WithdrawalGoal=IntVec3.Invalid;
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        internal bool CasualtyJob(Pawn pawn) => pawn.CurJobDef?.defName==TacticalCasualtyPolicy.JobName;
        private bool CasualtyCovered(TacticalSquadCommand command,IntVec3 cell,int tick)
        {
            foreach(var contact in command.Contacts.Memory.Entries)
                if(CareContactDanger(contact,tick) && GenSight.LineOfSight(cell,contact.Position,map,true)
                    && CoverUtility.CalculateOverallBlockChance(cell,contact.Position,map)<.35f) return false;
            return true;
        }
        // One new operation per decision, two roster checks per slice. No
        // global pawn search or continuously rebuilt rescue path/cover map.
        private bool AdvanceCasualties(TacticalSquadCommand command,int tick)
        {
            if(command.CqbParent!=null) return false;
            if(command.CqbTeams.Any(t=>t.ChargeAction!=null || t.OpeningAction?.Launched==true && !t.OpeningAction.EffectsCleared)) return false;
            bool retreat=command.Withdrawing || TacticalCasualtyPolicy.RetreatPhase(command.RaidLord?.CurLordToil)
                || command.RaidLord?.LordJob is LordJob_HelodSweep sweep && sweep.RetreatRequested;
            bool rescuing=command.Members.Any(m=>m.Pawn?.CurJob?.def.defName==TacticalCasualtyPolicy.JobName
                && m.Pawn.CurJob.targetA.Thing is Pawn);
            if(!retreat && rescuing) { command.Due=Math.Min(command.Due,tick+10); return true; }
            if(!retreat && tick<command.NextCasualtyCheck) return false;
            if(retreat && !command.Withdrawing)
            {
                if(command.ChargeAction!=null || command.OpeningAction?.Launched==true && !command.OpeningAction.EffectsCleared
                    || command.CqbTeams.Any(t=>t.ChargeAction!=null || t.OpeningAction?.Launched==true && !t.OpeningAction.EffectsCleared)) return false;
                EndHighCqb(command); command.CancelPreparation(); command.CancelEntryPreview(); ReleaseClaims(command);
                command.Withdrawing=true; command.Phase=TacticalCommandPhase.Returning;
                command.MedicalCare=null; command.ContactResponse=null; command.FocusFire=null;
                command.FieldResponse=null; command.BagRecovery=null;
                foreach(var member in command.Members)
                    member.Pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>()?.ReleaseAIMode();
            }
            var active=command.Members.Where(m=>m.Pawn?.Spawned==true && m.Pawn.Map==map && !m.Pawn.Dead && !m.Pawn.Downed
                && !m.Pawn.InMentalState && !m.Pawn.IsPrisoner && m.Pawn.CurJob?.playerForced!=true).ToList();
            if(active.Count==0) return false;
            if(retreat)
            {
                command.Due=tick+10; ScanContacts(command,active,tick);
                if(!command.WithdrawalGoal.IsValid)
                {
                    var budget=Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                    if(!budget.TryPlan(tick)) return true;
                    long started=Stopwatch.GetTimestamp();
                    try { if(!RCellFinder.TryFindBestExitSpot(active[0].Pawn,out command.WithdrawalGoal)) command.WithdrawalGoal=IntVec3.Invalid; }
                    finally { budget.Account(tick,Stopwatch.GetTimestamp()-started); }
                    if(!command.WithdrawalGoal.IsValid) { command.Due=tick+120; return true; }
                }
            }
            if((retreat || !rescuing) && tick>=command.NextCasualtyCheck && command.MedicalCare==null
                && command.ChargeAction==null && !(command.OpeningAction?.Launched==true && !command.OpeningAction.EffectsCleared))
            {
                bool danger=MedicalThreatActive(command,tick);
                for(int check=0;check<2 && command.CasualtyCursor<command.Members.Count;check++)
                {
                    var patient=command.Members[command.CasualtyCursor++].Pawn;
                    if(patient?.Spawned!=true || patient.Map!=map || patient.Dead || !patient.Downed
                        || map.GetComponent<MapComponent_TcccDragging>().IsPatient(patient)
                        || !retreat && MedicalSafe(command,patient.Position,tick)
                            && active.Any(m=>TacticalMedicalPolicy.WithinTreatmentRange(m.Pawn.Position,patient.Position)
                                && TcccUtility.CanTreat(m.Pawn,patient))) continue;
                    foreach(var helper in active.OrderBy(m=>m.Pawn.Position.DistanceToSquared(patient.Position)))
                    {
                        Pawn pawn=helper.Pawn;
                        if(CasualtyJob(pawn) && pawn.CurJob.targetA.Thing is Pawn || !TcccUtility.CanTreat(pawn,patient) || KnownCareDanger(pawn) || KnownCareDanger(patient)
                            || !pawn.CanReserve(patient) || !TacticalCasualtyPolicy.Approach(danger,
                                pawn.Position.DistanceToSquared(patient.Position),CasualtyCovered(command,pawn.Position,tick))) continue;
                        if(danger && !pawn.CanReachImmediate(patient,PathEndMode.Touch)) continue;
                        if(!TryCasualtyDestination(command,pawn,patient,retreat,tick,out IntVec3 goal)) break;
                        if(!CanIssue(helper)) break;
                        Job job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(TacticalCasualtyPolicy.JobName),patient,goal);
                        job.exitMapOnArrival=retreat;
                        if(Issue(helper,job)) { rescuing=true; command.Members.Find(m=>m.Pawn==patient).MedicalRejoinPending=true; }
                        break;
                    }
                    if(rescuing) break;
                }
                if(command.CasualtyCursor>=command.Members.Count)
                { command.CasualtyCursor=0; command.NextCasualtyCheck=tick+90; }
                else command.NextCasualtyCheck=tick+10;
            }
            if(!retreat)
            {
                if(rescuing) command.Due=Math.Min(command.Due,tick+10);
                return rescuing;
            }
            // Leave a nearby qualified rescuer briefly available while the
            // bounded casualty pass runs; others withdraw with their guns.
            foreach(var member in active)
            {
                if(CasualtyJob(member.Pawn) || tick<member.RetryTick || !CanIssue(member)) continue;
                if(!rescuing && command.CasualtyCursor>0 && member.Medic) continue;
                Job job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(TacticalCasualtyPolicy.JobName),LocalTargetInfo.Invalid,command.WithdrawalGoal);
                job.exitMapOnArrival=true; Issue(member,job);
            }
            return true;
        }
        private bool TryCasualtyDestination(TacticalSquadCommand command,Pawn helper,Pawn patient,bool retreat,int tick,out IntVec3 goal)
        {
            goal=retreat?command.WithdrawalGoal:IntVec3.Invalid;
            if(retreat) return true;
            var budget=Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if(!budget.TryPlan(tick)) return false;
            long started=Stopwatch.GetTimestamp();
            try
            {
                float best=float.MinValue;
                var pending=new Queue<IntVec3>(); var seen=new HashSet<IntVec3>{helper.Position}; pending.Enqueue(helper.Position);
                while(pending.Count>0)
                {
                    IntVec3 c=pending.Dequeue();
                    foreach(var direction in GenAdj.CardinalDirections)
                    {
                        IntVec3 next=c+direction;
                        if(seen.Count>=32 || seen.Contains(next) || !next.InBounds(map) || !next.Standable(map)
                            || next.GetEdifice(map) is Building_Door door && !door.Open || !CasualtyCovered(command,next,tick)) continue;
                        seen.Add(next); pending.Enqueue(next);
                    }
                    if(c.GetFirstPawn(map)!=null && c!=helper.Position || c.DistanceToSquared(patient.Position)<4) continue;
                    float score=(command.SecuredCells.Contains(c)?20:0)-c.DistanceToSquared(helper.Position);
                    if(score>best) { best=score; goal=c; }
                }
                return goal.IsValid;
            }
            finally { budget.Account(tick,Stopwatch.GetTimestamp()-started); }
        }
        internal bool TryWithdrawalFire(Pawn pawn,Job job,int tick)
        {
            if(!RootContext(pawn,out var command) || pawn.CurJob!=job || command.ChargeAction!=null
                || command.OpeningAction?.Launched==true && !command.OpeningAction.EffectsCleared) return false;
            Verb verb=pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if(verb==null || verb.IsMeleeAttack || pawn.equipment.Primary.TryGetComp<CompSharpshooterWeapon>()?.ReadyToFire==false) return false;
            var budget=Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if(!budget.TryObserve(tick)) return false;
            long started=Stopwatch.GetTimestamp();
            try
            {
                int checkedTargets=0;
                foreach(var contact in command.Contacts.Memory.Entries)
                {
                    if(checkedTargets++>=8) break;
                    Pawn enemy=contact.Position.InBounds(map)?contact.Position.GetFirstPawn(map):null;
                    if(enemy==null || enemy.thingIDNumber!=contact.EnemyId || enemy.Dead || enemy.Downed
                        || !enemy.HostileTo(pawn) || !verb.CanHitTarget(enemy)) continue;
                    if(!verb.Available()) return false;
                    pawn.pather.StopDead(); return verb.TryStartCastOn(enemy);
                }
                return false;
            }
            finally { budget.Account(tick,Stopwatch.GetTimestamp()-started); }
        }
    }
    public sealed class JobDriver_TacticalCasualtyMove : TacticalJobDriver
    {
        private int nextFire,firingUntil;
        private bool attached;
        private Pawn Patient=>job.targetA.Thing as Pawn;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => Patient==null || pawn.Reserve(Patient,job,1,-1,null,errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner(); job.canUseRangedWeapon=false;
            this.FailOn(()=>MapComponent_TacticalCommands.KnownCareDanger(pawn));
            this.FailOn(()=>Patient!=null && (!Patient.Spawned || Patient.Map!=pawn.Map || Patient.Dead || !Patient.Downed));
            AddFinishAction(_=> { if(Patient!=null && pawn.Map!=null) pawn.Map.GetComponent<MapComponent_TcccDragging>().Detach(pawn); });
            if(Patient!=null)
            {
                yield return AdmitMovement();
                yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
                var attach=Toils_General.Wait(TcccRules.DragAttachTicks,TargetIndex.A);
                attach.FailOn(()=>!pawn.Position.AdjacentTo8WayOrInside(Patient.Position));
                yield return attach;
                yield return Toils_General.Do(()=>
                {
                    attached=pawn.Map.GetComponent<MapComponent_TcccDragging>().Attach(pawn,Patient);
                    if(!attached) EndJobWith(JobCondition.Incompletable);
                });
            }
            var move=ToilMaker.MakeToil("TacticalCasualtyWithdraw"); move.defaultCompleteMode=ToilCompleteMode.Never;
            move.tickAction=()=>
            {
                var dragging=pawn.Map.GetComponent<MapComponent_TcccDragging>();
                if(Patient!=null && dragging.PatientFor(pawn)!=Patient) { EndJobWith(JobCondition.Incompletable); return; }
                IntVec3 goal=job.targetB.Cell;
                if(pawn.Position==goal || job.exitMapOnArrival && pawn.Position.OnEdge(pawn.Map))
                {
                    if(job.exitMapOnArrival)
                    {
                        var edge=CellRect.WholeMap(pawn.Map).GetClosestEdge(pawn.Position);
                        Pawn patient=Patient; dragging.Detach(pawn);
                        if(patient?.Spawned==true) patient.ExitMap(false,edge);
                        pawn.ExitMap(false,edge);
                    }
                    else { dragging.Detach(pawn); EndJobWith(JobCondition.Succeeded); }
                    return;
                }
                int tick=GenTicks.TicksGame;
                if(tick<firingUntil && pawn.stances.FullBodyBusy) return;
                if(tick>=firingUntil && pawn.stances.curStance is Stance_Warmup) pawn.stances.CancelBusyStanceSoft();
                if(tick>=nextFire && !pawn.stances.FullBodyBusy)
                {
                    nextFire=tick+30;
                    if(pawn.Map.GetComponent<MapComponent_TacticalCommands>().TryWithdrawalFire(pawn,job,tick))
                    { nextFire=tick+240; firingUntil=tick+150; return; }
                }
                if(!pawn.stances.FullBodyBusy && !pawn.pather.Moving) StartAdmittedPath(goal);
            };
            yield return move;
        }
        public override void ExposeData()
        { base.ExposeData(); Scribe_Values.Look(ref attached,"attached"); Scribe_Values.Look(ref nextFire,"nextFire"); Scribe_Values.Look(ref firingUntil,"firingUntil"); }
    }
}
