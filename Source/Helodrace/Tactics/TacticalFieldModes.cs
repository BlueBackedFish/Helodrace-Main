using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed partial class MapComponent_TacticalCommands
    {
        private bool PrepareFieldMode(TacticalSquadCommand command, TacticalMemberCommand member)
        {
            Pawn pawn=member.Pawn;
            var comp=pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>();
            if(pawn.Faction?.IsPlayer==true || comp==null) return false;
            if(!comp.CanUseSharpshooterMode || comp.AltVerb==null || !comp.Props.DefensiveAI)
            { comp.ReleaseAIMode(); return false; }
            bool settled=Hammer(command,pawn)
                ? (!FieldHammerMoving(command,member) || FieldAtWaypoint(command,member))
                    && pawn.CurJobDef!=JobDefOf.Goto && FieldCoverAt(command,pawn.Position,command.FieldResponse.Focus)
                : member.FieldPostState==TacticalFieldPostState.Fixed && pawn.Position==member.FieldPost;
            // Moving preserves the selection and resets passive preparation.
            // No mode-switch job or OFF barrier owns the movement path.
            return settled && !comp.altModeActive && comp.TrySetMode(true,true);
        }
        internal static void FieldModeSafety(Pawn pawn,Job next)
        {
            var comp=pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>();
            if(comp?.aiModeOwned==true && (next?.playerForced==true || pawn.Drafted || pawn.Faction?.IsPlayer==true))
                comp.ForgetAIOwnership();
        }
    }
}