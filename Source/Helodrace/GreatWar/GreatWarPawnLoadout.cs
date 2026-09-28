using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    // Ammunition belongs to the PawnKind loadout, independently of raid strategy.
    public class GreatWarAmmoLoadoutExtension : DefModExtension
    {
        public ThingDef ammoDef;
        public int ammoCount;

        public override IEnumerable<string> ConfigErrors()
        {
            if (ammoDef == null)
                yield return "Great War ammunition loadout requires an ammoDef.";
            if (ammoCount <= 0)
                yield return "Great War ammunition loadout requires a positive ammoCount.";
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), "GenerateGearFor",
        new[] { typeof(Pawn), typeof(PawnGenerationRequest) })]
    public static class Patch_PawnGenerator_GreatWarAmmoLoadout
    {
        public static void Postfix(Pawn pawn)
        {
            GreatWarAmmoLoadoutExtension loadout = pawn?.kindDef
                ?.GetModExtension<GreatWarAmmoLoadoutExtension>();
            if (loadout?.ammoDef == null || loadout.ammoCount <= 0
                || pawn.inventory == null) return;

            int missing = loadout.ammoCount - pawn.inventory.Count(loadout.ammoDef);
            if (missing > 0)
            {
                Thing ammo = ThingMaker.MakeThing(loadout.ammoDef);
                ammo.stackCount = missing;
                if (!pawn.inventory.innerContainer.TryAdd(ammo))
                    ammo.Destroy(DestroyMode.Vanish);
            }

            if (pawn.apparel == null) return;
            foreach (Apparel apparel in pawn.apparel.WornApparel)
                apparel.TryGetComp<CompM6RocketBag>()
                    ?.SetDesiredCount(loadout.ammoDef, loadout.ammoCount);
        }
    }
}
