using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalThreatEligibility
    {
        // Capability, not CanHitTarget/Available: distance, stance, reload and
        // cooldown must not hide an approaching ranged combatant. Called only
        // for bounded, visible candidates; no global cache or stat calculation.
        public static bool Ranged(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.WorkTagIsDisabled(WorkTags.Violent)) return false;
            if (pawn.equipment != null)
                foreach (Verb verb in pawn.equipment.AllEquipmentVerbs)
                    if (Ranged(verb)) return true;
            if (pawn.verbTracker != null)
                foreach (Verb verb in pawn.verbTracker.AllVerbs)
                    if (Ranged(verb)) return true;
            if (pawn.apparel != null)
                foreach (Verb verb in pawn.apparel.AllApparelVerbs)
                    if (Ranged(verb)) return true;
            if (pawn.health?.hediffSet != null)
                foreach (Verb verb in pawn.health.hediffSet.GetHediffsVerbs())
                    if (Ranged(verb)) return true;
            foreach (ThingComp comp in pawn.AllComps)
                if (comp is CompTurretGun turret && !turret.TurretDestroyed && Ranged(turret.GunCompEq?.PrimaryVerb)) return true;
            return false;
        }

        public static bool Ranged(Verb verb)
        {
            VerbProperties props = verb?.verbProps;
            if (props == null || !props.Ranged || verb.IsMeleeAttack
                || props.rangeStat == null && props.range <= 1.42f) return false;
            // Beam/fire/spray attacks are native ranged verbs without ordinary
            // projectile damage. Smoke-only launchers are not combat contacts.
            return !props.LaunchesProjectile || verb.HarmsHealth() || verb.IsEMP();
        }
    }
}
