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
