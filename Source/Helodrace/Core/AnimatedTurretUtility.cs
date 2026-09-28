using RimWorld;
using Verse;

namespace Helodrace
{
    /// <summary>
    /// Implemented by compatibility-layer turret classes whose gun and top
    /// are not exposed through vanilla Building_TurretGun.
    /// </summary>
    public interface IAnimatedTurret
    {
        TurretTop AnimatedTop { get; }
        CompEquippable AnimatedGunCompEq { get; }
        LocalTargetInfo AnimatedCurrentTarget { get; }
        Verb AnimatedAttackVerb { get; }
    }

    public static class AnimatedTurretUtility
    {
        public static TurretTop Top(ThingWithComps turret)
        {
            return turret is Building_TurretGun vanilla
                ? vanilla.Top
                : (turret as IAnimatedTurret)?.AnimatedTop;
        }

        public static CompEquippable GunCompEq(ThingWithComps turret)
        {
            return turret is Building_TurretGun vanilla
                ? vanilla.GunCompEq
                : (turret as IAnimatedTurret)?.AnimatedGunCompEq;
        }

        public static LocalTargetInfo CurrentTarget(ThingWithComps turret)
        {
            return turret is Building_TurretGun vanilla
                ? vanilla.CurrentTarget
                : (turret as IAnimatedTurret)?.AnimatedCurrentTarget
                    ?? LocalTargetInfo.Invalid;
        }

        public static Verb AttackVerb(ThingWithComps turret)
        {
            return turret is Building_TurretGun vanilla
                ? vanilla.AttackVerb
                : (turret as IAnimatedTurret)?.AnimatedAttackVerb;
        }
    }
}
