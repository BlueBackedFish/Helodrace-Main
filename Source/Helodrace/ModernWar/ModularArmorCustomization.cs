using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    public sealed class ModularArmorWorkOrder : IExposable
    {
        public int jobId, workTicks;
        public string baseline;
        public List<ModularArmorEdit> plan;

        public void ExposeData()
        {
            Scribe_Values.Look(ref jobId, "jobId");
            Scribe_Values.Look(ref workTicks, "workTicks", 60);
            Scribe_Values.Look(ref baseline, "baseline");
            Scribe_Collections.Look(ref plan, "edits", LookMode.Deep);
        }
    }

    public sealed class ModularArmorEdit : IExposable
    {
        public ModularArmorSlotDef slot;
        public ModularArmorPartDef part;
        public ModularArmorPositionDef position;
        public ModularArmorPalsPanelDef panel;
        public int x, y, sourceIndex = -1;
        public bool swapped;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref slot, "slot");
            Scribe_Defs.Look(ref part, "part");
            Scribe_Defs.Look(ref position, "position");
            Scribe_Defs.Look(ref panel, "panel");
            Scribe_Values.Look(ref x, "x");
            Scribe_Values.Look(ref y, "y");
            Scribe_Values.Look(ref sourceIndex, "sourceIndex", -1);
            Scribe_Values.Look(ref swapped, "swapped");
        }

        internal InstalledModularArmorPart Record()
            => new InstalledModularArmorPart
            {
                slot = slot, part = part, position = position, palsPanel = panel,
                palsX = x, palsY = y, plateOrderSwapped = swapped
            };

        internal static ModularArmorEdit From(InstalledModularArmorPart record, int source)
            => new ModularArmorEdit
            {
                slot = record.slot, part = record.part, position = record.position,
                panel = record.palsPanel, x = record.palsX, y = record.palsY,
                swapped = record.plateOrderSwapped, sourceIndex = source
            };
    }

    public sealed partial class CompModularArmor
    {
        private bool isPreviewDraft;
        private ModularArmorWorkOrder pendingCustomization;

        internal ModularArmorWorkOrder WorkOrderFor(int jobId)
            => pendingCustomization?.jobId == jobId ? pendingCustomization : null;

        internal void ClearWorkOrder(int jobId)
        {
            if (pendingCustomization?.jobId == jobId) pendingCustomization = null;
        }

        internal CompModularArmor CreateEditorDraft()
        {
            var draft = new CompModularArmor { parent = parent, props = props, isPreviewDraft = true };
            draft.installedParts = InstalledParts.Select((record, index) =>
            {
                var copy = ModularArmorEdit.From(record, index).Record();
                copy.previewSource = record;
                copy.previewSourceIndex = index;
                return copy;
            }).ToList();
            return draft;
        }

        internal List<ModularArmorEdit> EditorPlan()
            => InstalledParts.Select(record => ModularArmorEdit.From(record, record.previewSourceIndex)).ToList();

        // Physical item identity is included; health can change while work is pending.
        internal string ConfigurationSignature()
            => string.Join(";", InstalledParts.Select(record => string.Join("|",
                record.slot?.defName, record.part?.defName, record.position?.defName,
                record.palsPanel?.defName, record.palsX, record.palsY,
                record.plateOrderSwapped, record.InstalledItem?.thingIDNumber ?? -1)));

        internal bool ValidateEditorPlan(List<ModularArmorEdit> plan)
        {
            if (plan == null || plan.Any(entry => entry?.part == null)) return false;
            var candidate = new CompModularArmor
            {
                parent = parent, props = props, isPreviewDraft = true,
                installedParts = new List<InstalledModularArmorPart>()
            };
            foreach (var entry in plan.Where(entry => entry.panel == null))
                if (candidate.InstalledIn(entry.slot) != null
                    || !candidate.SetPart(entry.slot, entry.part, entry.position)) return false;
            foreach (var entry in plan.Where(entry => entry.panel != null))
                if (candidate.InstallPalsPart(entry.part, entry.panel, entry.x, entry.y) == null) return false;
            return candidate.InstalledParts.Count == plan.Count
                && (Props.slots.NullOrEmpty() || Props.slots.All(slot => !slot.required
                    || candidate.InstalledIn(slot) != null));
        }

        private InstalledModularArmorPart ReusableSource(ModularArmorEdit entry)
        {
            var source = entry.sourceIndex >= 0 && entry.sourceIndex < InstalledParts.Count
                ? InstalledParts[entry.sourceIndex] : null;
            return source?.InstalledItem != null
                && source.part.RequiredThingDef == entry.part.RequiredThingDef ? source : null;
        }

        private void MatchReusableItems(List<ModularArmorEdit> plan)
        {
            var used = new HashSet<int>();
            foreach (var entry in plan)
            {
                if (entry.part.RequiredThingDef == null && entry.sourceIndex >= 0
                    && entry.sourceIndex < InstalledParts.Count
                    && InstalledParts[entry.sourceIndex].part.RequiredThingDef == null
                    && used.Add(entry.sourceIndex)) continue;
                if (ReusableSource(entry) != null && used.Add(entry.sourceIndex)) continue;
                entry.sourceIndex = -1;
            }
            foreach (var entry in plan.Where(entry => entry.sourceIndex < 0
                && entry.part.RequiredThingDef != null))
                for (int i = 0; i < InstalledParts.Count; i++)
                    if (!used.Contains(i) && InstalledParts[i].InstalledItem != null
                        && InstalledParts[i].part.RequiredThingDef == entry.part.RequiredThingDef)
                    {
                        entry.sourceIndex = i;
                        used.Add(i);
                        break;
                    }
        }

        internal bool TryStartCustomization(CompModularArmor draft, string baseline,
            Thing bench, out string rejection)
        {
            rejection = null;
            var plan = draft.EditorPlan();
            if (!CompClothingWorkbench.CanUse(bench, this))
                rejection = "HD_ClothingBench_Required".Translate();
            else if (ConfigurationSignature() != baseline)
                rejection = "HD_ClothingBench_Changed".Translate();
            else if (!ValidateEditorPlan(plan))
                rejection = "HD_ModularArmor_InvalidInstall".Translate();
            if (rejection != null) return false;
            if (draft.ConfigurationSignature() == baseline) return true;
            MatchReusableItems(plan);

            var needed = plan.Where(entry => entry.part.RequiredThingDef != null
                && ReusableSource(entry) == null).ToList();
            if (DebugSettings.godMode)
            {
                var items = needed.Select(entry => MakeRequiredItem(entry.part)).ToList();
                bool result = CompleteCustomization(plan, baseline, items);
                if (!result) foreach (var item in items) item?.Destroy(DestroyMode.Vanish);
                return result;
            }
            Pawn worker = Wearer;
            if (worker == null)
            {
                rejection = "HD_ModularArmor_MustBeWorn".Translate();
                return false;
            }
            if (!CompClothingWorkbench.CanWorkAt(worker, bench, this))
            {
                rejection = "HD_ClothingBench_Unreachable".Translate();
                return false;
            }
            var sources = new List<Thing>();
            var allocated = new Dictionary<Thing, int>();
            foreach (var entry in needed)
            {
                ThingDef itemDef = entry.part.RequiredThingDef;
                var available = (worker.inventory?.innerContainer?.ToList() ?? new List<Thing>())
                    .Concat(worker.Map.listerThings.ThingsOfDef(itemDef))
                    .Where(item => item.def == itemDef && !item.Destroyed
                        && item.stackCount > (allocated.TryGetValue(item, out int used) ? used : 0)
                        && (!item.Spawned || !item.IsForbidden(worker)
                            && worker.CanReserveAndReach(item, PathEndMode.ClosestTouch, Danger.Some)))
                    .OrderBy(item => item.Spawned ? 1 : 0)
                    .ThenBy(item => item.PositionHeld.DistanceToSquared(worker.Position)).FirstOrDefault();
                if (available == null)
                {
                    rejection = "HD_ModularArmor_NoReachablePart".Translate(itemDef.LabelCap);
                    return false;
                }
                sources.Add(available);
                allocated[available] = allocated.TryGetValue(available, out int count) ? count + 1 : 1;
            }
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("HD_CustomizeModularArmor");
            if (def == null)
            {
                rejection = "HD_ModularArmor_JobMissing".Translate();
                return false;
            }
            Job job = JobMaker.MakeJob(def, parent, bench);
            job.targetQueueA = sources.Select(item => new LocalTargetInfo(item)).ToList();
            var order = new ModularArmorWorkOrder { jobId = job.loadID, plan = plan, baseline = baseline };
            order.workTicks = Math.Max(60, plan.Sum(entry =>
            {
                var original = entry.sourceIndex >= 0 && entry.sourceIndex < InstalledParts.Count
                    ? InstalledParts[entry.sourceIndex] : null;
                return original != null && SamePlacement(entry, original) ? 0
                    : Math.Max(1, entry.part.installWorkTicks);
            }) + InstalledParts.Count(record => !plan.Any(entry => entry.sourceIndex >= 0
                && entry.sourceIndex < InstalledParts.Count && InstalledParts[entry.sourceIndex] == record)) * 120);
            pendingCustomization = order;
            if (worker.jobs.TryTakeOrderedJob(job, JobTag.Misc)) return true;
            ClearWorkOrder(job.loadID);
            rejection = "HD_ModularArmor_CannotStartJob".Translate();
            return false;
        }

        private static bool SamePlacement(ModularArmorEdit entry, InstalledModularArmorPart record)
            => entry.part == record.part && entry.slot == record.slot && entry.position == record.position
                && entry.panel == record.palsPanel && entry.x == record.palsX && entry.y == record.palsY
                && entry.swapped == record.plateOrderSwapped;

        internal bool CompleteCustomization(List<ModularArmorEdit> plan, string baseline, List<Thing> supplied)
        {
            if (ConfigurationSignature() != baseline || !ValidateEditorPlan(plan)) return false;
            var previous = InstalledParts.ToList();
            var next = plan.Select(entry => entry.Record()).ToList();
            var usedSources = new HashSet<InstalledModularArmorPart>();
            var unused = supplied?.ToList() ?? new List<Thing>();
            var transfers = new List<Tuple<InstalledModularArmorPart, Thing>>();
            // Preflight the entire batch before moving any item or modifying any record.
            foreach (var entry in plan)
            {
                if (entry.part.RequiredThingDef == null)
                {
                    transfers.Add(Tuple.Create<InstalledModularArmorPart, Thing>(null, null));
                    continue;
                }
                var source = ReusableSource(entry);
                Thing item = source?.InstalledItem;
                if (source != null && !usedSources.Add(source)) return false;
                if (item == null)
                {
                    item = unused.FirstOrDefault(thing => thing != null && !thing.Destroyed
                        && thing.def == entry.part.RequiredThingDef && thing.stackCount == 1);
                    if (item == null) return false;
                    unused.Remove(item);
                }
                transfers.Add(Tuple.Create(source, item));
            }
            for (int i = 0; i < transfers.Count; i++)
            {
                var transfer = transfers[i];
                if (transfer.Item2 == null) continue;
                Thing item = transfer.Item1 != null ? transfer.Item1.RemoveInstalledItem() : transfer.Item2;
                item.holdingOwner?.Remove(item);
                if (next[i].TryInstallRequiredItem(item)) continue;
                // Restore already transferred items if an unexpected container failure occurs.
                for (int j = 0; j <= i; j++)
                {
                    Thing restore = j == i ? item : next[j].RemoveInstalledItem();
                    if (restore == null) continue;
                    if (transfers[j].Item1 != null) transfers[j].Item1.TryInstallRequiredItem(restore);
                    else ReturnLooseItems(new[] { restore });
                }
                return false;
            }
            installedParts = next;
            ReturnLooseItems(previous.Select(record => record.RemoveInstalledItem()).Concat(unused));
            NotifyConfigurationChanged();
            return true;
        }
    }

    public sealed class JobDriver_CustomizeModularArmor : JobDriver
    {
        internal List<ModularArmorEdit> plan;
        internal string baseline;
        internal int workTicks;
        private List<Thing> collected = new List<Thing>();
        private Thing Bench => job.GetTarget(TargetIndex.B).Thing;
        private CompModularArmor Armor => job.GetTarget(TargetIndex.A).Thing?.TryGetComp<CompModularArmor>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref plan, "armorEdits", LookMode.Deep);
            Scribe_Collections.Look(ref collected, "collectedParts", LookMode.Reference);
            Scribe_Values.Look(ref baseline, "armorBaseline");
            Scribe_Values.Look(ref workTicks, "workTicks", 60);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (plan == null)
            {
                var order = Armor?.WorkOrderFor(job.loadID);
                plan = order?.plan;
                baseline = order?.baseline;
                workTicks = order?.workTicks ?? 60;
            }
            if (plan == null || !CompClothingWorkbench.CanWorkAt(pawn, Bench, Armor)
                || !pawn.Reserve(Bench, job, 1, 1, null, errorOnFailed)) return false;
            foreach (var group in job.targetQueueA.Skip(collected.Count).Select(target => target.Thing).GroupBy(item => item))
            {
                Thing item = group.Key;
                if (item == null || item.Destroyed || item.stackCount < group.Count()) return false;
                if (item.Spawned)
                {
                    if (!pawn.Reserve(item, job, 1, group.Count(), null, errorOnFailed)) return false;
                }
                else if (pawn.inventory?.innerContainer?.Contains(item) != true) return false;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(condition => Armor?.ClearWorkOrder(job.loadID));
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => Armor?.Wearer != pawn || !CompClothingWorkbench.CanUse(Bench, Armor)
                || Armor.ConfigurationSignature() != baseline);
            for (int i = 0; i < job.targetQueueA.Count; i++)
            {
                int index = i;
                Toil collect = Toils_General.Do(() =>
                {
                    Thing source = job.targetQueueA[index].Thing;
                    Thing carried = pawn.carryTracker.CarriedThing;
                    Thing item = carried != null ? pawn.carryTracker.innerContainer.Take(carried, 1)
                        : pawn.inventory.innerContainer.Take(source, 1);
                    if (item == null || !pawn.inventory.innerContainer.TryAdd(item, false))
                    {
                        if (item != null) GenPlace.TryPlaceThing(item, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    collected.Add(item);
                });
                yield return Toils_General.Do(() =>
                {
                    job.SetTarget(TargetIndex.C, job.targetQueueA[index]);
                    job.count = 1;
                });
                yield return Toils_Jump.JumpIf(collect,
                    () => pawn.inventory.innerContainer.Contains(job.GetTarget(TargetIndex.C).Thing));
                yield return Toils_Goto.GotoThing(TargetIndex.C, PathEndMode.ClosestTouch);
                yield return Toils_Haul.StartCarryThing(TargetIndex.C, false, true, false);
                yield return collect;
            }
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);
            yield return Toils_General.Do(() => pawn.rotationTracker.FaceTarget(Bench));
            var work = Toils_General.Wait(workTicks, TargetIndex.B);
            work.handlingFacing = true;
            work.WithProgressBarToilDelay(TargetIndex.B);
            yield return work;
            yield return Toils_General.Do(() =>
            {
                if (collected.Any(item => item == null || !pawn.inventory.innerContainer.Contains(item))
                    || !Armor.CompleteCustomization(plan, baseline, collected))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                Messages.Message("HD_ClothingBench_Complete".Translate(), pawn,
                    MessageTypeDefOf.PositiveEvent, false);
            });
        }
    }
}
