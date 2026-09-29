using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    public sealed class CompProperties_ClothingWorkbench : CompProperties
    {
        public CompProperties_ClothingWorkbench() { compClass = typeof(CompClothingWorkbench); }
    }

    public sealed class CompClothingWorkbench : ThingComp
    {
        internal bool IsAvailable => parent?.Spawned == true && !parent.Destroyed
            && parent.Faction == Faction.OfPlayer;

        internal bool CanCustomize(CompModularArmor armor)
        {
            if (!IsAvailable || armor?.parent == null || armor.parent.Destroyed
                || !armor.Props.allowPlayerConfiguration
                || armor.Props.slots.NullOrEmpty() && armor.Props.palsPanels.NullOrEmpty()) return false;
            Pawn wearer = armor.Wearer;
            return wearer != null
                ? wearer.Spawned && wearer.Map == parent.Map && wearer.Faction == Faction.OfPlayer
                : armor.parent.Spawned && armor.parent.Map == parent.Map
                    && (armor.parent.Faction == null || armor.parent.Faction == Faction.OfPlayer)
                    && !armor.parent.IsForbidden(Faction.OfPlayer);
        }

        internal static bool CanUse(Thing bench, CompModularArmor armor)
            => bench?.TryGetComp<CompClothingWorkbench>()?.CanCustomize(armor) == true;

        internal static bool CanWorkAt(Pawn worker, Thing bench, CompModularArmor armor)
            => CanUse(bench, armor) && worker?.Spawned == true
                && worker == armor.Wearer && worker.Map == bench.Map
                && !worker.Dead && !worker.Downed && !worker.Drafted
                && !bench.IsForbidden(worker)
                && worker.CanReserveAndReach(bench, PathEndMode.InteractionCell, Danger.Some);

        private List<CompModularArmor> AvailableApparel()
        {
            if (!IsAvailable) return new List<CompModularArmor>();
            var apparel = new List<CompModularArmor>();
            foreach (Pawn pawn in parent.Map.mapPawns.FreeColonistsSpawned)
                if (pawn.apparel != null)
                    foreach (Apparel worn in pawn.apparel.WornApparel)
                    {
                        CompModularArmor armor = worn.TryGetComp<CompModularArmor>();
                        if (CanCustomize(armor)) apparel.Add(armor);
                    }
            foreach (Thing thing in parent.Map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel))
            {
                CompModularArmor armor = thing.TryGetComp<CompModularArmor>();
                if (CanCustomize(armor)) apparel.Add(armor);
            }
            return apparel.Distinct().OrderBy(armor => armor.parent.LabelCap.ToString()).ToList();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (!IsAvailable) yield break;
            var command = new Command_Action
            {
                defaultLabel = "HD_ClothingBench_Configure".Translate(),
                defaultDesc = "HD_ClothingBench_ConfigureDesc".Translate(),
                icon = parent.def.uiIcon,
                action = OpenApparelMenu
            };
            if (AvailableApparel().Count == 0)
                command.Disable("HD_ClothingBench_NoApparel".Translate());
            yield return command;
            if (AvailableApparel().Any(armor =>
            {
                return AmmoPouchUtility.CapacityFor(
                    armor.Wearer?.equipment?.Primary) > 0 && armor.InstalledParts.Any(part =>
                    part?.InstalledItem?.TryGetComp<CompAmmoPouch>()
                        ?.SteelNeeded > 0);
            }))
                yield return new Command_Action
                {
                    defaultLabel = "HD_AmmoPouch_Replenish".Translate(),
                    defaultDesc = "HD_AmmoPouch_ReplenishDesc".Translate(),
                    icon = parent.def.uiIcon,
                    action = OpenAmmoPouchMenu
                };
        }

        private void OpenAmmoPouchMenu()
        {
            ThingDef carbonSteel = DefDatabase<ThingDef>.GetNamedSilentFail("HD_CarbonSteel");
            if (carbonSteel == null || !IsAvailable) return;
            var options = new List<FloatMenuOption>();
            foreach (CompModularArmor armor in AvailableApparel())
            {
                Pawn worker = armor.Wearer;
                ThingWithComps weapon = worker?.equipment?.Primary;
                int capacity = AmmoPouchUtility.CapacityFor(weapon);
                if (capacity <= 0 || !CanWorkAt(worker, parent, armor)) continue;
                foreach (InstalledModularArmorPart installed in armor.InstalledParts)
                {
                    CompAmmoPouch pouch = installed?.InstalledItem?.TryGetComp<CompAmmoPouch>();
                    if (pouch == null || pouch.SteelNeeded == 0) continue;
                    int steelNeeded = pouch.SteelNeeded;
                    Thing source = worker.inventory?.innerContainer
                        ?.Where(item => item.def == carbonSteel && item.stackCount > 0)
                        .OrderByDescending(item => item.stackCount).FirstOrDefault();
                    if (source == null)
                        source = parent.Map.listerThings.ThingsOfDef(carbonSteel)
                            .Where(item => item.stackCount > 0
                                && !item.IsForbidden(worker)
                                && worker.CanReserveAndReach(item,
                                    PathEndMode.ClosestTouch, Danger.Some))
                            .OrderByDescending(item => item.stackCount).FirstOrDefault();
                    Thing selectedSource = source;
                    Thing selectedPouch = pouch.parent;
                    int suppliedSteel = System.Math.Min(steelNeeded, source?.stackCount ?? 0);
                    string label = "HD_AmmoPouch_ReplenishOption".Translate(
                        worker.LabelShortCap, installed.part.LabelCap,
                        pouch.RoundsFor(capacity), capacity, suppliedSteel);
                    if (selectedSource == null)
                    {
                        options.Add(new FloatMenuOption(label + " (" +
                            "HD_AmmoPouch_NeedSteel".Translate(steelNeeded) + ")", null));
                        continue;
                    }
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        if (!CanWorkAt(worker, parent, armor)
                            || !armor.InstalledParts.Any(part => part?.InstalledItem == selectedPouch))
                            return;
                        JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("HD_ReplenishAmmoPouch");
                        if (def == null) return;
                        Job job = JobMaker.MakeJob(def, armor.parent, parent, selectedSource);
                        job.targetQueueA = new List<LocalTargetInfo> { selectedPouch };
                        job.count = suppliedSteel;
                        worker.jobs.TryTakeOrderedJob(job);
                    }));
                }
            }
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenApparelMenu()
        {
            if (!IsAvailable) return;
            var options = new List<FloatMenuOption>();
            foreach (CompModularArmor armor in AvailableApparel())
            {
                string label = armor.Wearer != null
                    ? "HD_ClothingBench_WornApparel".Translate(armor.Wearer.LabelShortCap, armor.parent.LabelCap).ToString()
                    : "HD_ClothingBench_StoredApparel".Translate(armor.parent.LabelCap).ToString();
                options.Add(new FloatMenuOption(label, delegate
                {
                    if (CanCustomize(armor)) Find.WindowStack.Add(new Dialog_ModularArmor(armor, this));
                }));
            }
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
