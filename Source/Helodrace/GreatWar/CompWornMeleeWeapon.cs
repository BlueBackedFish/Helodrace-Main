using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    public class CompProperties_WornMeleeWeapon : CompProperties
    {
        public CompProperties_WornMeleeWeapon() => compClass = typeof(CompWornMeleeWeapon);
    }

    // 1.7 already includes CompEquippable apparel in its melee verb list. Supply
    // the wearer as caster and leave DPS categories and selection to the game.
    public class CompWornMeleeWeapon : CompEquippable, IVerbOwner
    {
        Thing IVerbOwner.ConstantCaster => (parent as Apparel)?.Wearer;

        internal void BindWearer()
        {
            Pawn wearer = (parent as Apparel)?.Wearer;
            foreach (Verb verb in AllVerbs) verb.caster = wearer;
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            BindWearer();
            pawn?.meleeVerbs?.Notify_PawnDespawned();
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            foreach (Verb verb in AllVerbs) verb.caster = null;
            pawn?.meleeVerbs?.Notify_PawnDespawned();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) BindWearer();
        }
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.GetUpdatedAvailableVerbsList))]
    public static class Patch_PawnMeleeVerbs_WornMeleeWeapons
    {
        // Loading or transferring apparel can precede its wear notification.
        // Bind before vanilla checks usability and calculates selection weights.
        public static void Prefix(Pawn_MeleeVerbs __instance, bool terrainTools)
        {
            if (terrainTools || __instance?.Pawn?.apparel == null) return;
            foreach (Apparel apparel in __instance.Pawn.apparel.WornApparel)
                apparel.TryGetComp<CompWornMeleeWeapon>()?.BindWearer();
        }
    }
}