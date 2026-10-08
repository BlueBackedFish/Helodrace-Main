using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    internal static class TacticalBreachTools
    {
        internal const string RecoveryJob = "HD_NewTacticalRecoverTool";
        internal const string CutterJob = "HD_NewTacticalCut";
        internal const string InstallJob = "HD_NewTacticalInstallCharge", TriggerJob = "HD_NewTacticalTriggerCharge";
        internal static CompBreachIgniter IgniterFor(Pawn pawn) => BreachExplosiveUtility.FindIgniter(pawn, BreachInitiationMode.ShockTube, false)
            ?? BreachExplosiveUtility.FindIgniter(pawn, BreachInitiationMode.TimeFuse, true);
        internal static bool CanCharge(Pawn pawn, Building barrier) => BreachExplosiveUtility.IsValidWall(barrier)
            && BreachExplosiveUtility.CountInInventory(pawn, BreachExplosiveUtility.C4Def) >= BreachExplosiveUtility.RequiredC4For(barrier)
            && IgniterFor(pawn) != null;
        internal static bool CanUse(Pawn pawn, Building barrier) => BreachExplosiveUtility.CanOperate(pawn)
            && (pawn.equipment?.Primary?.TryGetComp<CompPowerCutterBreach>() != null
                && CompPowerCutterBreach.IsValidBreachTarget(barrier)
                || CompSledgehammerBreach.WornBy(pawn) != null && CompSledgehammerBreach.IsValidTarget(pawn, barrier)
                || CanCharge(pawn, barrier));

        internal static void Remember(TacticalSquadCommand command, Pawn pawn)
        {
            Thing hammer = CompSledgehammerBreach.WornBy(pawn)?.parent;
            Thing cutter = pawn.equipment?.Primary;
            if (hammer != null && !command.BreachTools.Contains(hammer)) command.BreachTools.Add(hammer);
            if (cutter is ThingWithComps equipment && equipment.TryGetComp<CompPowerCutterBreach>() != null
                && !command.BreachTools.Contains(cutter)) command.BreachTools.Add(cutter);
            if (pawn.inventory != null)
                foreach (Thing item in pawn.inventory.innerContainer)
                    if ((item.def == BreachExplosiveUtility.C4Def || (item as ThingWithComps)?.TryGetComp<CompBreachIgniter>() != null)
                        && !command.BreachTools.Contains(item)) command.BreachTools.Add(item);
        }

        // Death can detach a donor before the next squad scheduler slot. Use the
        // captured item, not a new query of current organization membership.
        internal static Thing SourceFor(Thing tool)
        {
            if (tool == null || tool.Destroyed) return null;
            if (tool.Spawned) return tool;
            Pawn donor = tool.ParentHolder is Pawn_ApparelTracker apparel ? apparel.pawn
                : tool.ParentHolder is Pawn_EquipmentTracker equipment ? equipment.pawn
                : tool.ParentHolder is Pawn_InventoryTracker inventory ? inventory.pawn : null;
            return donor?.Dead == true ? (Thing)donor.Corpse : donor?.Downed == true ? donor : null;
        }

        internal static bool CanRecover(Pawn pawn, Thing tool) => tool != null && !tool.Destroyed && BreachExplosiveUtility.CanOperate(pawn)
            && (tool is Apparel apparel ? pawn.apparel != null && ApparelUtility.HasPartsToWear(pawn, apparel.def)
                && apparel.PawnCanWear(pawn, true) : pawn.equipment != null && tool is ThingWithComps equipment
                    && equipment.TryGetComp<CompPowerCutterBreach>() != null
                || pawn.inventory != null && (tool.def == BreachExplosiveUtility.C4Def
                    || (tool as ThingWithComps)?.TryGetComp<CompBreachIgniter>() != null))
            && (!CompBiocodable.IsBiocoded(tool) || CompBiocodable.IsBiocodedFor(tool, pawn));
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        public long ToolRecoveriesStarted, ToolRecoveriesCompleted, CutterJobsStarted;
        internal void ToolRecovered(Pawn pawn, Thing tool)
        {
            if (byPawn.TryGetValue(pawn, out TacticalSquadCommand command) && command.BreachTools.Contains(tool))
            {
                // Inventory transfer may merge C4 into a different stack and
                // destroy the captured source. Retain the actual successor gear.
                TacticalBreachTools.Remember(command, pawn);
                ToolRecoveriesCompleted++; command.PhaseStarted = GenTicks.TicksGame; Wake(pawn);
            }
        }

        private bool RecoverBreachTool(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            foreach (TacticalMemberCommand member in active)
                if (member.Job?.def.defName == TacticalBreachTools.RecoveryJob && member.Pawn.CurJob == member.Job)
                {
                    if (member.Pawn.jobs.curDriver is TacticalJobDriver driver && driver.AtPost) continue;
                    command.Breacher = member.Pawn; return true;
                }
            if (tick < command.RecoveryRetryAt) return true;
            command.RecoveryRetryAt = tick + 180;
            bool pending = false;
            var candidates = new List<KeyValuePair<TacticalMemberCommand, Thing>>();
            foreach (Thing tool in command.BreachTools)
            {
                Thing source = TacticalBreachTools.SourceFor(tool);
                if (source?.Spawned != true || source.Map != map) continue;
                pending = true;
                foreach (TacticalMemberCommand member in active)
                {
                    Pawn pawn = member.Pawn;
                    if (tick < member.RetryTick || pawn.CurJob?.playerForced == true || !TacticalBreachTools.CanRecover(pawn, tool)
                        || pawn.Position.DistanceToSquared(source.Position) > 32 * 32) continue;
                    candidates.Add(new KeyValuePair<TacticalMemberCommand, Thing>(member, tool));
                }
            }
            if (candidates.Count == 0) return pending;
            candidates.Sort((a, b) =>
            {
                int preferred = (a.Key.Pawn == command.Breacher ? 0 : 1).CompareTo(b.Key.Pawn == command.Breacher ? 0 : 1);
                return preferred != 0 ? preferred : a.Key.Pawn.Position.DistanceToSquared(TacticalBreachTools.SourceFor(a.Value).Position)
                    .CompareTo(b.Key.Pawn.Position.DistanceToSquared(TacticalBreachTools.SourceFor(b.Value).Position));
            });
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryPlan(tick)) { command.RecoveryRetryAt = tick + 1; command.DeferredWork = true; return true; }
            var candidate = candidates[command.RecoveryCandidateCursor++ % candidates.Count];
            Thing interaction = TacticalBreachTools.SourceFor(candidate.Value);
            long started = Stopwatch.GetTimestamp(); bool reachable;
            try { reachable = candidate.Key.Pawn.CanReserveAndReach(interaction, PathEndMode.ClosestTouch, Danger.Deadly); }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
            if (!reachable) return true;
            if (!CanIssue(candidate.Key)) { command.RecoveryRetryAt = tick + 1; return true; }
            if (Issue(candidate.Key, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(TacticalBreachTools.RecoveryJob), candidate.Value, interaction)))
            { command.Breacher = candidate.Key.Pawn; command.RecoveryCandidateCursor = 0; ToolRecoveriesStarted++; }
            return true;
        }
    }
}
