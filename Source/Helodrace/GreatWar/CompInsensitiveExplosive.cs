using RimWorld;
using Verse;

namespace Helodrace
{
    public class CompProperties_InsensitiveExplosive : CompProperties_Explosive
    {
        public float minimumExplosionDamage = 30f;

        public CompProperties_InsensitiveExplosive()
        {
            compClass = typeof(CompInsensitiveExplosive);
        }
    }

    /// <summary>
    /// A CompExplosive variant which only detonates when it is struck by a
    /// sufficiently powerful explosion.
    /// </summary>
    public class CompInsensitiveExplosive : CompExplosive
    {
        private bool detonating;

        private CompProperties_InsensitiveExplosive InsensitiveProps =>
            (CompProperties_InsensitiveExplosive)props;

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            // Deliberately bypass CompExplosive's HP-based cook-off logic.
            absorbed = false;

            if (detonating
                || !parent.Spawned
                || dinfo.Def == null
                || !dinfo.Def.isExplosive
                || dinfo.Amount < InsensitiveProps.minimumExplosionDamage)
            {
                return;
            }

            detonating = true;
            // Detonate destroys the stack itself. Absorb the triggering hit so
            // DamageWorker does not continue and try to kill it a second time.
            absorbed = true;
            Map map = parent.Map;

            // StartWick records the damage instigator for kill attribution.
            // Detonate immediately: the incoming blast is the detonator.
            StartWick(dinfo.Instigator);
            Detonate(map);
        }

        public override string GetDescriptionPart()
        {
            return "HD_InsensitiveExplosive_Description".Translate(
                InsensitiveProps.minimumExplosionDamage.ToString("0.#"));
        }
    }
}
