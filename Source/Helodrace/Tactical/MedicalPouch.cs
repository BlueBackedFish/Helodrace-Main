using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    public sealed class CompProperties_MedicalPouch : CompProperties
    {
        public int capacity;
        public int hemostaticCapacity;

        public CompProperties_MedicalPouch() { compClass = typeof(CompMedicalPouch); }
    }

    // The installed module owns its stock, so removing or moving it preserves charges.
    public sealed class CompMedicalPouch : ThingComp
    {
        private int charges;
        private int hemostatic;
        public CompProperties_MedicalPouch Props => (CompProperties_MedicalPouch)props;
        public int Capacity => Props.capacity;
        public int HemostaticCapacity => Props.hemostaticCapacity;
        public int Charges => charges;
        public int Hemostatic => hemostatic;
        public bool IsLarge => HemostaticCapacity > 0;

        public override void PostPostMake()
        {
            base.PostPostMake();
            charges = Capacity;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref charges, "medicalPouchCharges", Capacity);
            Scribe_Values.Look(ref hemostatic, "medicalPouchHemostatic", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                charges = Mathf.Clamp(charges, 0, Capacity);
                hemostatic = Mathf.Clamp(hemostatic, 0, HemostaticCapacity);
            }
        }

        public override string CompInspectStringExtra() =>
            IsLarge
                ? "HD_MedicalPouch_StockLarge".Translate(charges, Capacity, hemostatic, HemostaticCapacity)
                : "HD_MedicalPouch_StockSmall".Translate(charges, Capacity);

        public bool CanSpend(TcccTreatment treatment) => treatment == TcccTreatment.Hemostatic
            ? IsLarge && hemostatic > 0
            : (treatment != TcccTreatment.Analgesic || IsLarge) && charges > 0;

        public bool TrySpend(TcccTreatment treatment)
        {
            if (!CanSpend(treatment)) return false;
            if (treatment == TcccTreatment.Hemostatic) hemostatic--;
            else charges--;
            return true;
        }

        public bool CanLoad(ThingDef supply) => supply == ThingDefOf.MedicineIndustrial
            ? charges < Capacity
            : IsLarge && supply?.defName == "HD_TCCC_HemostaticAgent" && hemostatic < HemostaticCapacity;

        public bool TryLoad(Pawn wearer, Thing supply)
        {
            if (wearer == null || supply == null || supply.Destroyed || !CanLoad(supply.def)
                || !MedicalPouchUtility.WornPouches(wearer).Contains(this)
                || wearer.inventory?.innerContainer?.Contains(supply) != true
                    && !(supply.Spawned && supply.Map == wearer.Map
                        && wearer.Position.AdjacentTo8WayOrInside(supply.Position))) return false;
            ThingDef supplyDef = supply.def;
            Thing consumed = supply.SplitOff(1);
            consumed.Destroy(DestroyMode.Vanish);
            if (supplyDef == ThingDefOf.MedicineIndustrial)
                charges = System.Math.Min(Capacity, charges + 2);
            else hemostatic++;
            return true;
        }
    }

    public static class MedicalPouchUtility
    {
        public static IEnumerable<CompMedicalPouch> WornPouches(Pawn pawn)
        {
            if (pawn?.apparel?.WornApparel == null) yield break;
            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                CompModularArmor armor = apparel.TryGetComp<CompModularArmor>();
                if (armor?.Props.armorTags?.Contains("HD_IOTVGen4") != true) continue;
                foreach (InstalledModularArmorPart part in armor.InstalledParts)
                {
                    CompMedicalPouch pouch = part?.InstalledItem?.TryGetComp<CompMedicalPouch>();
                    if (pouch != null) yield return pouch;
                }
            }
        }

        public static CompMedicalPouch ForTreatment(Pawn pawn, TcccTreatment treatment) =>
            WornPouches(pawn).Where(pouch => pouch.CanSpend(treatment))
                .OrderBy(pouch => pouch.IsLarge ? 1 : 0).FirstOrDefault();

        public static void BeginLoad(Pawn pawn, CompMedicalPouch pouch, ThingDef supplyDef)
        {
            if (pawn?.Map == null || pouch == null || !pouch.CanLoad(supplyDef)
                || !WornPouches(pawn).Contains(pouch)) return;
            Thing supply = pawn.inventory?.innerContainer?.InnerListForReading
                .FirstOrDefault(item => item.def == supplyDef);
            if (supply != null) { pouch.TryLoad(pawn, supply); return; }
            supply = pawn.Map.listerThings.ThingsOfDef(supplyDef)
                .Where(item => !item.IsForbidden(pawn)
                    && pawn.CanReserveAndReach(item, PathEndMode.Touch, Danger.Deadly))
                .OrderBy(item => pawn.Position.DistanceToSquared(item.Position)).FirstOrDefault();
            if (supply == null)
            {
                Messages.Message("HD_MedicalPouch_NoSupply".Translate(supplyDef.LabelCap), pawn,
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_TCCC_LoadPouch"), supply, pouch.parent);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public static IEnumerable<Gizmo> ReloadGizmos(Pawn pawn)
        {
            foreach (CompMedicalPouch pouch in WornPouches(pawn))
            {
                CompMedicalPouch selected = pouch;
                Texture2D icon = ContentFinder<Texture2D>.Get(pouch.parent.def.graphicData.texPath, false)
                    ?? BaseContent.BadTex;
                var medicine = new Command_Action
                {
                    defaultLabel = "HD_MedicalPouch_LoadMedicine".Translate(pouch.parent.def.LabelCap),
                    defaultDesc = "HD_MedicalPouch_LoadMedicineDesc".Translate(pouch.Charges, pouch.Capacity),
                    icon = icon,
                    action = () => BeginLoad(pawn, selected, ThingDefOf.MedicineIndustrial)
                };
                if (pouch.Charges >= pouch.Capacity) medicine.Disable("HD_MedicalPouch_Full".Translate());
                yield return medicine;
                if (!pouch.IsLarge) continue;
                ThingDef agent = DefDatabase<ThingDef>.GetNamed("HD_TCCC_HemostaticAgent");
                var hemostatic = new Command_Action
                {
                    defaultLabel = "HD_MedicalPouch_LoadHemostatic".Translate(),
                    defaultDesc = "HD_MedicalPouch_LoadHemostaticDesc".Translate(pouch.Hemostatic, pouch.HemostaticCapacity),
                    icon = icon,
                    action = () => BeginLoad(pawn, selected, agent)
                };
                if (pouch.Hemostatic >= pouch.HemostaticCapacity)
                    hemostatic.Disable("HD_MedicalPouch_Full".Translate());
                yield return hemostatic;
            }
        }
    }

    public sealed class JobDriver_TcccLoadPouch : JobDriver
    {
        private Thing Supply => job.targetA.Thing;
        private Thing PouchThing => job.targetB.Thing;
        private CompMedicalPouch Pouch => PouchThing?.TryGetComp<CompMedicalPouch>();

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            Supply != null && pawn.Reserve(Supply, job, 1, 1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => Pouch == null || !Pouch.CanLoad(Supply?.def)
                || !MedicalPouchUtility.WornPouches(pawn).Contains(Pouch));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() => Pouch.TryLoad(pawn, Supply));
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_MedicalPouchGizmos
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance?.Faction == Faction.OfPlayer && !__instance.Dead)
                __result = __result.Concat(MedicalPouchUtility.ReloadGizmos(__instance));
        }
    }
}
