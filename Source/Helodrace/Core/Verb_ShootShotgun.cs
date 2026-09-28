using Verse;

namespace Helodrace
{
    /// <summary>
    /// Verb properties for weapons that launch several independent projectiles
    /// from a single shell. Burst settings remain available for weapons that
    /// fire more than one shell per attack.
    /// </summary>
    public sealed class VerbProperties_Shotgun : VerbProperties
    {
        public int pelletCount = 1;
    }

    /// <summary>
    /// Launches every pellet during one logical shot. RimWorld therefore plays
    /// the report and muzzle flash once and records one shot, while each pellet
    /// still receives its own vanilla accuracy and impact calculation.
    /// </summary>
    public class Verb_ShootShotgun : Verb_Shoot
    {
        protected virtual int PelletCount
        {
            get
            {
                VerbProperties_Shotgun shotgunProps = verbProps as VerbProperties_Shotgun;
                return shotgunProps != null ? UnityEngine.Mathf.Max(1, shotgunProps.pelletCount) : 1;
            }
        }

        protected override bool TryCastShot()
        {
            if (!base.TryCastShot())
            {
                return false;
            }

            int pelletCount = PelletCount;
            for (int pelletIndex = 1; pelletIndex < pelletCount; pelletIndex++)
            {
                // The first successful projectile represents the shell being
                // fired. A later pellet failing to launch must not refund it.
                base.TryCastShot();
            }

            return true;
        }
    }
}
