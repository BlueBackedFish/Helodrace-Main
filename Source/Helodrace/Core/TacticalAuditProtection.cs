using HarmonyLib;
using Verse;

namespace Helodrace
{
    internal static class TacticalAuditProtection
    {
        internal static Pawn ProtectedOwner;
    }

    [StaticConstructorOnStartup]
    internal static class TacticalAuditBootstrap
    {
        static TacticalAuditBootstrap()
        {
            // Hidden audits must keep pumping startup before a map exists.
            // Normal player sessions retain their own background preference.
            if (GenCommandLine.TryGetCommandLineArg("hdTacticalEngineAudit", out _)
                || GenCommandLine.TryGetCommandLineArg("hdCqbSavedMapAudit", out _))
            {
                Prefs.RunInBackground = true;
                Waylith.EngineWindow.RunInBackground = true;
                if (GenCommandLine.TryGetCommandLineArg("hdCqbSavedMapAudit", out _)) Prefs.DevMode = true;
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class Patch_RaidRuntimeAudit_Owner
    {
        private static bool Prepare() => GenCommandLine.TryGetCommandLineArg("hdTacticalEngineAudit", out _)
            || GenCommandLine.TryGetCommandLineArg("hdCqbSavedMapAudit", out _);
        private static bool Prefix(Thing __instance, ref DamageWorker.DamageResult __result)
        {
            if (__instance != TacticalAuditProtection.ProtectedOwner
                && !(__instance is Pawn pawn && MapComponent_TacticalEngineAudit.ProtectedRaiders.Contains(pawn))) return true;
            __result = new DamageWorker.DamageResult(); return false;
        }
    }

    [HarmonyPatch(typeof(RimWorld.WildAnimalSpawner), nameof(RimWorld.WildAnimalSpawner.WildAnimalSpawnerTick))]
    internal static class Patch_RaidRuntimeAudit_Wildlife
    {
        // The installed game's ordinary spawner does not consult noAnimals.
        // Install only in the isolated audit process, equally in both engines.
        private static bool Prepare() => GenCommandLine.TryGetCommandLineArg("hdTacticalEngineAudit", out _)
            || GenCommandLine.TryGetCommandLineArg("hdCqbSavedMapAudit", out _);
        private static bool Prefix() => false;
    }
}
