using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class CompProperties_GasMaskPouch : CompProperties
    {
        public float toxicEnvironmentResistance = 1f;
        public bool protectsSweetGas;
        public int wearTicks = 120;

        public CompProperties_GasMaskPouch()
        {
            compClass = typeof(CompGasMaskPouch);
        }
    }

    public sealed class CompGasMaskPouch : ThingComp
    {
        private const string WearMaskJobDefName = "HD_WearGasMask";

        private bool maskWorn;

        public CompProperties_GasMaskPouch Props => (CompProperties_GasMaskPouch)props;

        public Pawn Wearer => (parent.ParentHolder as Pawn_ApparelTracker)?.pawn;

        public bool ProtectionActive => maskWorn && Wearer != null;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref maskWorn, "gasMaskWorn", false);
        }

        public override string CompInspectStringExtra()
        {
            return (ProtectionActive
                ? "HD_GasMaskPouch_Status_Worn"
                : "HD_GasMaskPouch_Status_Stored").Translate();
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

            Command_Toggle command = new Command_Toggle
            {
                defaultLabel = (maskWorn
                    ? "HD_GasMaskPouch_Remove_Label"
                    : "HD_GasMaskPouch_Wear_Label").Translate(),
                defaultDesc = "HD_GasMaskPouch_Toggle_Desc".Translate(
                    (Props.wearTicks / 60f).ToString("0.#")),
                icon = ContentFinder<Texture2D>.Get(parent.def.graphicData.texPath, true),
                isActive = () => maskWorn,
                toggleAction = ToggleMask
            };

            if (wearer.CurJobDef?.defName == WearMaskJobDefName
                && wearer.CurJob.GetTarget(TargetIndex.B).Thing == parent)
            {
                command.Disable("HD_GasMaskPouch_Wearing".Translate());
            }

            yield return command;
        }

        private void ToggleMask()
        {
            if (maskWorn)
            {
                SetMaskWorn(false);
                return;
            }

            Pawn wearer = Wearer;
            if (wearer == null)
            {
                return;
            }

            if (Props.wearTicks <= 0)
            {
                SetMaskWorn(true);
                return;
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(WearMaskJobDefName);
            if (jobDef == null)
            {
                Log.ErrorOnce("Helodrace: HD_WearGasMask JobDef is missing.", 58412701);
                return;
            }

            Job job = JobMaker.MakeJob(jobDef, wearer, parent);
            wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public void SetMaskWorn(bool worn)
        {
            maskWorn = worn;
            Wearer?.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }

    public static class GasMaskPouchUtility
    {
        public static CompGasMaskPouch ActiveMask(Pawn pawn, bool requireSweetGasProtection = false)
        {
            List<Apparel> apparel = pawn?.apparel?.WornApparel;
            if (apparel == null)
            {
                return null;
            }

            for (int i = 0; i < apparel.Count; i++)
            {
                CompGasMaskPouch mask = apparel[i]?.TryGetComp<CompGasMaskPouch>();
                if (mask?.ProtectionActive == true
                    && (!requireSweetGasProtection || mask.Props.protectsSweetGas))
                {
                    return mask;
                }
            }

            return null;
        }

        public static bool HasActiveMask(Pawn pawn, bool requireSweetGasProtection = false)
        {
            return ActiveMask(pawn, requireSweetGasProtection) != null;
        }
    }

    public sealed class JobDriver_WearGasMask : JobDriver
    {
        private const TargetIndex WearerInd = TargetIndex.A;
        private const TargetIndex PouchInd = TargetIndex.B;

        private Thing Pouch => job.GetTarget(PouchInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Pouch?.TryGetComp<CompGasMaskPouch>() == null);
            this.FailOn(() => Pouch?.TryGetComp<CompGasMaskPouch>()?.Wearer != pawn);
            this.FailOn(() => Pouch?.TryGetComp<CompGasMaskPouch>()?.ProtectionActive == true);

            CompGasMaskPouch mask = Pouch.TryGetComp<CompGasMaskPouch>();
            Toil wear = new Toil
            {
                initAction = () => pawn.pather.StopDead(),
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = Mathf.Max(1, mask.Props.wearTicks)
            };
            wear.WithProgressBarToilDelay(WearerInd);
            yield return wear;

            yield return new Toil
            {
                initAction = delegate
                {
                    CompGasMaskPouch currentMask = Pouch?.TryGetComp<CompGasMaskPouch>();
                    if (currentMask?.Wearer == pawn)
                    {
                        currentMask.SetMaskWorn(true);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    public sealed class PawnRenderNodeWorker_ActiveGasMask : PawnRenderNodeWorker_FlipWhenCrawling
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            return base.CanDrawNow(node, parms)
                && GasMaskPouchUtility.HasActiveMask(parms.pawn);
        }
    }

    [HarmonyPatch(typeof(StatExtension), nameof(StatExtension.GetStatValue))]
    public static class Patch_StatExtension_GetStatValue_GasMaskPouch
    {
        public static void Postfix(Thing thing, StatDef stat, ref float __result)
        {
            if (stat != StatDefOf.ToxicEnvironmentResistance || !(thing is Pawn pawn))
            {
                return;
            }

            CompGasMaskPouch mask = GasMaskPouchUtility.ActiveMask(pawn);
            if (mask != null)
            {
                __result = Mathf.Clamp01(__result + mask.Props.toxicEnvironmentResistance);
            }
        }
    }
}
