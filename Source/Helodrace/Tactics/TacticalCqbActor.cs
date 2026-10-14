using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    // Value-only intent. Updating an intent does not restart the job or its gun stance.
    public sealed class TacticalCqbOrder : IExposable
    {
        public string Owner;
        public int Version, Revision, StartLeg;
        public bool Ingress, Combat;
        public IntVec3 Destination, Face, Outside, Opening, Inside, Lane = IntVec3.Invalid;
        public bool SameRoute(TacticalCqbOrder other) => other != null && Owner == other.Owner
            && Version == other.Version && Ingress == other.Ingress && Combat == other.Combat
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
                Destination=destination,Face=face,Combat=combat });
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
    }

    public sealed class JobDriver_TacticalActor : TacticalJobDriver
    {
        private int revision=-1, leg, quietUntil, nextShot;
        private IntVec3 pathGoal=IntVec3.Invalid;
        private TacticalMemberCommand member;
        public override IntVec3 PostCell => member?.CqbOrder?.Destination ?? job.targetA.Cell;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner(); job.canUseRangedWeapon=false;
            var actor=ToilMaker.MakeToil("TacticalCqbActor");
            actor.defaultCompleteMode=ToilCompleteMode.Never; actor.handlingFacing=true;
            actor.tickAction=Step;
            yield return actor;
        }
        private void Step()
        {
            var owner=pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
            if (owner==null) { EndJobWith(JobCondition.Incompletable); return; }
            member=member??owner.ActorMember(pawn,job);
            TacticalCqbOrder order=member?.CqbOrder;
            if(member==null || !owner.ActorValid(member,order))
            { pawn.pather.StopDead(); AtPost=false; return; } // Await an admitted new role/intent.
            int tick=GenTicks.TicksGame;
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
                if(!pawn.stances.FullBodyBusy) pawn.rotationTracker.FaceCell(order.Face);
                return;
            }
            AtPost=false;
            if(!order.Ingress && owner.StackMovementPaused(pawn,job)) { pawn.pather.StopDead(); return; }
            if(leg==order.LastLeg && Fire(owner,order,tick,true)) return;
            if(tick<quietUntil || pawn.stances.FullBodyBusy) return;
            if(pawn.pather.Moving && pathGoal==goal) return;
            var budget=Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if(!budget.TryPath(tick)) return;
            pathGoal=goal;
            pawn.pather.StartPath(goal,PathEndMode.OnCell);
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
