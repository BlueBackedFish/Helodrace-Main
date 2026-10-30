using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    // Value-only intent. Updating an intent does not restart the job or its gun stance.
    public sealed class TacticalCqbOrder : IExposable
    {
        public string Owner;
        public int Version, Revision, StartLeg;
        public bool Ingress, Combat, Reposition;
        public IntVec3 Destination, Face, Outside, Opening, Inside, Lane = IntVec3.Invalid;
        public bool SameRoute(TacticalCqbOrder other) => other != null && Owner == other.Owner
            && Version == other.Version && Ingress == other.Ingress && Combat == other.Combat && Reposition==other.Reposition
            && Destination == other.Destination && (!Ingress || Outside == other.Outside
                && Opening == other.Opening && Inside == other.Inside && Lane == other.Lane);
        public int LastLeg => Ingress ? 4 : 0;
        public IntVec3 Cell(int leg) => !Ingress || leg >= 4 ? Destination
            : leg == 0 ? Outside : leg == 1 ? Opening : leg == 2 ? Inside : Lane;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Owner,"owner"); Scribe_Values.Look(ref Version,"version");
            Scribe_Values.Look(ref Revision,"revision"); Scribe_Values.Look(ref StartLeg,"startLeg");
            Scribe_Values.Look(ref Ingress,"ingress"); Scribe_Values.Look(ref Combat,"combat");
            Scribe_Values.Look(ref Reposition,"reposition");
            Scribe_Values.Look(ref Destination,"destination"); Scribe_Values.Look(ref Face,"face");
            Scribe_Values.Look(ref Outside,"outside"); Scribe_Values.Look(ref Opening,"opening");
            Scribe_Values.Look(ref Inside,"inside"); Scribe_Values.Look(ref Lane,"lane",IntVec3.Invalid);
        }
    }
    public sealed partial class TacticalMemberCommand { public TacticalCqbOrder CqbOrder; }

    public sealed partial class MapComponent_TacticalCommands
    {
        internal TacticalMemberCommand ActorMember(Pawn pawn, Job job) => byPawn.TryGetValue(pawn,out var c)
            ? c.Members.Find(m=>m.Pawn==pawn && m.Job==job) : null;
        internal bool ActorValid(TacticalMemberCommand member, TacticalCqbOrder order) => order != null
            && byPawn.TryGetValue(member.Pawn,out var c) && c.Id==order.Owner && c.PlanVersion==order.Version
            && !c.Terminal && c.Phase!=TacticalCommandPhase.Returning;
        internal bool ActorMovementAllowed(TacticalMemberCommand member,TacticalCqbOrder order)
        {
            if(!byPawn.TryGetValue(member.Pawn,out var c)) return false;
            if(order.Ingress && c.Phase!=TacticalCommandPhase.Enter && c.Phase!=TacticalCommandPhase.Clear
                && c.Phase!=TacticalCommandPhase.Complete) return false;
            if(c.Plan.Direct) return true;
            bool blast=c.OpeningAction?.Launched==true && !c.OpeningAction.EffectsCleared
                || c.ChargeAction!=null && !c.ChargeAction.EffectsCleared;
            IntVec3 target=order.Destination-c.Plan.Opening,source=member.Pawn.Position-c.Plan.Opening;
            return !blast || target.x*c.Plan.Inward.x+target.z*c.Plan.Inward.z<c.Plan.Depth
                || source.x*c.Plan.Inward.x+source.z*c.Plan.Inward.z>=c.Plan.Depth;
        }
        private static bool ContinuingActor(TacticalMemberCommand member) => member.Job != null
            && member.Pawn.CurJob==member.Job && member.Pawn.jobs.curDriver is JobDriver_TacticalActor;
        private static bool IngressJob(TacticalMemberCommand member) => member.Job?.def.defName=="HD_NewTacticalIngress"
            || ContinuingActor(member) && member.CqbOrder?.Ingress==true;
        private bool ActorContext(TacticalMemberCommand member,out TacticalSquadCommand c) =>
            byPawn.TryGetValue(member.Pawn,out c) && c.Plan!=null && !c.Defensive && c.FieldResponse==null
            && c.ContactResponse==null && !c.ContactRestoring && c.Phase!=TacticalCommandPhase.Returning
            && !c.Terminal && (!c.Plan.Direct || c.Plan.Opening.InBounds(map) && c.Plan.Opening.Roofed(map));
        private bool PrepareActor(TacticalMemberCommand member,ref Job requested)
        {
            bool ingress=requested.def.defName=="HD_NewTacticalIngress";
            if ((!ingress && requested.def.defName!="HD_NewTacticalPost") || !ActorContext(member,out var c)) return false;
            var order=new TacticalCqbOrder { Owner=c.Id,Version=c.PlanVersion,Ingress=ingress,
                Combat=ingress || requested.count==1,Destination=ingress?requested.targetC.Cell:requested.targetA.Cell,
                Face=requested.targetB.Cell,Outside=requested.targetA.Cell,Opening=requested.targetB.Cell,
                Inside=ingress?requested.targetQueueA[0].Cell:IntVec3.Invalid,
                Lane=ingress && requested.targetQueueA.Count>1?requested.targetQueueA[1].Cell:IntVec3.Invalid,
                StartLeg=ingress?ActorIngressStart(c,member):0 };
            order.Reposition=!ingress && order.Combat && CoverMove(c,member,order.Destination);
            PublishActorOrder(member,order);
            if (ContinuingActor(member)) return true;
            requested=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalActor"),order.Destination,order.Face);
            requested.count=order.Combat?1:0;
            return false;
        }
        private static void PublishActorOrder(TacticalMemberCommand member,TacticalCqbOrder order)
        {
            TacticalCqbOrder previous=member.CqbOrder;
            if(order.Ingress && TacticalCqbProgressPolicy.Physical(member,order.Version,order.Owner)) order.StartLeg=4;
            order.Revision=previous?.Revision??0;
            if (!order.SameRoute(previous)) order.Revision++;
            else order.StartLeg=previous.StartLeg;
            member.CqbOrder=order;
        }
        private void PublishPostIntent(TacticalMemberCommand member,IntVec3 destination,IntVec3 face,bool combat=false)
        {
            if(member.Pawn.CurJob?.playerForced==true || !ActorContext(member,out var c)) return;
            PublishActorOrder(member,new TacticalCqbOrder { Owner=c.Id,Version=c.PlanVersion,
                Destination=destination,Face=face,Combat=combat,Reposition=combat && CoverMove(c,member,destination) });
        }
        private void PublishIngressIntent(TacticalSquadCommand c,TacticalMemberCommand member,int index)
        {
            if(member.Pawn.CurJob?.playerForced==true || !ActorContext(member,out var execution) || execution!=c) return;
            var p=c.Plan;
            PublishActorOrder(member,new TacticalCqbOrder { Owner=c.Id,Version=c.PlanVersion,Ingress=true,Combat=true,
                Destination=p.Positions[index],Face=p.Opening,Outside=p.Outside,Opening=p.Opening,Inside=p.Inside,
                Lane=p.ReportedPassage?IntVec3.Invalid:p.EntryLane,
                StartLeg=ActorIngressStart(c,member) });
        }
        private static int ActorIngressStart(TacticalSquadCommand c,TacticalMemberCommand member)
        {
            if(TacticalCqbProgressPolicy.Physical(member,c.PlanVersion,c.Id)) return 4;
            bool same=member.CqbOrder?.Owner==c.Id && member.CqbOrder.Version==c.PlanVersion;
            if(same && member.Pawn.jobs.curDriver is TacticalJobDriver driver)
                same=driver.PlanVersion==c.PlanVersion && driver.CqbOwnerId==c.Id;
            return same && member.Crossed?3:same && member.Passed || c.Plan.ReportedPassage?1:0;
        }
        private static bool CoverMove(TacticalSquadCommand c,TacticalMemberCommand member,IntVec3 destination) =>
            TacticalEntryCombatPolicy.ArrivalPhase(c.Phase) && member.EntryCoverOwner==c.Id
                && member.EntryCoverVersion==c.PlanVersion && member.EntryCoverAnchor.IsValid && destination!=member.EntryCoverAnchor;
    }

    public sealed class JobDriver_TacticalActor : TacticalJobDriver
    {
        private int revision=-1, leg, quietUntil, nextShot;
        private IntVec3 pathGoal=IntVec3.Invalid;
        private IntVec3 lastCell=IntVec3.Invalid;
        private int nextStepAt;
        private TacticalMemberCommand member;
        internal string RouteDiagnostic => "leg=" + leg + ":revision=" + revision + ":pathGoal=" + pathGoal
            + ":atPost=" + AtPost + ":quietUntil=" + quietUntil + ":nextStep=" + nextStepAt
            + ":busy=" + pawn.stances.FullBodyBusy + ":stance=" + pawn.stances.curStance?.GetType().Name
            + ":order=" + (member?.CqbOrder == null ? "none" : "start=" + member.CqbOrder.StartLeg
                + ":revision=" + member.CqbOrder.Revision + ":ingress=" + member.CqbOrder.Ingress
                + ":route=" + string.Join("/", Enumerable.Range(0, member.CqbOrder.LastLeg + 1)
                    .Select(member.CqbOrder.Cell)));
        public override IntVec3 PostCell => member?.CqbOrder?.Destination ?? job.targetA.Cell;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner(); job.canUseRangedWeapon=false;
            // Native rotation prioritizes the firing stance, then the next
            // path cell, and only faces B (the watch direction) when stopped.
            // Owning facing for this whole continuing toil freezes transit.
            rotateToFace=TargetIndex.B;
            var actor=ToilMaker.MakeToil("TacticalCqbActor");
            actor.defaultCompleteMode=ToilCompleteMode.Never;
            actor.tickAction=Step;
            yield return actor;
        }
        private void Step()
        {
            if(!pawn.Spawned || pawn.Dead || pawn.Downed || pawn.CurJob!=job) return;
            int tick=GenTicks.TicksGame;
            if(member?.Job==job && member.RoomWatchUntil<=tick && member.CqbOrder?.Revision==revision && pawn.Position==lastCell
                && AtPost && tick<nextStepAt) return;
            lastCell=pawn.Position; nextStepAt=tick+3;
            var owner=pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
            if (owner==null) { EndJobWith(JobCondition.Incompletable); return; }
            member=member??owner.ActorMember(pawn,job);
            // A short rear watch overlays execution, not the next plan. Keep
            // accepting future intents so expiry resumes the newest route.
            if (member != null && owner.RoomWatching(member, tick))
            {
                AtPost = false;
                if (pawn.pather.Moving) pawn.pather.StopDead();
                job.targetB = member.RoomWatchFace;
                if (tick >= nextShot)
                {
                    bool checkedNow = owner.TryRoomWatchFire(pawn, job, tick);
                    nextShot = tick + TacticalPostFirePolicy.CheckDelay(checkedNow, pawn.thingIDNumber);
                }
                return;
            }
            TacticalCqbOrder order=member?.CqbOrder;
            if(member==null || !owner.ActorValid(member,order))
            { if(pawn.pather.Moving) pawn.pather.StopDead(); AtPost=false; return; } // Await an admitted new role/intent.
            if(revision!=order.Revision)
            {
                revision=order.Revision; leg=order.StartLeg; AtPost=false; quietUntil=0;
                member.LastProgressTick=tick; member.LastPosition=pawn.Position;
                // A different post/portal is a real movement change, not a new Job.
                IntVec3 next=order.Cell(leg);
                if(pathGoal!=next)
                {
                    pawn.pather.StopDead(); pathGoal=IntVec3.Invalid;
                    if(pawn.Position!=next && pawn.stances.curStance is Stance_Warmup)
                        pawn.stances.CancelBusyStanceSoft();
                }
            }
            PlanVersion=order.Version; CqbOwnerId=order.Owner;
            member.CqbExecutionVersion=order.Version;
            job.targetA=order.Destination; job.targetB=order.Face; job.count=order.Combat?1:0;
            job.locomotionUrgency=order.Combat?LocomotionUrgency.Sprint:LocomotionUrgency.Jog;
            // Consume reached portal legs immediately. Never hold or shoot in the lane.
            while(leg<order.LastLeg && (!order.Cell(leg).IsValid || pawn.Position==order.Cell(leg)))
            {
                if(leg==1) owner.PassedOpening(pawn,job,pawn.Position,PlanVersion,CqbOwnerId);
                if(leg==2) { Crossed=true; owner.CrossedInside(pawn,job,PlanVersion,CqbOwnerId); }
                leg++; pathGoal=IntVec3.Invalid;
                if(leg==4) owner.EntryLaneCleared(pawn,job,PlanVersion,CqbOwnerId);
            }
            IntVec3 goal=order.Cell(leg);
            if(leg==order.LastLeg && pawn.Position==goal)
            {
                if(!AtPost) { AtPost=true; pawn.pather.StopDead(); owner.PostArrived(pawn,job,PlanVersion,CqbOwnerId); }
                Fire(owner,order,tick,false);
                nextStepAt=nextShot;
                return;
            }
            AtPost=false;
            if(!owner.ActorMovementAllowed(member,order)) { if(pawn.pather.Moving) pawn.pather.StopDead(); return; }
            if(!order.Ingress && owner.StackMovementPaused(pawn,job)) { if(pawn.pather.Moving) pawn.pather.StopDead(); return; }
            // An admitted local cover change must actually reach that cover;
            // reacquiring the same enemy on the old exposed tile cannot cancel it.
            if(leg==order.LastLeg && !order.Reposition && Fire(owner,order,tick,true)) return;
            if(tick<quietUntil || pawn.stances.FullBodyBusy) return;
            if(pawn.pather.Moving && pathGoal==goal) return;
            if(StartAdmittedPath(goal)) pathGoal=goal;
        }
        private bool Fire(MapComponent_TacticalCommands owner,TacticalCqbOrder order,int tick,bool moving)
        {
            if(tick<nextShot) return tick<quietUntil;
            bool engaged=false;
            bool checkedNow=moving?owner.TryMovementFire(pawn,job,tick,out engaged)
                :order.Combat?owner.TryEntryFire(pawn,job,tick):owner.TryStackFire(pawn,job,tick);
            nextShot=tick+TacticalPostFirePolicy.CheckDelay(checkedNow,pawn.thingIDNumber);
            if(engaged) quietUntil=tick+TacticalEntryCombatPolicy.QuietTicks;
            return moving && tick<quietUntil;
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref revision,"orderRevision",-1);
            Scribe_Values.Look(ref leg,"routeLeg"); Scribe_Values.Look(ref pathGoal,"pathGoal",IntVec3.Invalid);
            Scribe_Values.Look(ref quietUntil,"quietUntil"); Scribe_Values.Look(ref nextShot,"nextShot");
        }
    }
}
