using RimWorld;
using Vector3 = System.Numerics.Vector3;
using GameVector3 = UnityEngine.Vector3;
using Verse;

namespace Helodrace.ModernWar
{
    public delegate bool ModularWeaponRecoilResolver(
        Thing equipment,
        float aimAngle,
        out GameVector3 drawOffset,
        out float angleOffset);

    /// <summary>
    /// Allows a combat overhaul to supply the same visual recoil transform it
    /// applies to the base weapon mesh. Vanilla recoil remains the fallback.
    /// </summary>
    public static class ModularWeaponRecoilUtility
    {
        public static ModularWeaponRecoilResolver ExternalResolver { get; set; }

        public static void Resolve(
            Thing equipment,
            float aimAngle,
            out GameVector3 drawOffset,
            out float angleOffset)
        {
            ResolveNative(equipment, aimAngle, out Vector3 nativeOffset, out angleOffset);
            drawOffset = nativeOffset.ToGame();
        }

        // External resolvers and EquipmentUtility are game API boundaries.
        // All owned adjustment and callers inside the modular renderer use native data.
        internal static void ResolveNative(
            Thing equipment,
            float aimAngle,
            out Vector3 drawOffset,
            out float angleOffset)
        {
            drawOffset = Vector3.Zero;
            angleOffset = 0f;

            ModularWeaponRecoilResolver resolver = ExternalResolver;
            if (resolver != null)
            {
                try
                {
                    if (resolver(
                        equipment,
                        aimAngle,
                        out GameVector3 externalOffset,
                        out angleOffset))
                    {
                        drawOffset = externalOffset.ToNative();
                        return;
                    }
                }
                catch (System.Exception exception)
                {
                    Log.ErrorOnce(
                        "[Helodrace] External modular weapon recoil resolver failed: "
                        + exception,
                        1947602183);
                }

                drawOffset = Vector3.Zero;
                angleOffset = 0f;
            }

            CompEquippable equippable = equipment?.TryGetComp<CompEquippable>();
            if (equippable == null) return;
            Verb_LaunchProjectile verb = EquipmentUtility.GetRecoilVerb(
                equippable.AllVerbs);
            if (verb == null) return;

            EquipmentUtility.Recoil(
                equipment.def,
                verb,
                out GameVector3 gameOffset,
                out angleOffset,
                aimAngle);

            // Vanilla's recoil helper has no instance-level stat hook. Apply the same
            // gas-adjusted raw recoil multiplier used by the converted combat stats so
            // the held-weapon kick stays consistent with the actual configuration.
            CompModularWeaponNode modular = equipment
                ?.TryGetComp<CompModularWeaponNode>();
            float gasRecoil = modular?.ConvertedStats?.GasRecoilMultiplier ?? 1f;
            drawOffset = gameOffset.ToNative() * gasRecoil;
            angleOffset *= gasRecoil;
        }
    }
}
