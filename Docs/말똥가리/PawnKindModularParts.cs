using System.Collections.Generic;
using Verse;

namespace LGModularWeapons
{
    // Attach to a PawnKindDef to control how its modular weapons come configured, in the
    // same spirit as apparelRequired / apparelTags:
    //
    //   <modExtensions>
    //     <li Class="LGModularWeapons.PawnKindModularParts">
    //       <requiredParts>
    //         <li>LGM16_Part_StandardBarrel</li>
    //       </requiredParts>
    //       <allowedPartTags>
    //         <li>Standard</li>
    //       </allowedPartTags>
    //       <chance>0.75</chance>
    //       <perSlotChance>0.5</perSlotChance>
    //     </li>
    //   </modExtensions>
    //
    // A PawnKindDef WITHOUT this extension spawns weapons stock, so nothing changes for
    // pawn kinds you have not opted in.
    // One slot's odds, overriding PawnKindModularParts.perSlotChance.
    public class SlotChance
    {
        public WeaponPartSlotDef slot;
        public float chance = 0.5f;
    }

    public class PawnKindModularParts : DefModExtension
    {
        // Always fitted when compatible, regardless of tags or the roll below. Use this for
        // the parts that define a unit's silhouette - a scout's short barrel, a marksman's
        // scope.
        public List<WeaponPartDef> requiredParts;

        // The pool this pawn kind may draw optional parts from. A part qualifies if it
        // carries at least one of these tags (WeaponPartDef.partTags).
        // EMPTY = no optional parts at all, so a kind can be locked to exactly its
        // requiredParts. Use <allowAnyPart>true</allowAnyPart> for "anything goes".
        public List<string> allowedPartTags;

        public bool allowAnyPart = false;

        // Whether the weapon's own defaultParts may be REPLACED by something from the pool.
        //
        // With this off, generation only fills slots the weapon left empty, so every pawn
        // ends up on the stock barrel/handguard/stock with a few extras bolted on. With it
        // on, a slot already holding a default part can be re-rolled, which is what lets a
        // CQB unit actually turn up with a different handguard rather than the issue one.
        //
        // Parts named in requiredParts are never replaced - they are the point of the kind.
        public bool replaceDefaults = true;

        // Chance this pawn gets any randomisation at all (required parts are still fitted).
        public float chance = 1f;

        // Per optional slot, chance something is fitted into it. Used for every slot that
        // does not appear in slotChances below.
        public float perSlotChance = 0.5f;

        // Per-slot overrides. A marksman should almost always get an optic while barely ever
        // bothering with an under-rail accessory, and one blanket number cannot say that.
        //
        //   <slotChances>
        //     <li><slot>LGM16_Slot_Optic</slot><chance>0.95</chance></li>
        //     <li><slot>LGM16_Slot_RailBottom</slot><chance>0.1</chance></li>
        //     <li><slot>LGM16_Slot_Muzzle</slot><chance>0</chance></li>
        //   </slotChances>
        //
        // 0 means that slot is never rolled. Required slots ignore this entirely - they are
        // always filled.
        public List<SlotChance> slotChances;

        public float ChanceForSlot(WeaponPartSlotDef slot)
        {
            if (!slotChances.NullOrEmpty())
            {
                for (int i = 0; i < slotChances.Count; i++)
                    if (slotChances[i]?.slot == slot) return slotChances[i].chance;
            }
            return perSlotChance;
        }

        public bool PartAllowed(WeaponPartDef part)
        {
            if (allowAnyPart) return true;
            if (allowedPartTags.NullOrEmpty()) return false;

            for (int i = 0; i < allowedPartTags.Count; i++)
                if (part.HasTag(allowedPartTags[i])) return true;

            return false;
        }
    }
}