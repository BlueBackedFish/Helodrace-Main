using System.Collections.Generic;
using System.Linq;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace.Squads
{
    public class RaidStrategyWorker_HelodSweep : RaidStrategyWorker
    {
        public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind) =>
            parms.faction?.def.GetModExtension<FactionOrganizationExtension>()?.doctrine != null
            && parms.faction.HostileTo(Faction.OfPlayer) && base.CanUseWith(parms, groupKind);
        protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed) => new LordJob_HelodSweep(parms.faction);
    }
    public sealed class LordJob_HelodSweep : LordJob
    {
        private Faction faction;
        public bool RetreatRequested;
        public bool CompletedObjective;
        public LordJob_HelodSweep() { }
        public LordJob_HelodSweep(Faction faction) { this.faction = faction; }
        public override bool GuiltyOnDowned => true;
        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            var assault = new LordToil_AssaultColonySappers { useAvoidGrid = true };
            var withdrawal = new LordToil_HelodWithdrawal();
            var exit = new LordToil_ExitMap(LocomotionUrgency.Jog, false, true) { useAvoidGrid = true };
            graph.AddToil(assault); graph.AddToil(withdrawal); graph.AddToil(exit);
            var retreat = new Transition(assault, withdrawal);
            retreat.AddTrigger(new Trigger_FractionPawnsLost(.25f));
            retreat.AddTrigger(new Trigger_TicksPassed(45000));
            retreat.AddTrigger(new Trigger_Memo("HelodSweepComplete"));
            retreat.AddTrigger(new Trigger_BecameNonHostileToPlayer());
            graph.AddTransition(retreat);
            var leave = new Transition(withdrawal, exit);
            leave.AddTrigger(new Trigger_Memo("HelodWithdrawalReady"));
            leave.AddPostAction(new TransitionAction_WakeAll()); graph.AddTransition(leave);
            return graph;
        }
        public override void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction"); Scribe_Values.Look(ref RetreatRequested, "retreatRequested");
            Scribe_Values.Look(ref CompletedObjective, "completedObjective");
        }
    }
    public sealed class LordToil_HelodWithdrawal : LordToil_AssaultColony
    {
        public override void Init()
        {
            base.Init();
            ((LordJob_HelodSweep)lord.LordJob).RetreatRequested = true;
            foreach (Pawn pawn in lord.ownedPawns)
            {
                var unit = RaidTacticalUnit.ForPawn(pawn);
                if (unit != null) RaidSupport.Cancel(unit.Organization);
            }
        }
        public override void LordToilTick()
        {
            if (lord.ticksInToil % 30 != 0) return;
            var owner = Map.GetComponent<MapComponent_TacticalCommands>();
            bool pending = owner?.Commands.Any(c => c.RaidLord == lord && !c.Terminal) == true;
            // The tactical controller completes bounded reachability checks
            // and repeated evacuation trips before releasing its command.
            if (!pending) lord.ReceiveMemo("HelodWithdrawalReady");
        }
    }
}
