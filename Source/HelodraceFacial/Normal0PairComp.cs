using FacialAnimation;
using Verse;

namespace HelodraceFacial
{
    public sealed class Normal0PairCompProperties : CompProperties
    {
        public float chance = 0.05f;

        public Normal0PairCompProperties()
        {
            compClass = typeof(Normal0PairComp);
        }
    }

    public sealed class Normal0PairComp : ThingComp
    {
        private const string LidNormal0DefName = "HD_FA_LidNormal0";
        private const string MouthNormal0DefName = "HD_FA_MouthNormal0";
        private const string LidFallbackDefName = "HD_FA_LidNormal";
        private const string MouthFallbackDefName = "HD_FA_MouthNormal";

        private bool initialized;
        private bool useNormal0;

        private Normal0PairCompProperties PairProps => (Normal0PairCompProperties)props;

        public override void PostPostMake()
        {
            base.PostPostMake();
            InitializeNewPairIfNeeded();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            InitializeNewPairIfNeeded();
            ApplyStoredPair();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref initialized, "helodNormal0PairInitialized", false);
            Scribe_Values.Look(ref useNormal0, "helodUsesNormal0Pair", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (!initialized)
                {
                    MigrateExistingPawn();
                }

                ApplyStoredPair();
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (parent.IsHashIntervalTick(60))
            {
                RepairRuntimeMismatch();
            }
        }

        private void InitializeNewPairIfNeeded()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            useNormal0 = Rand.Chance(Clamp01(PairProps.chance));
            ApplyStoredPair();
        }

        private void MigrateExistingPawn()
        {
            LidControllerComp lid = parent.GetComp<LidControllerComp>();
            MouthControllerComp mouth = parent.GetComp<MouthControllerComp>();

            bool lidIsNormal0 = lid?.FaceType?.defName == LidNormal0DefName;
            bool mouthIsNormal0 = mouth?.FaceType?.defName == MouthNormal0DefName;

            // Preserve an existing valid pair. Old mismatched rolls are normalized below.
            useNormal0 = lidIsNormal0 && mouthIsNormal0;
            initialized = true;
        }

        private void ApplyStoredPair()
        {
            LidControllerComp lid = parent.GetComp<LidControllerComp>();
            MouthControllerComp mouth = parent.GetComp<MouthControllerComp>();
            if (lid == null || mouth == null)
            {
                return;
            }

            LidTypeDef lidNormal0 = DefDatabase<LidTypeDef>.GetNamedSilentFail(LidNormal0DefName);
            MouthTypeDef mouthNormal0 = DefDatabase<MouthTypeDef>.GetNamedSilentFail(MouthNormal0DefName);
            if (lidNormal0 == null || mouthNormal0 == null)
            {
                return;
            }

            if (useNormal0)
            {
                lid.FaceType = lidNormal0;
                mouth.FaceType = mouthNormal0;
                return;
            }

            // Normal0 is not part of the ordinary random pool. This also repairs
            // mismatched combinations created by older versions of the patch.
            if (lid.FaceType == lidNormal0)
            {
                LidTypeDef fallback = DefDatabase<LidTypeDef>.GetNamedSilentFail(LidFallbackDefName);
                if (fallback != null)
                {
                    lid.FaceType = fallback;
                }
            }

            if (mouth.FaceType == mouthNormal0)
            {
                MouthTypeDef fallback = DefDatabase<MouthTypeDef>.GetNamedSilentFail(MouthFallbackDefName);
                if (fallback != null)
                {
                    mouth.FaceType = fallback;
                }
            }
        }

        private void RepairRuntimeMismatch()
        {
            LidControllerComp lid = parent.GetComp<LidControllerComp>();
            MouthControllerComp mouth = parent.GetComp<MouthControllerComp>();
            if (lid == null || mouth == null)
            {
                return;
            }

            bool lidIsNormal0 = lid.FaceType?.defName == LidNormal0DefName;
            bool mouthIsNormal0 = mouth.FaceType?.defName == MouthNormal0DefName;
            if (lidIsNormal0 == mouthIsNormal0)
            {
                useNormal0 = lidIsNormal0;
                return;
            }

            // A single-part reroll or editor change must not leave half of the pair active.
            useNormal0 = false;
            ApplyStoredPair();
            lid.InitializeIfNeed();
            mouth.InitializeIfNeed();
            lid.ReloadIfNeed();
            mouth.ReloadIfNeed();
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
