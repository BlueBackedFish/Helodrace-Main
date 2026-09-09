using RimWorld;
using Verse;
using Verse.Sound;

namespace LGModularWeapons
{
    // Two sounds for the whole system: one when a part is swapped, one when a modification
    // finishes.
    //
    // Resolved by defName rather than [DefOf] so a missing SoundDef degrades to a vanilla
    // fallback instead of throwing at startup, and cached because these are hit on every
    // click in the customization window.
    [StaticConstructorOnStartup]
    public static class ModularSounds
    {
        private const string InstallDefName = "LGMW_PartInstall";
        private const string CompleteDefName = "LGMW_ModificationComplete";

        private static readonly SoundDef install =
            DefDatabase<SoundDef>.GetNamedSilentFail(InstallDefName) ?? SoundDefOf.Click;

        private static readonly SoundDef complete =
            DefDatabase<SoundDef>.GetNamedSilentFail(CompleteDefName) ?? SoundDefOf.Crunch;

        public static SoundDef Install => install;
        public static SoundDef Complete => complete;

        // Safe wrappers. PlayOneShotOnCamera throws an error for defs whose subsounds are
        // world-positional, and PlayOneShot needs a real map position - so pick the call
        // that matches how each def is actually authored instead of assuming.
        public static void PlayInstallUI()
        {
            PlayUI(install);
        }

        // Completion is a world event: play it at the given thing so it carries position and
        // distance. Falls back to a UI play only if the def supports it.
        public static void PlayCompleteAt(Thing thing)
        {
            if (complete == null) return;

            if (thing != null && thing.Spawned && thing.Map != null)
            {
                complete.PlayOneShot(new TargetInfo(thing.Position, thing.Map));
                return;
            }
            PlayUI(complete);
        }

        private static void PlayUI(SoundDef def)
        {
            if (def == null) return;

            // A MapOnly def has no on-camera subsound; playing it on camera is an error.
            if (def.context == SoundContext.MapOnly || def.HasSubSoundsInWorld)
            {
                Map map = Find.CurrentMap;
                if (map != null)
                    def.PlayOneShot(new TargetInfo(map.Center, map));
                return;
            }

            def.PlayOneShotOnCamera();
        }
    }
}