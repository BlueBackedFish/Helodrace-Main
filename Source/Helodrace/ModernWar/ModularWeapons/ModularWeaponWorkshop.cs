using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    public sealed class CompProperties_ModularWeaponPartsBox : CompProperties
    {
        public CompProperties_ModularWeaponPartsBox()
        {
            compClass = typeof(CompModularWeaponPartsBox);
        }
    }

    /// <summary>
    /// Small parts are stored as ThingDef/count pairs; independent parts retain
    /// their individual Thing instances inside the box.
    /// </summary>
    public sealed class CompModularWeaponPartsBox : ThingComp, IThingHolder
    {
        private Dictionary<ThingDef, int> virtualParts = new Dictionary<ThingDef, int>();
        private ThingOwner<Thing> independentParts;
        private List<ThingDef> workingKeys;
        private List<int> workingValues;

        public int TotalCount => (virtualParts?.Values.Sum() ?? 0)
            + (independentParts?.Sum(thing => thing.stackCount) ?? 0);

        public ThingOwner GetDirectlyHeldThings()
        {
            EnsureContainer();
            return independentParts;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            EnsureContainer();
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, independentParts);
        }

        private void EnsureContainer()
        {
            if (independentParts == null)
                independentParts = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            EnsureContainer();
            independentParts.ExposeData();
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
            if (partDef == null) return 0;
            if (StorageModeFor(partDef) == ModularWeaponPartStorageMode.IndependentThing)
            {
                EnsureContainer();
                return independentParts.Where(thing => thing.def == partDef)
                    .Sum(thing => thing.stackCount);
            }
            if (virtualParts == null) return 0;
            return virtualParts.TryGetValue(partDef, out int count) ? count : 0;
        }

        public bool TryAddIndependent(Thing part)
        {
            if (part == null || part.Destroyed
                || StorageModeFor(part.def) != ModularWeaponPartStorageMode.IndependentThing
                || part.TryGetComp<CompModularWeaponNode>()?.Props.isAssemblyRoot == true)
                return false;
            EnsureContainer();
            Map map = part.Map;
            IntVec3 position = part.Position;
            if (part.Spawned) part.DeSpawn();
            if (independentParts.TryAdd(part, false)) return true;
            if (map != null)
                GenPlace.TryPlaceThing(part, position, map, ThingPlaceMode.Near);
            return false;
        }

        public Thing TryTakeIndependent(ThingDef partDef)
        {
            EnsureContainer();
            Thing part = independentParts.FirstOrDefault(thing => thing.def == partDef);
            return part == null ? null : independentParts.Take(part, 1);
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
            EnsureContainer();
            if (TotalCount == 0)
                return "HD_ModularPartsBox_Empty".Translate();

            IEnumerable<string> lines = virtualParts
                .Where(pair => pair.Key != null && pair.Value > 0)
                .Concat(independentParts.GroupBy(thing => thing.def)
                    .Select(group => new KeyValuePair<ThingDef, int>(group.Key,
                        group.Sum(thing => thing.stackCount))))
                .OrderBy(pair => pair.Key.label)
                .Take(6)
                .Select(pair => pair.Key.LabelCap + " ×" + pair.Value);
            string result = "HD_ModularPartsBox_Total".Translate(TotalCount) + "\n"
                + string.Join("\n", lines);
            int distinctCount = virtualParts.Count
                + independentParts.Select(thing => thing.def).Distinct().Count();
            if (distinctCount > 6)
                result += "\n" + "HD_ModularPartsBox_More".Translate(distinctCount - 6);
            return result;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;

            if (parent.Spawned)
            {
                yield return new Command_Action
                {
                    defaultLabel = "HD_ModularPartsBox_InsertAtBench".Translate(),
                    defaultDesc = "HD_ModularPartsBox_InsertAtBenchDesc".Translate(),
                    icon = parent.def.uiIcon,
                    action = QueueInstallJob
                };
            }

            yield return new Command_Action
            {
                defaultLabel = "HD_ModularPartsBox_Absorb".Translate(),
                defaultDesc = "HD_ModularPartsBox_AbsorbDesc".Translate(),
                icon = parent.def.uiIcon,
                action = () => AbsorbNearbyParts(parent)
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

        private void QueueInstallJob()
        {
            Map map = parent.Map;
            if (map == null || !parent.Spawned) return;

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(
                "HD_InsertModularWeaponPartsBox");
            if (jobDef == null)
            {
                Log.ErrorOnce("Helodrace: parts-box insertion JobDef is missing.", 73852104);
                return;
            }

            ThingDef benchDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_GunSmithTable");
            Pawn chosenPawn = null;
            Thing chosenBench = null;
            float bestDistance = float.MaxValue;
            if (benchDef != null)
            {
                foreach (Thing bench in map.listerThings.ThingsOfDef(benchDef))
                {
                    if (bench?.Spawned != true || bench.Faction != Faction.OfPlayer
                        || bench.TryGetComp<CompModularWeaponWorkbench>() == null
                        || bench.TryGetComp<CompModularWeaponWorkbench>().InstalledPartsBox != null)
                        continue;

                    foreach (Pawn worker in map.mapPawns.FreeColonistsSpawned)
                    {
                        if (worker.Drafted || worker.Downed
                            || worker.WorkTagIsDisabled(WorkTags.Hauling)
                            || !worker.health.capacities.CapableOf(PawnCapacityDefOf.Moving)
                            || !worker.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                            || parent.IsForbidden(worker) || bench.IsForbidden(worker)
                            || !worker.CanReserveAndReach(parent,
                                PathEndMode.ClosestTouch, Danger.Some)
                            || !worker.CanReserveAndReach(bench,
                                PathEndMode.InteractionCell, Danger.Some))
                            continue;

                        float distance = worker.Position.DistanceToSquared(parent.Position)
                            + parent.Position.DistanceToSquared(bench.Position);
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        chosenPawn = worker;
                        chosenBench = bench;
                    }
                }
            }

            if (chosenPawn == null)
            {
                Messages.Message("HD_ModularPartsBox_NoInstallJob".Translate(),
                    parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            Job job = JobMaker.MakeJob(jobDef, parent, chosenBench);
            job.count = 1;
            if (!chosenPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                Messages.Message("HD_ModularPartsBox_NoInstallJob".Translate(),
                    parent, MessageTypeDefOf.RejectInput, false);
        }

        internal void AbsorbNearbyParts(Thing location)
        {
            Map map = location?.Map;
            if (map == null) return;
            List<Thing> candidates = map.listerThings.AllThings
                .Where(thing => thing?.Spawned == true
                    && thing.Position.InHorDistOf(location.Position, 8f)
                    && (StorageModeFor(thing.def) == ModularWeaponPartStorageMode.Virtual
                        || StorageModeFor(thing.def) == ModularWeaponPartStorageMode.IndependentThing))
                .ToList();
            int absorbed = 0;
            ModularWeaponWorkshopSession session = new ModularWeaponWorkshopSession(
                parent,
                new List<CompModularWeaponPartsBox> { this });
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing thing = candidates[i];
                if (StorageModeFor(thing.def) == ModularWeaponPartStorageMode.IndependentThing)
                {
                    if (TryAddIndependent(thing)) absorbed += thing.stackCount;
                    continue;
                }
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
                location,
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

    public sealed class CompProperties_ModularWeaponWorkbench : CompProperties
    {
        public CompProperties_ModularWeaponWorkbench()
        {
            compClass = typeof(CompModularWeaponWorkbench);
        }
    }

    public sealed class CompModularWeaponWorkbench : ThingComp, IThingHolder
    {
        private ThingOwner<Thing> partsBoxes;

        public CompModularWeaponPartsBox InstalledPartsBox
        {
            get
            {
                EnsureContainer();
                return partsBoxes.Count > 0
                    ? partsBoxes[0].TryGetComp<CompModularWeaponPartsBox>()
                    : null;
            }
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            EnsureContainer();
            return partsBoxes;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            EnsureContainer();
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, partsBoxes);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            EnsureContainer();
            partsBoxes.ExposeData();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            EnsureContainer();
            if (previousMap != null && partsBoxes.Count > 0)
            {
                Thing box = partsBoxes.Take(partsBoxes[0], 1);
                if (box != null)
                    GenPlace.TryPlaceThing(box, parent.Position, previousMap, ThingPlaceMode.Near);
            }
            base.PostDestroy(mode, previousMap);
        }

        private void EnsureContainer()
        {
            if (partsBoxes == null)
                partsBoxes = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

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
            if (InstalledPartsBox == null)
                command.Disable("HD_ModularWeaponBench_NoPartsBox".Translate());
            else if (AvailableWeaponOwners().Count == 0)
                command.Disable("HD_ModularWeaponBench_NoWeapon".Translate());
            yield return command;

            if (InstalledPartsBox != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "HD_ModularWeaponBench_RemoveBox".Translate(),
                    icon = InstalledPartsBox.parent.def.uiIcon,
                    action = RemovePartsBox
                };
                yield return new Command_Action
                {
                    defaultLabel = "HD_ModularPartsBox_Absorb".Translate(),
                    defaultDesc = "HD_ModularPartsBox_AbsorbDesc".Translate(),
                    icon = InstalledPartsBox.parent.def.uiIcon,
                    action = () => InstalledPartsBox?.AbsorbNearbyParts(parent)
                };
            }
        }

        public bool TryInstallPartsBox(Thing box)
        {
            EnsureContainer();
            if (parent.Map == null || InstalledPartsBox != null
                || box == null || box.Destroyed || box.Spawned
                || box.TryGetComp<CompModularWeaponPartsBox>() == null
                || box.ParentHolder != null) return false;
            return partsBoxes.TryAdd(box, false);
        }

        private void RemovePartsBox()
        {
            EnsureContainer();
            if (partsBoxes.Count == 0 || parent.Map == null) return;
            Thing box = partsBoxes.Take(partsBoxes[0], 1);
            if (box != null)
                GenPlace.TryPlaceThing(box, parent.Position, parent.Map, ThingPlaceMode.Near);
        }

        private void OpenWeaponMenu()
        {
            Find.WindowStack.Add(new Dialog_ModularWeaponSelection(this));
        }

        internal Thing BenchThing => parent;

        internal bool QueueModificationJob(Pawn owner)
        {
            if (owner?.Map != parent.Map || InstalledPartsBox == null) return false;
            Thing weapon = owner.equipment?.Primary;
            CompModularWeaponNode comp = weapon?.TryGetComp<CompModularWeaponNode>();
            if (comp?.Props.isAssemblyRoot != true
                || !comp.Props.allowPlayerConfiguration || !comp.IsTopLevelAssembly
                || !owner.CanReserveAndReach(parent,
                    PathEndMode.InteractionCell, Danger.Some))
                return false;

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(
                "HD_ModifyModularWeaponAtBench");
            if (jobDef == null) return false;
            Job job = JobMaker.MakeJob(jobDef, parent, weapon);
            return owner.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        internal List<Pawn> AvailableWeaponOwners()
        {
            Map map = parent.Map;
            if (map == null) return new List<Pawn>();
            return map.mapPawns.FreeColonistsSpawned
                .Where(pawn => !pawn.Drafted && !pawn.Downed
                    && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)
                    && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                    && !parent.IsForbidden(pawn)
                    && pawn.CanReserveAndReach(parent,
                        PathEndMode.InteractionCell, Danger.Some))
                .Where(pawn =>
                {
                    CompModularWeaponNode comp = pawn.equipment?.Primary
                        ?.TryGetComp<CompModularWeaponNode>();
                    return comp?.Props.isAssemblyRoot == true
                        && comp.Props.allowPlayerConfiguration
                        && comp.IsTopLevelAssembly;
                })
                .OrderBy(pawn => pawn.LabelShortCap.ToString())
                .ToList();
        }
    }

    public sealed class ModularWeaponWorkshopSession
    {
        private readonly Thing bench;
        private readonly List<CompModularWeaponPartsBox> boxes;
        private readonly Pawn worker;

        public ModularWeaponWorkshopSession(
            Thing bench,
            List<CompModularWeaponPartsBox> boxes,
            Pawn worker = null)
        {
            this.bench = bench;
            this.boxes = boxes ?? new List<CompModularWeaponPartsBox>();
            this.worker = worker;
        }

        public bool IsValidFor(CompModularWeaponNode root)
        {
            if (worker == null) return true;
            return boxes.Count > 0 && boxes[0] != null
                && bench?.Spawned == true && worker.Spawned
                && worker.Map == bench.Map
                && worker.equipment?.Primary == root?.parent
                && worker.Position.InHorDistOf(bench.Position, 2.5f)
                && bench.TryGetComp<CompModularWeaponWorkbench>()
                    ?.InstalledPartsBox == boxes.FirstOrDefault();
        }

        public static bool CanInstantCraft(ThingDef def)
        {
            return def?.GetCompProperties<CompProperties_ModularWeaponNode>()
                ?.canInstantCraft == true && ResearchUnlocked(def);
        }

        public static bool ResearchUnlocked(ThingDef def)
        {
            return def?.researchPrerequisites.NullOrEmpty() != false
                || def.researchPrerequisites.All(project => project?.IsFinished == true);
        }

        public static ResearchProjectDef MissingResearch(ThingDef def)
        {
            return def?.researchPrerequisites?.FirstOrDefault(
                project => project != null && !project.IsFinished);
        }

        public int AvailableCount(ThingDef def)
        {
            ModularWeaponPartStorageMode mode = CompModularWeaponPartsBox.StorageModeFor(def);
            if (mode == ModularWeaponPartStorageMode.Internal || CanInstantCraft(def))
                return int.MaxValue;
            return boxes.Where(box => box != null).Sum(box => box.CountOf(def));
        }

        public bool TryTakePart(ThingDef def, out Thing part)
        {
            part = null;
            bool crafted = false;
            ModularWeaponPartStorageMode mode = CompModularWeaponPartsBox.StorageModeFor(def);
            CompModularWeaponPartsBox box = boxes.FirstOrDefault(
                candidate => candidate != null);
            if (box == null) return false;
            if (mode == ModularWeaponPartStorageMode.IndependentThing)
            {
                part = boxes.FirstOrDefault(candidate => candidate?.CountOf(def) > 0)
                    ?.TryTakeIndependent(def);
                if (part != null) return true;
                if (!CanInstantCraft(def)) return false;
                Thing made = ThingMaker.MakeThing(def);
                if (made == null) return false;
                if (!box.TryAddIndependent(made))
                {
                    DestroyAssemblyWithoutStorage(made);
                    return false;
                }
                crafted = true;
                part = box.TryTakeIndependent(def);
                return part != null;
            }

            List<Tuple<CompModularWeaponPartsBox, ThingDef>> debited =
                new List<Tuple<CompModularWeaponPartsBox, ThingDef>>();
            if (mode == ModularWeaponPartStorageMode.Virtual
                && AvailableCountInBoxes(def) == 0)
            {
                if (!CanInstantCraft(def)) return false;
                if (!box.TryAdd(def)) return false;
                crafted = true;
            }
            if (mode == ModularWeaponPartStorageMode.Virtual
                && !TryDebitVirtualPart(def, debited)) return false;

            part = ThingMaker.MakeThing(def);
            if (part != null && TryDebitDefaultSubtree(part, debited)) return true;

            for (int i = 0; i < debited.Count; i++)
                if (!crafted || i > 0)
                    debited[i].Item1.TryAdd(debited[i].Item2);
            DestroyAssemblyWithoutStorage(part);
            part = null;
            return false;
        }

        private int AvailableCountInBoxes(ThingDef def)
        {
            return boxes.Where(box => box != null).Sum(box => box.CountOf(def));
        }

        public void ReturnAssembly(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;
            ModularWeaponPartStorageMode mode =
                CompModularWeaponPartsBox.StorageModeFor(thing.def);
            if (mode == ModularWeaponPartStorageMode.IndependentThing)
            {
                if (boxes.FirstOrDefault(box => box != null)?.TryAddIndependent(thing) != true)
                    PlaceNearBench(thing);
                return;
            }

            CompModularWeaponNode comp = thing.TryGetComp<CompModularWeaponNode>();
            while (comp != null && comp.ChildCount > 0)
                ReturnAssembly(comp.DetachChildAt(comp.ChildCount - 1));

            if (mode == ModularWeaponPartStorageMode.Virtual)
                boxes.FirstOrDefault(box => box != null)?.TryAdd(thing.def);
            thing.Destroy(DestroyMode.Vanish);
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
                {
                    if (!CanInstantCraft(child.def)) return false;
                }
                if (childMode == ModularWeaponPartStorageMode.Virtual
                    && !CanInstantCraft(child.def)
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
            CompModularWeaponPartsBox source = boxes.FirstOrDefault(box => box?.CountOf(def) > 0);
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
