using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class CompProperties_ModularWeaponPartsBox : CompProperties_Facility
    {
        public CompProperties_ModularWeaponPartsBox()
        {
            compClass = typeof(CompModularWeaponPartsBox);
        }
    }

    /// <summary>
    /// Compact storage for weapon parts which have no useful identity of their own.
    /// Only a ThingDef/count pair is saved, so a box containing hundreds of small parts
    /// does not add hundreds of Things to the map or save file.
    /// </summary>
    public sealed class CompModularWeaponPartsBox : CompFacility
    {
        private Dictionary<ThingDef, int> virtualParts = new Dictionary<ThingDef, int>();
        private List<ThingDef> workingKeys;
        private List<int> workingValues;

        public int TotalCount => virtualParts?.Values.Sum() ?? 0;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(
                ref virtualParts,
                "modularWeaponVirtualParts",
                LookMode.Def,
                LookMode.Value,
                ref workingKeys,
                ref workingValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                virtualParts = virtualParts ?? new Dictionary<ThingDef, int>();
                virtualParts = virtualParts
                    .Where(pair => pair.Key != null && pair.Value > 0
                        && StorageModeFor(pair.Key) == ModularWeaponPartStorageMode.Virtual)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            }
        }

        public int CountOf(ThingDef partDef)
        {
            if (partDef == null || virtualParts == null) return 0;
            return virtualParts.TryGetValue(partDef, out int count) ? count : 0;
        }

        public bool TryAdd(ThingDef partDef, int count = 1)
        {
            if (partDef == null || count <= 0
                || StorageModeFor(partDef) != ModularWeaponPartStorageMode.Virtual)
                return false;

            virtualParts = virtualParts ?? new Dictionary<ThingDef, int>();
            virtualParts.TryGetValue(partDef, out int current);
            virtualParts[partDef] = current + count;
            return true;
        }

        public bool TryTake(ThingDef partDef, int count = 1)
        {
            if (partDef == null || count <= 0 || CountOf(partDef) < count)
                return false;

            int remaining = virtualParts[partDef] - count;
            if (remaining == 0) virtualParts.Remove(partDef);
            else virtualParts[partDef] = remaining;
            return true;
        }

        public override string CompInspectStringExtra()
        {
            if (virtualParts.NullOrEmpty())
                return "HD_ModularPartsBox_Empty".Translate();

            IEnumerable<string> lines = virtualParts
                .Where(pair => pair.Key != null && pair.Value > 0)
                .OrderBy(pair => pair.Key.label)
                .Take(6)
                .Select(pair => pair.Key.LabelCap + " ×" + pair.Value);
            string result = "HD_ModularPartsBox_Total".Translate(TotalCount) + "\n"
                + string.Join("\n", lines);
            if (virtualParts.Count > 6)
                result += "\n" + "HD_ModularPartsBox_More".Translate(virtualParts.Count - 6);
            return result;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;

            yield return new Command_Action
            {
                defaultLabel = "HD_ModularPartsBox_Absorb".Translate(),
                defaultDesc = "HD_ModularPartsBox_AbsorbDesc".Translate(),
                icon = parent.def.uiIcon,
                action = AbsorbNearbyLooseVirtualParts
            };

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: add modular parts",
                    defaultDesc = "Add ten of every virtual modular weapon part to this box.",
                    icon = TexCommand.DesirePower,
                    action = delegate
                    {
                        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                        {
                            if (StorageModeFor(def) == ModularWeaponPartStorageMode.Virtual)
                                TryAdd(def, 10);
                        }
                    }
                };
            }
        }

        private void AbsorbNearbyLooseVirtualParts()
        {
            Map map = parent.Map;
            if (map == null) return;
            List<Thing> candidates = map.listerThings.AllThings
                .Where(thing => thing?.Spawned == true
                    && thing.Position.InHorDistOf(parent.Position, 8f)
                    && StorageModeFor(thing.def) == ModularWeaponPartStorageMode.Virtual)
                .ToList();
            int absorbed = 0;
            ModularWeaponWorkshopSession session = new ModularWeaponWorkshopSession(
                parent,
                new List<CompModularWeaponPartsBox> { this });
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing thing = candidates[i];
                int count = Math.Max(1, thing.stackCount);
                if (thing.stackCount > 1)
                {
                    if (!TryAdd(thing.def, count)) continue;
                    absorbed += count;
                    thing.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    session.ReturnAssembly(thing);
                    absorbed++;
                }
            }

            Messages.Message(
                "HD_ModularPartsBox_Absorbed".Translate(absorbed),
                parent,
                absorbed > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent,
                false);
        }

        internal static ModularWeaponPartStorageMode StorageModeFor(ThingDef def)
        {
            CompProperties_ModularWeaponNode props =
                def?.GetCompProperties<CompProperties_ModularWeaponNode>();
            return props?.storageMode ?? ModularWeaponPartStorageMode.Internal;
        }
    }

    public sealed class CompProperties_ModularWeaponWorkbench
        : CompProperties_AffectedByFacilities
    {
        public CompProperties_ModularWeaponWorkbench()
        {
            compClass = typeof(CompModularWeaponWorkbench);
        }
    }

    public sealed class CompModularWeaponWorkbench : CompAffectedByFacilities
    {
        public List<CompModularWeaponPartsBox> LinkedPartBoxes =>
            LinkedFacilitiesListForReading
                .Select(thing => thing?.TryGetComp<CompModularWeaponPartsBox>())
                .Where(comp => comp != null)
                .ToList();

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;

            Command_Action command = new Command_Action
            {
                defaultLabel = "HD_ModularWeaponBench_Command".Translate(),
                defaultDesc = "HD_ModularWeaponBench_CommandDesc".Translate(),
                icon = parent.def.uiIcon,
                action = OpenWeaponMenu
            };
            if (LinkedPartBoxes.Count == 0)
                command.Disable("HD_ModularWeaponBench_NoPartsBox".Translate());
            else if (AvailableWeapons().Count == 0)
                command.Disable("HD_ModularWeaponBench_NoWeapon".Translate());
            yield return command;
        }

        private void OpenWeaponMenu()
        {
            List<CompModularWeaponNode> weapons = AvailableWeapons();
            if (weapons.Count == 0) return;
            Find.WindowStack.Add(new FloatMenu(weapons.Select(comp =>
                new FloatMenuOption(
                    WeaponLabel(comp),
                    () => Find.WindowStack.Add(new Dialog_ModularWeapon(
                        comp,
                        new ModularWeaponWorkshopSession(parent, LinkedPartBoxes))))).ToList()));
        }

        private List<CompModularWeaponNode> AvailableWeapons()
        {
            Map map = parent.Map;
            if (map == null) return new List<CompModularWeaponNode>();
            HashSet<CompModularWeaponNode> result = new HashSet<CompModularWeaponNode>();

            foreach (Thing thing in map.listerThings.AllThings)
            {
                CompModularWeaponNode comp = (thing as ThingWithComps)
                    ?.TryGetComp<CompModularWeaponNode>();
                if (comp?.Props.isAssemblyRoot == true && comp.IsTopLevelAssembly)
                    result.Add(comp);
            }

            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                CompModularWeaponNode comp = pawn.equipment?.Primary
                    ?.TryGetComp<CompModularWeaponNode>();
                if (comp?.Props.isAssemblyRoot == true) result.Add(comp);
            }

            return result.OrderBy(comp => comp.parent.LabelCap.ToString()).ToList();
        }

        private static string WeaponLabel(CompModularWeaponNode comp)
        {
            Pawn wielder = (comp?.parent?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            return wielder == null
                ? comp?.parent?.LabelCap.ToString() ?? "-"
                : "HD_ModularWeaponBench_WieldedBy".Translate(
                    comp.parent.LabelCap,
                    wielder.LabelShortCap).ToString();
        }
    }

    public sealed class ModularWeaponWorkshopSession
    {
        private readonly Thing bench;
        private readonly List<CompModularWeaponPartsBox> boxes;

        public ModularWeaponWorkshopSession(
            Thing bench,
            List<CompModularWeaponPartsBox> boxes)
        {
            this.bench = bench;
            this.boxes = boxes ?? new List<CompModularWeaponPartsBox>();
        }

        public int AvailableCount(ThingDef def)
        {
            ModularWeaponPartStorageMode mode = CompModularWeaponPartsBox.StorageModeFor(def);
            if (mode == ModularWeaponPartStorageMode.Internal) return int.MaxValue;
            if (mode == ModularWeaponPartStorageMode.Virtual)
                return boxes.Sum(box => box.CountOf(def));
            return AvailableIndependentParts(def).Count;
        }

        public bool TryTakePart(ThingDef def, out Thing part)
        {
            part = null;
            ModularWeaponPartStorageMode mode = CompModularWeaponPartsBox.StorageModeFor(def);
            if (mode == ModularWeaponPartStorageMode.IndependentThing)
            {
                part = AvailableIndependentParts(def).FirstOrDefault();
                if (part == null) return false;
                if (part.Spawned) part.DeSpawn();
                else if (part.ParentHolder is ThingOwner owner)
                    part = owner.Take(part, 1);
                return part != null;
            }

            List<Tuple<CompModularWeaponPartsBox, ThingDef>> debited =
                new List<Tuple<CompModularWeaponPartsBox, ThingDef>>();
            if (mode == ModularWeaponPartStorageMode.Virtual
                && !TryDebitVirtualPart(def, debited))
                return false;

            part = ThingMaker.MakeThing(def);
            if (part != null && TryDebitDefaultSubtree(part, debited)) return true;

            for (int i = 0; i < debited.Count; i++)
                debited[i].Item1.TryAdd(debited[i].Item2);
            DestroyAssemblyWithoutStorage(part);
            part = null;
            return false;
        }

        public void ReturnAssembly(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;
            ModularWeaponPartStorageMode mode =
                CompModularWeaponPartsBox.StorageModeFor(thing.def);
            if (mode == ModularWeaponPartStorageMode.IndependentThing)
            {
                PlaceNearBench(thing);
                return;
            }

            CompModularWeaponNode comp = thing.TryGetComp<CompModularWeaponNode>();
            while (comp != null && comp.ChildCount > 0)
                ReturnAssembly(comp.DetachChildAt(comp.ChildCount - 1));

            if (mode == ModularWeaponPartStorageMode.Virtual && boxes.Count > 0)
                boxes[0].TryAdd(thing.def);
            thing.Destroy(DestroyMode.Vanish);
        }

        private List<Thing> AvailableIndependentParts(ThingDef def)
        {
            Map map = bench?.Map;
            if (map == null || def == null) return new List<Thing>();
            List<Thing> result = map.listerThings.ThingsOfDef(def)
                .Where(thing => thing?.Spawned == true && !thing.Destroyed
                    && !thing.IsForbidden(Faction.OfPlayer))
                .ToList();
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                ThingOwner<Thing> inventory = pawn.inventory?.innerContainer;
                if (inventory == null) continue;
                for (int i = 0; i < inventory.Count; i++)
                    if (inventory[i]?.def == def) result.Add(inventory[i]);
            }
            return result;
        }

        private bool TryDebitDefaultSubtree(
            Thing thing,
            List<Tuple<CompModularWeaponPartsBox, ThingDef>> debited)
        {
            CompModularWeaponNode comp = thing?.TryGetComp<CompModularWeaponNode>();
            if (comp == null) return true;
            for (int i = 0; i < comp.ChildCount; i++)
            {
                Thing child = comp.ChildAt(i);
                ModularWeaponPartStorageMode childMode =
                    CompModularWeaponPartsBox.StorageModeFor(child?.def);
                if (childMode == ModularWeaponPartStorageMode.IndependentThing)
                    return false;
                if (childMode == ModularWeaponPartStorageMode.Virtual
                    && !TryDebitVirtualPart(child.def, debited))
                    return false;
                if (!TryDebitDefaultSubtree(child, debited)) return false;
            }
            return true;
        }

        private bool TryDebitVirtualPart(
            ThingDef def,
            List<Tuple<CompModularWeaponPartsBox, ThingDef>> debited)
        {
            CompModularWeaponPartsBox source = boxes.FirstOrDefault(box => box.CountOf(def) > 0);
            if (source == null || !source.TryTake(def)) return false;
            debited.Add(Tuple.Create(source, def));
            return true;
        }

        private static void DestroyAssemblyWithoutStorage(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;
            CompModularWeaponNode comp = thing.TryGetComp<CompModularWeaponNode>();
            while (comp != null && comp.ChildCount > 0)
                DestroyAssemblyWithoutStorage(comp.DetachChildAt(comp.ChildCount - 1));
            thing.Destroy(DestroyMode.Vanish);
        }

        private void PlaceNearBench(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;
            if (thing.Spawned) thing.DeSpawn();
            Map map = bench?.Map;
            if (map != null)
                GenPlace.TryPlaceThing(thing, bench.Position, map, ThingPlaceMode.Near);
            else
                thing.Destroy(DestroyMode.Vanish);
        }
    }
}
