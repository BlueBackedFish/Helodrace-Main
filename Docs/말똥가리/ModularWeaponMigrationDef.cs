using System.Collections.Generic;
using Verse;

namespace LGModularWeapons
{
    // Maps a legacy weapon variant onto the modular weapon plus the parts that reproduce it.
    //
    // Deleting the old ThingDefs outright is not an option: every saved Thing referencing
    // them fails to resolve on load, so the item is dropped with an error and any pawn
    // carrying one ends up empty-handed. The old defs stay in the mod (hidden from crafting
    // and trading) purely so old saves can still load them, and this def says what each one
    // should become.
    //
    //   <LGModularWeapons.ModularWeaponMigrationDef>
    //     <defName>LGM16_Migrate_Grenadier</defName>
    //     <oldWeapon>LG_M16A1_GL</oldWeapon>
    //     <newWeapon>LG_M16A1A</newWeapon>
    //     <parts>
    //       <li>LGM16_Part_StandardBarrel</li>
    //       <li>LGM16_Part_TacticalHandGuard</li>
    //       <li>LGM16_Part_UnderbarrelLauncher</li>
    //     </parts>
    //   </LGModularWeapons.ModularWeaponMigrationDef>
    public class ModularWeaponMigrationDef : Def
    {
        public ThingDef oldWeapon;
        public ThingDef newWeapon;

        // Fitted in list order, then validated - anything that cannot legally fit is skipped
        // rather than forced, so a bad mapping degrades instead of corrupting the weapon.
        public List<WeaponPartDef> parts;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;

            if (oldWeapon == null) yield return "oldWeapon is null";
            if (newWeapon == null) yield return "newWeapon is null";
            else if (newWeapon.GetCompProperties<CompProperties_WeaponModular>() == null)
                yield return "newWeapon " + newWeapon.defName + " has no CompProperties_WeaponModular";
        }
    }
}
