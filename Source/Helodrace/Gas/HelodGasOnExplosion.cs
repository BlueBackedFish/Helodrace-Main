using HarmonyLib;
using RimWorld;
using System.Reflection;
using Verse;

namespace Helodrace
{
    public sealed class HelodGasOnExplosionExtension : DefModExtension
    {
        public HelodGasDef gasDef;
        public float radius = 3.6f;
    }

    public static class HelodGasOnExplosionUtility
    {
        public static void Release(ThingDef sourceDef, IntVec3 cell, Map map)
        {
            HelodGasOnExplosionExtension extension =
                sourceDef?.GetModExtension<HelodGasOnExplosionExtension>();
            if (extension?.gasDef == null || map == null || !cell.InBounds(map))
            {
                return;
            }

            HelodGasStore.AddGasRadius(cell, map, extension.gasDef, extension.radius);
        }
    }

    [HarmonyPatch(typeof(Projectile_Explosive), "Explode")]
    internal static class Patch_HelodGasOnExplosion_Projectile
    {
        private static void Prefix(Projectile_Explosive __instance)
        {
            HelodGasOnExplosionUtility.Release(
                __instance.def,
                __instance.Position,
                __instance.Map);
        }
    }

    [HarmonyPatch]
    internal static class Patch_HelodGasOnExplosion_CompExplosive
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CompExplosive), "Detonate");
        }

        private static void Prefix(CompExplosive __instance, object[] __args)
        {
            Thing parent = __instance?.parent;
            if (parent == null)
            {
                return;
            }

            Map map = parent.MapHeld;
            if (__args != null)
            {
                for (int i = 0; i < __args.Length; i++)
                {
                    if (__args[i] is Map argumentMap)
                    {
                        map = argumentMap;
                        break;
                    }
                }
            }

            HelodGasOnExplosionUtility.Release(parent.def, parent.PositionHeld, map);
        }
    }
}
