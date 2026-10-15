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
        // Number of ancestors whose children may be hit by the same shell.
        public int spreadFactor = 1;
    }

    /// <summary>
    /// Launches every pellet during one logical shot. RimWorld therefore plays
    /// the report and muzzle flash once and records one shot, while each pellet
    /// still receives its own vanilla accuracy and armor calculation. Pellets
    /// share an anatomical hit region for each pawn struck by the shell.
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
            ShotgunShell previous = ShotgunSpreadContext.Launching;
            int pelletCount = PelletCount;
            ShotgunSpreadContext.Launching = pelletCount > 1
                ? new ShotgunShell((verbProps as VerbProperties_Shotgun)?.spreadFactor ?? 1)
                : null;
            try
            {
                if (!base.TryCastShot())
                {
                    return false;
                }

                for (int pelletIndex = 1; pelletIndex < pelletCount; pelletIndex++)
                {
                    // A later pellet failing to launch must not refund the shell.
                    base.TryCastShot();
                }

                return true;
            }
            finally
            {
                ShotgunSpreadContext.Launching = previous;
            }
        }
    }
}
