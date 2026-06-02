using HarmonyLib;
using Verse;
using System.Reflection;

namespace Helodrace
{
    [StaticConstructorOnStartup]
    public static class HelodraceMod
    {
        static HelodraceMod()
        {
            var harmony = new Harmony("YourName.Helodrace");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("Helodrace initialized.");
        }
    }

    public class HelodraceBase : Mod
    {
        public HelodraceBase(ModContentPack content) : base(content)
        {
            Log.Message("Helodrace Mod loaded.");
        }
    }
}
