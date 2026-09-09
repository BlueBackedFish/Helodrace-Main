using Verse;

namespace LGModularWeapons
{
    // A customization slot on a weapon (receiver, barrel, stock, muzzle, optic, gas block...).
    // One Def per slot type. Weapons declare which slots they expose in CompProperties.
    public class WeaponPartSlotDef : Def
    {
        // If true the slot should always hold a part; defaultPart fills it when empty.
        public bool required = false;

        // Fallback part used when a required slot has nothing installed.
        public WeaponPartDef defaultPart;

        // Display order in the customization window (lower = higher up).
        public int uiOrder = 0;

    }
}