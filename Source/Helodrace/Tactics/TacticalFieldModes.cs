using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed partial class MapComponent_TacticalCommands
    {
        internal const string FieldModeJobName = "HD_AISetSharpshooterMode";
        private bool FieldModeNeeded(TacticalSquadCommand command, TacticalMemberCommand member, out CompSharpshooterWeapon comp, out bool enabled)
        {
            Pawn pawn = member.Pawn;
            comp = pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>(); enabled = false;
            if (pawn.Faction?.IsPlayer == true || comp == null) return false;
            bool settled = Hammer(command, pawn)
                ? (!FieldHammerMoving(command, member) || FieldAtWaypoint(command, member))
                    && pawn.CurJobDef != JobDefOf.Goto && FieldCoverAt(command, pawn.Position, command.FieldResponse.Focus)
                : member.FieldPostState == TacticalFieldPostState.Fixed && pawn.Position == member.FieldPost;
            if (!comp.CanUseSharpshooterMode || comp.AltVerb == null || !comp.Props.DefensiveAI)
            { comp.ReleaseAIMode(); return false; }
            enabled = settled;
            return comp.altModeActive != enabled && (enabled || comp.aiModeOwned);
        }
        private Job FieldModeJob(TacticalSquadCommand command, TacticalMemberCommand member)
        {
            Pawn pawn = member.Pawn;
            if (!FieldModeNeeded(command, member, out var comp, out bool enabled)) return null;
            // A preparation never cuts a native warmup or burst in half.
            if (enabled && pawn.stances.FullBodyBusy) return null;
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(FieldModeJobName), pawn, comp.parent);
            job.count = enabled ? 1 : 0;
            pendingFieldJobs[pawn] = job;
            return job;
        }
        internal static void FieldModeSafety(Pawn pawn, Job next)
        {
            var comp = pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>();
            if (comp?.aiModeOwned != true) return;
            if (next?.playerForced == true || pawn.Drafted || pawn.Faction?.IsPlayer == true)
            { comp.aiModeOwned = false; return; }
            if (pawn.IsBurning() || pawn.Dead || pawn.Downed || pawn.InMentalState
                || next?.def == JobDefOf.Goto || next?.def == JobDefOf.AttackMelee
                || next?.def?.driverClass != null && typeof(JobDriver_Flee).IsAssignableFrom(next.def.driverClass)
                || next?.def?.driverClass != null && typeof(JobDriver_FleeAndCower).IsAssignableFrom(next.def.driverClass)
                || next?.def?.driverClass != null && typeof(JobDriver_FleeAndCowerShort).IsAssignableFrom(next.def.driverClass)
                || next?.def?.defName == TacticalExplosiveEvasionPolicy.JobName
                || next?.def?.defName == InventoryGrenadeUtility.ThrowJobName)
                comp.ReleaseAIMode(); // urgent movement must not wait for a mode switch
        }
    }
}
