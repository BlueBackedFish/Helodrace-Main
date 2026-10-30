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
        // Angular standard deviation, in degrees. Ammunition can scale it.
        public float spreadAngle = 0.9f;
    }

    /// <summary>
    /// One vanilla shot supplies accuracy/cover data, consumes one shell and
    /// records one shot. Pellets share a continuous aim with individual offsets.
    /// </summary>
    public class Verb_ShootShotgun : Verb_Shoot
    {
        protected virtual int PelletCount
        {
            get
            {
                VerbProperties_Shotgun shotgunProps = verbProps as VerbProperties_Shotgun;
                return shotgunProps != null ? Waylith.Math.Mathf.Max(1, shotgunProps.pelletCount) : 1;
            }
        }

        protected override bool TryCastShot()
        {
            ShotgunLaunch previous = ShotgunSpreadContext.Launching;
            int pelletCount = PelletCount;
            ShotgunSpreadContext.Launching = pelletCount > 1
                ? new ShotgunLaunch(this, pelletCount, (verbProps as VerbProperties_Shotgun)?.spreadAngle ?? 0.9f)
                : null;
            try
            {
                if (!base.TryCastShot())
                {
                    return false;
                }

                ShotgunSpreadContext.Launching?.LaunchRemaining();

                return true;
            }
            finally
            {
                ShotgunSpreadContext.Launching = previous;
            }
        }
    }
}
