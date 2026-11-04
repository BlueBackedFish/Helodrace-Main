using Verse;

namespace Helodrace.ModernWar
{
    public sealed partial class Dialog_ModularArmor
    {
        private bool EditorCanAcquire(ModularArmorPartDef part, ModularArmorSlotDef replacing,
            out string rejection)
        {
            rejection = null;
            if (DebugSettings.godMode || part.RequiredThingDef == null
                || EditorStock(part.RequiredThingDef, replacing) > 0) return true;
            if (part.CanInstantCraft)
                return ArmorInstantCrafting.CanCraftNow(part, bench.parent, original.Wearer, out rejection);
            rejection = "HD_ModularArmor_PartRequired".Translate();
            return false;
        }

        private string EditorAcquisitionLabel(ModularArmorPartDef part, int stock, string rejection)
        {
            if (stock > 0) return "HD_ModularArmor_PartStock".Translate(stock);
            if (!rejection.NullOrEmpty()) return "HD_ArmorCraft_Unavailable".Translate();
            return (part.CanInstantCraft ? "HD_ArmorCraft_Instant" : "HD_ArmorCraft_Independent").Translate();
        }

        private static string EditorPartTip(ModularArmorPartDef part)
            => part.CanInstantCraft
                ? "HD_ArmorCraft_Cost".Translate(ArmorInstantCrafting.CostText(part)).Resolve()
                : "HD_ArmorCraft_IndependentTip".Translate().Resolve();
    }
}
