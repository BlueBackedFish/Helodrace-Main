using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Helodrace
{
    // Vanilla: vanilla raid AI plus BasicRaidAI. Squad: the rewritten squad AI
    // (mod setting). Legacy: the previous tactical engine, kept only as a
    // command-line reference until the squad AI covers its features.
    public enum TacticalEngineKind { Vanilla, Legacy, Squad }
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class LegacyEngineAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class SquadTacticalAttribute : Attribute { }

    // Fixed before patches/components are installed. Never switch a live raid's owner.
    public static class TacticalEngineSelection
    {
        private static TacticalEngineKind? selected;
        // The command-line switch (test harnesses) overrides the mod setting.
        public static TacticalEngineKind Kind => selected ?? (selected =
            GenCommandLine.TryGetCommandLineArg("hdTacticalEngine", out string value) ? Parse(value)
            : RaidSettings.TacticalAI ? TacticalEngineKind.Squad : TacticalEngineKind.Vanilla).Value;
        public static bool NewImplemented => true;
        public static string EffectiveEngine => Kind.ToString().ToLowerInvariant();

        public static TacticalEngineKind Parse(string value)
        {
            switch (value?.ToLowerInvariant())
            {
                case "vanilla": return TacticalEngineKind.Vanilla;
                case "legacy": return TacticalEngineKind.Legacy;
                case null:
                case "squad": return TacticalEngineKind.Squad;
                default: throw new ArgumentException("hdTacticalEngine must be vanilla, legacy or squad.");
            }
        }
        public static bool Install(Type type, TacticalEngineKind engine) =>
            type.IsDefined(typeof(LegacyEngineAttribute), false) ? engine == TacticalEngineKind.Legacy
            : !type.IsDefined(typeof(SquadTacticalAttribute), false) || engine == TacticalEngineKind.Squad;
        internal static void InstallPatches(Harmony harmony, Assembly assembly)
        {
            foreach (Type type in assembly.GetTypes())
                if (type.IsDefined(typeof(HarmonyPatch), false) && Install(type, Kind))
                {
                    // One conflicting patch must not abort the rest of startup
                    // (race caches, rendering and apparel patches come after it).
                    try { harmony.CreateClassProcessor(type).Patch(); }
                    catch (Exception e) { Log.Error("[Helodrace] Failed to apply " + type.FullName + ": " + e); }
                }
            Log.Message("Helodrace tactical engine: " + Kind + " (effective=" + EffectiveEngine + ").");
        }
        internal static void Filter(Map map)
        {
            foreach (MapComponent component in map.components.Where(value => !Install(value.GetType(), Kind)).ToArray())
            {
                component.MapRemoved();
                map.components.Remove(component);
            }
        }
        internal static void Filter(Game game)
        {
            game.components.RemoveAll(component => !Install(component.GetType(), Kind));
        }
    }

    [HarmonyPatch(typeof(Map), "FillComponents")]
    public static class Patch_TacticalEngine_MapComponents
    {
        private static void Postfix(Map __instance) => TacticalEngineSelection.Filter(__instance);
    }
    [HarmonyPatch(typeof(Game), "FillComponents")]
    public static class Patch_TacticalEngine_GameComponents
    {
        private static void Postfix(Game __instance) => TacticalEngineSelection.Filter(__instance);
    }
}
