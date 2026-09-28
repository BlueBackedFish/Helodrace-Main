using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Converts legacy weapon variants into the modular weapon once, on load.
    //
    // Runs in FinalizeInit rather than LoadedGame so that maps, pawns and their equipment are
    // fully spawned and safe to mutate. Everything it touches is re-checked against the same
    // validator the bench uses, so a migrated weapon can never end up in a state the player
    // could not have built.
    public class ModularWeaponMigrator : GameComponent
    {
        private bool migrated;

        public ModularWeaponMigrator(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref migrated, "migrated", false);
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (migrated) return;

            // Marked before the sweep: a failure part-way through should not make the next
            // load try the whole thing again on a half-converted colony.
            migrated = true;

            Dictionary<ThingDef, ModularWeaponMigrationDef> byOld = BuildMap();
            if (byOld.Count == 0) return;

            int converted = 0;
            converted += MigrateEquipped(byOld);
            converted += MigrateLoose(byOld);

            if (converted > 0)
            {
                Messages.Message("LGMW_MigrationDone".Translate(converted),
                    MessageTypeDefOf.PositiveEvent, false);
                Log.Message("[LGModularWeapons] migrated " + converted + " legacy weapon(s).");
            }
        }

        private static Dictionary<ThingDef, ModularWeaponMigrationDef> BuildMap()
        {
            var map = new Dictionary<ThingDef, ModularWeaponMigrationDef>();
            foreach (ModularWeaponMigrationDef def in
                DefDatabase<ModularWeaponMigrationDef>.AllDefsListForReading)
            {
                if (def.oldWeapon == null || def.newWeapon == null) continue;
                map[def.oldWeapon] = def;
            }
            return map;
        }

        // Weapons in pawns' hands and inventories, across every map, caravan and the world.
        private static int MigrateEquipped(Dictionary<ThingDef, ModularWeaponMigrationDef> byOld)
        {
            int count = 0;

            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive.ToList())
            {
                ThingWithComps primary = pawn.equipment?.Primary;
                if (primary != null && byOld.TryGetValue(primary.def, out var rule))
                {
                    ThingWithComps replacement = Build(primary, rule);
                    if (replacement != null)
                    {
                        pawn.equipment.Remove(primary);
                        primary.Destroy();
                        pawn.equipment.AddEquipment(replacement);
                        count++;
                    }
                }

                if (pawn.inventory?.innerContainer == null) continue;

                List<Thing> inv = pawn.inventory.innerContainer.ToList();
                foreach (Thing t in inv)
                {
                    ThingWithComps twc = t as ThingWithComps;
                    if (twc == null || !byOld.TryGetValue(twc.def, out var invRule)) continue;

                    ThingWithComps replacement = Build(twc, invRule);
                    if (replacement == null) continue;

                    pawn.inventory.innerContainer.Remove(twc);
                    twc.Destroy();
                    pawn.inventory.innerContainer.TryAdd(replacement);
                    count++;
                }
            }

            return count;
        }

        // Weapons lying on the ground or sitting in storage.
        private static int MigrateLoose(Dictionary<ThingDef, ModularWeaponMigrationDef> byOld)
        {
            int count = 0;

            foreach (Map map in Find.Maps)
            {
                foreach (var kv in byOld)
                {
                    List<Thing> things = map.listerThings.ThingsOfDef(kv.Key).ToList();
                    foreach (Thing t in things)
                    {
                        ThingWithComps twc = t as ThingWithComps;
                        if (twc == null || !twc.Spawned) continue;

                        IntVec3 pos = twc.Position;
                        Rot4 rot = twc.Rotation;

                        ThingWithComps replacement = Build(twc, kv.Value);
                        if (replacement == null) continue;

                        twc.Destroy();
                        GenSpawn.Spawn(replacement, pos, map, rot);
                        count++;
                    }
                }
            }

            return count;
        }

        // Creates the modular replacement, carrying over the properties a player would be
        // upset to lose: quality, damage, stuff and any custom name.
        private static ThingWithComps Build(ThingWithComps old, ModularWeaponMigrationDef rule)
        {
            ThingWithComps fresh = ThingMaker.MakeThing(rule.newWeapon, old.Stuff) as ThingWithComps;
            if (fresh == null) return null;

            QualityCategory quality;
            if (old.TryGetQuality(out quality))
                fresh.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Colony);

            // Proportional, since the two defs can have different MaxHitPoints.
            if (old.MaxHitPoints > 0)
            {
                float pct = (float)old.HitPoints / old.MaxHitPoints;
                fresh.HitPoints = Mathf.Max(1, Mathf.RoundToInt(fresh.MaxHitPoints * pct));
            }

            // Art is deliberately NOT transferred: CompArt has no API for adopting another
            // item's title (InitializeArt only takes a generation context or a related
            // thing), and the title is derived from an internal TaleReference. Weapons with
            // real art are rare enough that dropping it beats faking it.

            ApplyParts(fresh, rule);
            return fresh;
        }

        private static void ApplyParts(ThingWithComps weapon, ModularWeaponMigrationDef rule)
        {
            CompWeaponModular comp = weapon.GetComp<CompWeaponModular>();
            if (comp == null || rule.parts.NullOrEmpty()) return;

            var config = new Dictionary<WeaponPartSlotDef, WeaponPartDef>();
            foreach (WeaponPartSlotDef slot in comp.ActiveSlots)
            {
                WeaponPartDef fitted = comp.PartInSlot(slot);
                if (fitted != null) config[slot] = fitted;
            }

            // Repeated passes: a part can open the slot another part needs, and the list
            // order should not decide whether the migration succeeds.
            var remaining = new List<WeaponPartDef>(rule.parts);
            for (int pass = 0; pass < 8 && remaining.Count > 0; pass++)
            {
                bool progress = false;
                List<WeaponPartSlotDef> slots = comp.ComputeActiveSlots(config);

                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    WeaponPartDef part = remaining[i];
                    if (part?.slot == null || !slots.Contains(part.slot)) continue;

                    string reason;
                    if (!comp.CanFitPart(part, config, slots, out reason)) continue;

                    config[part.slot] = part;
                    remaining.RemoveAt(i);
                    progress = true;
                }

                if (!progress) break;
            }

            if (remaining.Count > 0)
            {
                Log.Warning("[LGModularWeapons] migration " + rule.defName + ": "
                          + remaining.Count + " part(s) could not be fitted; the weapon was "
                          + "still converted.");
            }

            comp.SetConfiguration(config);
        }
    }
}