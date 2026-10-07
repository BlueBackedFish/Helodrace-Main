using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Helodrace
{
    public enum TacticalEngineKind { Vanilla, Legacy, New }

    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class LegacyTacticalAttribute : Attribute { }

    // Fixed before patches/components are installed. Never switch a live raid's owner.
    public static class TacticalEngineSelection
    {
        private static TacticalEngineKind? selected;
        public static TacticalEngineKind Kind => selected ?? (selected = Parse(
            GenCommandLine.TryGetCommandLineArg("hdTacticalEngine", out string value) ? value : null)).Value;
        public static bool NewImplemented => false; // R2 supplies the first execution slice.
        public static string EffectiveEngine => Kind == TacticalEngineKind.New && !NewImplemented
            ? "vanilla-fallback" : Kind.ToString().ToLowerInvariant();

        public static TacticalEngineKind Parse(string value)
        {
            if (value == null) return TacticalEngineKind.Legacy;
            switch (value.ToLowerInvariant())
            {
                case "vanilla": return TacticalEngineKind.Vanilla;
                case "legacy": return TacticalEngineKind.Legacy;
                case "new": return TacticalEngineKind.New;
                default: throw new ArgumentException("hdTacticalEngine must be vanilla, legacy, or new.");
            }
        }
        public static bool IsLegacy(Type type) => type.IsDefined(typeof(LegacyTacticalAttribute), false);
        public static bool Install(Type type, TacticalEngineKind engine) => engine == TacticalEngineKind.Legacy || !IsLegacy(type);
        internal static void InstallPatches(Harmony harmony, Assembly assembly)
        {
            foreach (Type type in assembly.GetTypes())
                if (type.IsDefined(typeof(HarmonyPatch), false) && Install(type, Kind))
                    harmony.CreateClassProcessor(type).Patch();
            Log.Message("Helodrace tactical engine: " + Kind + " (effective=" + EffectiveEngine + ").");
        }
        internal static void Filter(Map map)
        {
            if (Kind == TacticalEngineKind.Legacy) return;
            foreach (MapComponent component in map.components.Where(value => IsLegacy(value.GetType())).ToArray())
            {
                component.MapRemoved();
                map.components.Remove(component);
            }
        }
        internal static void Filter(Game game)
        {
            if (Kind != TacticalEngineKind.Legacy)
                game.components.RemoveAll(component => IsLegacy(component.GetType()));
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
