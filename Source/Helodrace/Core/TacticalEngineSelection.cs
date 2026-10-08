using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Helodrace
{
    public enum TacticalEngineKind { Vanilla, New }
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class NewTacticalAttribute : Attribute { }

    // Fixed before patches/components are installed. Never switch a live raid's owner.
    public static class TacticalEngineSelection
    {
        private static TacticalEngineKind? selected;
        public static TacticalEngineKind Kind => selected ?? (selected = Parse(
            GenCommandLine.TryGetCommandLineArg("hdTacticalEngine", out string value) ? value : null)).Value;
        public static bool NewImplemented => true;
        public static string EffectiveEngine => Kind.ToString().ToLowerInvariant();

        public static TacticalEngineKind Parse(string value)
        {
            if (value == null) return TacticalEngineKind.New;
            switch (value.ToLowerInvariant())
            {
                case "vanilla": return TacticalEngineKind.Vanilla;
                case "new": return TacticalEngineKind.New;
                default: throw new ArgumentException("hdTacticalEngine must be vanilla or new.");
            }
        }
        public static bool Install(Type type, TacticalEngineKind engine) =>
            !type.IsDefined(typeof(NewTacticalAttribute), false) || engine == TacticalEngineKind.New;
        internal static void InstallPatches(Harmony harmony, Assembly assembly)
        {
            foreach (Type type in assembly.GetTypes())
                if (type.IsDefined(typeof(HarmonyPatch), false) && Install(type, Kind))
                    harmony.CreateClassProcessor(type).Patch();
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
