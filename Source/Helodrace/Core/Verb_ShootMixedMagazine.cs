using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Helodrace
{
    /// <summary>
    /// Defines a repeating projectile order for one burst. The firing index is
    /// intentionally transient and restarts at zero for every new burst.
    /// </summary>
    public sealed class MixedMagazineExtension : DefModExtension
    {
        public List<ThingDef> projectileSequence = new List<ThingDef>();
    }

    public sealed class Verb_ShootMixedMagazine : Verb_Shoot
    {
        private int projectileIndex;

        private MixedMagazineExtension Magazine =>
            EquipmentSource?.def?.GetModExtension<MixedMagazineExtension>();

        public override ThingDef Projectile
        {
            get
            {
                List<ThingDef> sequence = Magazine?.projectileSequence;
                if (sequence == null || sequence.Count == 0)
                {
                    return base.Projectile;
                }

                return sequence[projectileIndex % sequence.Count] ?? base.Projectile;
            }
        }

        public override void WarmupComplete()
        {
            // Do not carry the previous burst's position into the next burst.
            projectileIndex = 0;
            base.WarmupComplete();
        }

        protected override bool TryCastShot()
        {
            bool fired = base.TryCastShot();
            if (fired)
            {
                projectileIndex++;
                if (Caster is Building_TurretGun turret)
                {
                    turret.TryGetComp<CompM167VadsGraphic>()?.NotifyShotFired();
                }
            }

            return fired;
        }
    }
}
