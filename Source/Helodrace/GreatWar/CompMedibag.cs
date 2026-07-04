using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public class CompProperties_Medibag : CompProperties
    {
        public ThingDef supplyDef;
        public ThingDef medicineDef;
        public HediffDef plasmaTransfusionHediff;
        public int maxStoredSupplies = -1;
        public int maxStoredMedicine = 5;
        public int hemostasisSupplyCost = -1;
        public int hemostasisMedicineCost = 1;
        public int plasmaSupplyCost = -1;
        public int plasmaMedicineCost = 1;
        public int hemostasisPartCount = 4;
        public float hemostasisQuality = 0.35f;
        public float hemostasisMaxQuality = 0.60f;
        public float bloodLossReduction = 0.60f;
        public int treatmentTicks = 180;
        public List<BodyPartDef> excludedHemostasisParts;

        public CompProperties_Medibag()
        {
            compClass = typeof(CompMedibag);
        }
    }

    public class CompMedibag : ThingComp
    {
        private int storedSupplies;

        public CompProperties_Medibag Props => (CompProperties_Medibag)props;

        private Pawn Wearer
        {
            get
            {
                if (parent.ParentHolder is Pawn_ApparelTracker apparelTracker)
                {
                    return apparelTracker.pawn;
                }

                return null;
            }
        }

        private ThingDef SupplyDef => Props.supplyDef ?? Props.medicineDef ?? ThingDefOf.MedicineHerbal;

        private int MaxStoredSupplies => Props.maxStoredSupplies > 0 ? Props.maxStoredSupplies : Props.maxStoredMedicine;

        private int HemostasisSupplyCost => Props.hemostasisSupplyCost > 0 ? Props.hemostasisSupplyCost : Props.hemostasisMedicineCost;

        private int PlasmaSupplyCost => Props.plasmaSupplyCost > 0 ? Props.plasmaSupplyCost : Props.plasmaMedicineCost;

        public Pawn CurrentWearer => Wearer;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref storedSupplies, "storedSupplies", 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars && storedSupplies == 0)
            {
                Scribe_Values.Look(ref storedSupplies, "storedMedicine", 0);
            }
        }

        public override string CompInspectStringExtra()
        {
            return "HD_Medibag_StoredSupplies".Translate(storedSupplies, MaxStoredSupplies, SupplyDef.label);
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn wearer = Wearer;
            if (wearer == null || wearer.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            Texture2D icon = ContentFinder<Texture2D>.Get(parent.def.graphicData.texPath, true);

            Command_Action reloadCommand = new Command_Action
            {
                defaultLabel = "HD_Medibag_Load_Label".Translate(),
                defaultDesc = "HD_Medibag_Load_Desc".Translate(SupplyDef.label, storedSupplies, MaxStoredSupplies),
                icon = icon,
                action = LoadOneSupply
            };

            if (storedSupplies >= MaxStoredSupplies)
            {
                reloadCommand.Disable("HD_Medibag_Load_Full".Translate());
            }

            yield return reloadCommand;

            if (!wearer.Drafted)
            {
                yield break;
            }

            Command_Action hemostasisCommand = new Command_Action
            {
                defaultLabel = "HD_Medibag_Hemostasis_Label".Translate(),
                defaultDesc = "HD_Medibag_Hemostasis_Desc".Translate(Props.hemostasisPartCount, HemostasisSupplyCost),
                icon = icon,
                action = BeginHemostasisTargeting
            };

            if (storedSupplies < HemostasisSupplyCost)
            {
                hemostasisCommand.Disable("HD_Medibag_NotEnoughSupplies".Translate(SupplyDef.label));
            }

            yield return hemostasisCommand;

            Command_Action plasmaCommand = new Command_Action
            {
                defaultLabel = "HD_Medibag_Plasma_Label".Translate(),
                defaultDesc = "HD_Medibag_Plasma_Desc".Translate(PlasmaSupplyCost),
                icon = icon,
                action = BeginPlasmaTargeting
            };

            if (storedSupplies < PlasmaSupplyCost)
            {
                plasmaCommand.Disable("HD_Medibag_NotEnoughSupplies".Translate(SupplyDef.label));
            }

            yield return plasmaCommand;
        }

        private void BeginHemostasisTargeting()
        {
            Find.Targeter.BeginTargeting(PawnTargetingParameters(), target => StartTreatmentJob(target.Thing as Pawn, "HD_MedibagHemostasis"));
        }

        private void BeginPlasmaTargeting()
        {
            Find.Targeter.BeginTargeting(PawnTargetingParameters(), target => StartTreatmentJob(target.Thing as Pawn, "HD_MedibagPlasmaTransfusion"));
        }

        private TargetingParameters PawnTargetingParameters()
        {
            Pawn wearer = Wearer;
            return new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetLocations = false,
                validator = target =>
                {
                    Pawn pawn = target.Thing as Pawn;
                    return pawn != null && CanUseOn(wearer, pawn);
                }
            };
        }

        public bool CanUseOn(Pawn wearer, Pawn target)
        {
            return wearer != null
                && target != null
                && !target.Dead
                && target.Spawned
                && wearer.Map == target.Map
                && !target.HostileTo(wearer);
        }

        private void StartTreatmentJob(Pawn target, string jobDefName)
        {
            Pawn wearer = Wearer;
            if (!CanUseOn(wearer, target))
            {
                return;
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(jobDefName);
            if (jobDef == null)
            {
                Log.ErrorOnce($"Helodrace: {jobDefName} JobDef is missing.", jobDefName.GetHashCode());
                return;
            }

            Job job = JobMaker.MakeJob(jobDef, target, parent);
            wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public bool TryApplyHemostasis(Pawn target)
        {
            Pawn wearer = Wearer;
            if (!CanUseOn(wearer, target) || !TryConsumeSupplies(HemostasisSupplyCost))
            {
                return false;
            }

            List<IGrouping<BodyPartRecord, Hediff>> bleedingParts = target.health.hediffSet.hediffs
                .Where(hediff => hediff.BleedRate > 0f && hediff.Part != null && !IsExcludedHemostasisPart(hediff.Part.def))
                .GroupBy(hediff => hediff.Part)
                .OrderByDescending(group => group.Sum(hediff => hediff.BleedRate))
                .Take(Props.hemostasisPartCount)
                .ToList();

            int treatedParts = 0;
            foreach (IGrouping<BodyPartRecord, Hediff> partGroup in bleedingParts)
            {
                bool treatedAny = false;
                foreach (Hediff hediff in partGroup)
                {
                    hediff.Tended(Props.hemostasisQuality, Props.hemostasisMaxQuality);
                    treatedAny = true;
                }

                if (treatedAny)
                {
                    treatedParts++;
                }
            }

            if (treatedParts == 0)
            {
                storedSupplies += HemostasisSupplyCost;
                Messages.Message("HD_Medibag_Hemostasis_NoBleeding".Translate(target.LabelShort), target, MessageTypeDefOf.RejectInput);
                return false;
            }

            Messages.Message("HD_Medibag_Hemostasis_Applied".Translate(wearer.LabelShort, target.LabelShort, treatedParts), target, MessageTypeDefOf.PositiveEvent);
            return true;
        }

        public bool TryApplyPlasma(Pawn target)
        {
            Pawn wearer = Wearer;
            if (!CanUseOn(wearer, target) || Props.plasmaTransfusionHediff == null || !TryConsumeSupplies(PlasmaSupplyCost))
            {
                return false;
            }

            if (target.health.hediffSet.HasHediff(Props.plasmaTransfusionHediff))
            {
                storedSupplies += PlasmaSupplyCost;
                Messages.Message("HD_Medibag_Plasma_AlreadyActive".Translate(target.LabelShort), target, MessageTypeDefOf.RejectInput);
                return false;
            }

            Hediff bloodLoss = target.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bloodLoss != null)
            {
                bloodLoss.Severity = Mathf.Max(0f, bloodLoss.Severity - Props.bloodLossReduction);
                if (bloodLoss.Severity <= 0f)
                {
                    target.health.RemoveHediff(bloodLoss);
                }
            }

            Hediff plasma = HediffMaker.MakeHediff(Props.plasmaTransfusionHediff, target);
            plasma.Severity = 1f;
            target.health.AddHediff(plasma);

            Messages.Message("HD_Medibag_Plasma_Applied".Translate(wearer.LabelShort, target.LabelShort), target, MessageTypeDefOf.PositiveEvent);
            return true;
        }

        private bool TryConsumeSupplies(int count)
        {
            if (storedSupplies < count)
            {
                Messages.Message("HD_Medibag_NotEnoughSupplies".Translate(SupplyDef.label), parent, MessageTypeDefOf.RejectInput);
                return false;
            }

            storedSupplies -= count;
            return true;
        }

        private void LoadOneSupply()
        {
            Pawn wearer = Wearer;
            if (wearer == null || storedSupplies >= MaxStoredSupplies)
            {
                return;
            }

            Thing supply = wearer.inventory?.innerContainer?.FirstOrDefault(thing => thing.def == SupplyDef);
            if (supply != null)
            {
                ConsumeSupplyThing(supply);
                return;
            }

            Find.Targeter.BeginTargeting(SupplyTargetingParameters(), target => StartLoadSupplyJob(target.Thing));
        }

        private TargetingParameters SupplyTargetingParameters()
        {
            Pawn wearer = Wearer;
            return new TargetingParameters
            {
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetItems = true,
                canTargetLocations = false,
                validator = target =>
                {
                    Thing thing = target.Thing;
                    return wearer != null
                        && thing != null
                        && thing.def == SupplyDef
                        && thing.Spawned
                        && thing.Map == wearer.Map
                        && !thing.IsForbidden(wearer)
                        && wearer.CanReserveAndReach(thing, PathEndMode.Touch, Danger.Deadly);
                }
            };
        }

        private void StartLoadSupplyJob(Thing supply)
        {
            Pawn wearer = Wearer;
            if (wearer == null || !CanLoadSupply(supply))
            {
                return;
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_MedibagLoadSupply");
            if (jobDef == null)
            {
                Log.ErrorOnce("Helodrace: HD_MedibagLoadSupply JobDef is missing.", "HD_MedibagLoadSupply".GetHashCode());
                return;
            }

            Job job = JobMaker.MakeJob(jobDef, supply, parent);
            wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public bool CanLoadSupply(Thing supply)
        {
            Pawn wearer = Wearer;
            return wearer != null
                && supply != null
                && supply.def == SupplyDef
                && supply.Spawned
                && supply.Map == wearer.Map
                && storedSupplies < MaxStoredSupplies;
        }

        public bool TryLoadSupply(Thing supply)
        {
            if (!CanLoadSupply(supply))
            {
                return false;
            }

            ConsumeSupplyThing(supply);
            return true;
        }

        private void ConsumeSupplyThing(Thing supply)
        {
            Thing consumed = supply.SplitOff(1);
            consumed.Destroy(DestroyMode.Vanish);
            storedSupplies++;
        }

        private bool IsExcludedHemostasisPart(BodyPartDef partDef)
        {
            if (partDef == null)
            {
                return false;
            }

            if (Props.excludedHemostasisParts != null && Props.excludedHemostasisParts.Contains(partDef))
            {
                return true;
            }

            string defName = partDef.defName;
            return defName == "Brain" || defName == "Heart" || defName == "Lung" || defName == "Liver";
        }
    }

    public class JobDriver_MedibagHemostasis : JobDriver_MedibagTreatment
    {
        protected override bool ApplyTreatment(CompMedibag medibag, Pawn target)
        {
            return medibag.TryApplyHemostasis(target);
        }
    }

    public class JobDriver_MedibagPlasmaTransfusion : JobDriver_MedibagTreatment
    {
        protected override bool ApplyTreatment(CompMedibag medibag, Pawn target)
        {
            return medibag.TryApplyPlasma(target);
        }
    }

    public class JobDriver_MedibagLoadSupply : JobDriver
    {
        private const TargetIndex SupplyInd = TargetIndex.A;
        private const TargetIndex MedibagInd = TargetIndex.B;

        protected Thing SupplyThing => job.GetTarget(SupplyInd).Thing;
        protected Thing MedibagThing => job.GetTarget(MedibagInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(SupplyThing, job, 1, 1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(SupplyInd);
            this.FailOnForbidden(SupplyInd);
            this.FailOn(() => MedibagThing?.TryGetComp<CompMedibag>() == null);
            this.FailOn(() => MedibagThing?.TryGetComp<CompMedibag>()?.CurrentWearer != pawn);
            this.FailOn(() => MedibagThing?.TryGetComp<CompMedibag>()?.CanLoadSupply(SupplyThing) != true);

            yield return Toils_Goto.GotoThing(SupplyInd, PathEndMode.Touch);

            yield return new Toil
            {
                initAction = delegate
                {
                    MedibagThing.TryGetComp<CompMedibag>()?.TryLoadSupply(SupplyThing);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    public abstract class JobDriver_MedibagTreatment : JobDriver
    {
        private const TargetIndex PatientInd = TargetIndex.A;
        private const TargetIndex MedibagInd = TargetIndex.B;

        protected Pawn Patient => job.GetTarget(PatientInd).Pawn;
        protected Thing MedibagThing => job.GetTarget(MedibagInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Patient == null || Patient.Dead);
            this.FailOn(() => MedibagThing?.TryGetComp<CompMedibag>() == null);
            this.FailOn(() => MedibagThing?.TryGetComp<CompMedibag>()?.CurrentWearer != pawn);

            if (Patient != pawn)
            {
                yield return Toils_Goto.GotoThing(PatientInd, PathEndMode.Touch);
            }

            CompMedibag medibag = MedibagThing.TryGetComp<CompMedibag>();
            Toil treatment = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = medibag.Props.treatmentTicks
            };
            treatment.WithProgressBarToilDelay(PatientInd);
            yield return treatment;

            yield return new Toil
            {
                initAction = delegate
                {
                    CompMedibag currentMedibag = MedibagThing.TryGetComp<CompMedibag>();
                    if (currentMedibag != null && currentMedibag.CanUseOn(pawn, Patient))
                    {
                        ApplyTreatment(currentMedibag, Patient);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        protected abstract bool ApplyTreatment(CompMedibag medibag, Pawn target);
    }

    [HarmonyPatch(typeof(Thing), "get_DrawColor")]
    public static class Patch_Medibag_DrawColor
    {
        public static void Postfix(Thing __instance, ref Color __result)
        {
            if (UsesFixedApparelColor(__instance))
            {
                __result = Color.white;
            }
        }

        private static bool UsesFixedApparelColor(Thing thing)
        {
            string defName = thing?.def?.defName;
            return defName == "HD_Apparel_GreatWarMedibag"
                || defName == "HD_Apparel_GreatWarGasMaskPouch"
                || defName == "HD_Apparel_GreatWarCBRNPouch";
        }
    }

    [HarmonyPatch(typeof(Thing), "get_DrawColorTwo")]
    public static class Patch_Medibag_DrawColorTwo
    {
        public static void Postfix(Thing __instance, ref Color __result)
        {
            if (UsesFixedApparelColor(__instance))
            {
                __result = Color.white;
            }
        }

        private static bool UsesFixedApparelColor(Thing thing)
        {
            string defName = thing?.def?.defName;
            return defName == "HD_Apparel_GreatWarMedibag"
                || defName == "HD_Apparel_GreatWarGasMaskPouch"
                || defName == "HD_Apparel_GreatWarCBRNPouch";
        }
    }
}
