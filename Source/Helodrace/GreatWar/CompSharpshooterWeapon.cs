using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace Helodrace
{
    public class CompProperties_SharpshooterWeapon : CompProperties
    {
        public int altVerbIndex = 1; 
        public float altCooldownMultiplier = 1.0f; 
        public float altAccuracyMultiplier = 1.0f; 
        public float moveSpeedOffset = 0.0f;
        public int switchTicks = 120; // Default 2 seconds to switch modes
        public string normalModeLabelKey = "HD_SharpshooterMode_Normal";
        public string altModeLabelKey = "HD_SharpshooterMode_Label";
        public string modeDescriptionKey = "HD_SharpshooterMode_Desc";

        public CompProperties_SharpshooterWeapon()
        {
            this.compClass = typeof(CompSharpshooterWeapon);
        }
    }

    public class CompSharpshooterWeapon : ThingComp
    {
        public CompProperties_SharpshooterWeapon Props => (CompProperties_SharpshooterWeapon)props;
        
        public bool altModeActive = false;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref altModeActive, "altModeActive", false);
        }

        public Pawn Wielder
        {
            get
            {
                if (parent.ParentHolder is Pawn_EquipmentTracker equipmentTracker)
                {
                    return equipmentTracker.pawn;
                }
                if (parent.ParentHolder is Pawn_ApparelTracker apparelTracker)
                {
                    return apparelTracker.pawn;
                }
                if (parent.ParentHolder is Pawn_InventoryTracker inventoryTracker)
                {
                    return inventoryTracker.pawn;
                }
                return parent.ParentHolder as Pawn;
            }
        }

        public bool CanUseSharpshooterMode
        {
            get
            {
                Pawn wielder = Wielder;
                if (wielder == null)
                {
                    return false;
                }
                if (wielder.genes == null)
                {
                    return false;
                }
                GeneDef geneDef = DefDatabase<GeneDef>.GetNamedSilentFail("HD_Gene_Sharpshooter");
                if (geneDef == null)
                {
                    return false;
                }
                return wielder.genes.HasActiveGene(geneDef);
            }
        }

        public Verb AltVerb
        {
            get
            {
                var eq = parent.GetComp<CompEquippable>();
                if (eq != null && eq.AllVerbs.Count > Props.altVerbIndex)
                {
                    return eq.AllVerbs[Props.altVerbIndex];
                }
                return null;
            }
        }

        public void PerformSwitch()
        {
            altModeActive = !altModeActive;
            parent.TryGetComp<CompCEDoubleTapWeapon>()?.ApplyMode();
        }

        public string CurrentModeLabel
        {
            get
            {
                string key = altModeActive ? Props.altModeLabelKey : Props.normalModeLabelKey;
                return key.Translate().Resolve();
            }
        }

        public string ModeDescription => Props.modeDescriptionKey.Translate().Resolve();

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
        }
    }

    public class JobDriver_SwitchSharpshooterMode : JobDriver
    {
        protected Thing Weapon => job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var comp = Weapon?.TryGetComp<CompSharpshooterWeapon>();
            if (comp == null) yield break;

            Toil switchToil = new Toil();
            switchToil.initAction = delegate
            {
                pawn.pather.StopDead();
            };
            switchToil.tickAction = delegate
            {
                // Can add custom effects or sounds here while switching
            };
            switchToil.defaultCompleteMode = ToilCompleteMode.Delay;
            switchToil.defaultDuration = comp.Props.switchTicks;
            switchToil.WithProgressBarToilDelay(TargetIndex.A);
            yield return switchToil;

            yield return new Toil
            {
                initAction = delegate
                {
                    comp.PerformSwitch();
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    [HarmonyPatch(typeof(StatExtension), "GetStatValue")]
    public static class Patch_StatExtension_GetStatValue
    {
        public static void Postfix(Thing thing, StatDef stat, ref float __result)
        {
            if (thing is Pawn pawn)
            {
                if (pawn.equipment?.Primary == null)
                {
                    return;
                }

                var comp = pawn.equipment.Primary.TryGetComp<CompSharpshooterWeapon>();
                if (comp == null || !comp.altModeActive || !comp.CanUseSharpshooterMode)
                {
                    return;
                }

                if (stat == StatDefOf.MoveSpeed && comp.Props.moveSpeedOffset != 0f)
                {
                    __result = UnityEngine.Mathf.Max(0.0f, __result + comp.Props.moveSpeedOffset);
                }
            }
            else
            {
                if (stat == StatDefOf.RangedWeapon_Cooldown)
                {
                    var comp = thing.TryGetComp<CompSharpshooterWeapon>();
                    if (comp != null && comp.altModeActive && comp.CanUseSharpshooterMode)
                    {
                        __result *= comp.Props.altCooldownMultiplier;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(VerbTracker), "get_PrimaryVerb")]
    public static class Patch_VerbTracker_PrimaryVerb
    {
        public static void Postfix(VerbTracker __instance, ref Verb __result)
        {
            if (__instance.directOwner is CompEquippable eq && eq.parent != null)
            {
                CompCEDoubleTapWeapon ceDoubleTap = eq.parent.TryGetComp<CompCEDoubleTapWeapon>();
                if (ceDoubleTap != null)
                {
                    ceDoubleTap.ApplyMode();
                    Verb selectedVerb = ceDoubleTap.CurrentVerb;
                    if (selectedVerb != null)
                    {
                        __result = selectedVerb;
                    }
                    return;
                }

                var comp = eq.parent.TryGetComp<CompSharpshooterWeapon>();
                if (comp != null && comp.altModeActive && comp.CanUseSharpshooterMode && comp.AltVerb != null)
                {
                    __result = comp.AltVerb;
                }
            }
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_ShotReport_HitReportFor
    {
        public static void Postfix(Thing caster, Verb verb, LocalTargetInfo target, ref ShotReport __result)
        {
            if (caster is Pawn pawn && pawn.equipment?.Primary != null)
            {
                var comp = pawn.equipment.Primary.TryGetComp<CompSharpshooterWeapon>();
                if (comp != null && comp.altModeActive && comp.CanUseSharpshooterMode)
                {
                    SharpshooterAccuracyReport.Register(__result, comp.Props.altAccuracyMultiplier);
                }
            }
        }
    }

    public static class SharpshooterAccuracyReport
    {
        private const int CacheLifetimeTicks = 120;
        private static readonly System.Reflection.FieldInfo TargetField = AccessTools.Field(typeof(ShotReport), "target");
        private static readonly System.Reflection.FieldInfo DistanceField = AccessTools.Field(typeof(ShotReport), "distance");
        private static readonly System.Reflection.FieldInfo CoversOverallBlockChanceField = AccessTools.Field(typeof(ShotReport), "coversOverallBlockChance");
        private static readonly System.Reflection.FieldInfo FactorFromShooterAndDistField = AccessTools.Field(typeof(ShotReport), "factorFromShooterAndDist");
        private static readonly System.Reflection.FieldInfo FactorFromEquipmentField = AccessTools.Field(typeof(ShotReport), "factorFromEquipment");
        private static readonly System.Reflection.FieldInfo FactorFromTargetSizeField = AccessTools.Field(typeof(ShotReport), "factorFromTargetSize");
        private static readonly System.Reflection.FieldInfo FactorFromWeatherField = AccessTools.Field(typeof(ShotReport), "factorFromWeather");
        private static readonly System.Reflection.FieldInfo FactorFromCoveringGasField = AccessTools.Field(typeof(ShotReport), "factorFromCoveringGas");
        private static readonly System.Reflection.FieldInfo ShootLineField = AccessTools.Field(typeof(ShotReport), "shootLine");
        private static readonly Dictionary<string, CachedMultiplier> MultipliersByReport = new Dictionary<string, CachedMultiplier>();

        private struct CachedMultiplier
        {
            public float multiplier;
            public int tick;
        }

        public static void Register(ShotReport report, float multiplier)
        {
            if (UnityEngine.Mathf.Approximately(multiplier, 1.0f) || multiplier <= 0f)
            {
                return;
            }

            string key = GetKey(report);
            if (key == null)
            {
                return;
            }

            PruneOldEntries();
            MultipliersByReport[key] = new CachedMultiplier
            {
                multiplier = multiplier,
                tick = CurrentTick
            };
        }

        public static bool TryGetMultiplier(ShotReport report, out float multiplier)
        {
            string key = GetKey(report);
            if (key != null && MultipliersByReport.TryGetValue(key, out CachedMultiplier cached))
            {
                multiplier = cached.multiplier;
                return true;
            }

            multiplier = 1.0f;
            return false;
        }

        private static string GetKey(ShotReport report)
        {
            if (TargetField == null || DistanceField == null || FactorFromShooterAndDistField == null)
            {
                return null;
            }

            object boxedReport = report;
            return string.Join("|",
                StableHash(TargetField.GetValue(boxedReport)),
                StableHash(ShootLineField?.GetValue(boxedReport)),
                RoundedHash(DistanceField.GetValue(boxedReport)),
                RoundedHash(CoversOverallBlockChanceField?.GetValue(boxedReport)),
                RoundedHash(FactorFromShooterAndDistField.GetValue(boxedReport)),
                RoundedHash(FactorFromEquipmentField?.GetValue(boxedReport)),
                RoundedHash(FactorFromTargetSizeField?.GetValue(boxedReport)),
                RoundedHash(FactorFromWeatherField?.GetValue(boxedReport)),
                RoundedHash(FactorFromCoveringGasField?.GetValue(boxedReport)));
        }

        private static int StableHash(object value)
        {
            return value?.GetHashCode() ?? 0;
        }

        private static int RoundedHash(object value)
        {
            return value is float number ? UnityEngine.Mathf.RoundToInt(number * 10000f) : 0;
        }

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;

        private static void PruneOldEntries()
        {
            int currentTick = CurrentTick;
            if (MultipliersByReport.Count < 64)
            {
                return;
            }

            List<string> expiredKeys = new List<string>();
            foreach (KeyValuePair<string, CachedMultiplier> entry in MultipliersByReport)
            {
                if (currentTick - entry.Value.tick > CacheLifetimeTicks)
                {
                    expiredKeys.Add(entry.Key);
                }
            }

            foreach (string expiredKey in expiredKeys)
            {
                MultipliersByReport.Remove(expiredKey);
            }
        }
    }

    [HarmonyPatch(typeof(ShotReport), "get_AimOnTargetChance_StandardTarget")]
    public static class Patch_ShotReport_AimOnTargetChance_StandardTarget
    {
        public static void Postfix(ShotReport __instance, ref float __result)
        {
            if (SharpshooterAccuracyReport.TryGetMultiplier(__instance, out float multiplier))
            {
                __result = UnityEngine.Mathf.Clamp01(__result * multiplier);
            }
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.GetTextReadout))]
    public static class Patch_ShotReport_GetTextReadout
    {
        public static void Postfix(ShotReport __instance, ref string __result)
        {
            if (SharpshooterAccuracyReport.TryGetMultiplier(__instance, out float multiplier))
            {
                __result += "\n" + "HD_SharpshooterMode_AccuracyFactor".Translate(multiplier.ToStringPercent()).Resolve();
            }
        }
    }

    [HarmonyPatch(typeof(CompEquippable), "get_PrimaryVerb")]
    public static class Patch_CompEquippable_PrimaryVerb
    {
        public static void Postfix(CompEquippable __instance, ref Verb __result)
        {
            if (__instance.parent != null)
            {
                CompCEDoubleTapWeapon ceDoubleTap = __instance.parent.TryGetComp<CompCEDoubleTapWeapon>();
                if (ceDoubleTap != null)
                {
                    ceDoubleTap.ApplyMode();
                    Verb selectedVerb = ceDoubleTap.CurrentVerb;
                    if (selectedVerb != null)
                    {
                        __result = selectedVerb;
                    }
                    return;
                }

                var comp = __instance.parent.TryGetComp<CompSharpshooterWeapon>();
                if (comp != null && comp.altModeActive && comp.CanUseSharpshooterMode && comp.AltVerb != null)
                {
                    __result = comp.AltVerb;
                }
            }
        }
    }

    [HarmonyPatch(typeof(CompEquippable), nameof(CompEquippable.GetVerbsCommands))]
    public static class Patch_CompEquippable_GetVerbsCommands
    {
        public static void Postfix(CompEquippable __instance, ref IEnumerable<Command> __result)
        {
            if (__instance.parent != null)
            {
                var comp = __instance.parent.TryGetComp<CompSharpshooterWeapon>();
                if (comp != null)
                {
                    var updated = new List<Command>();
                    CompCEDoubleTapWeapon ceDoubleTap = __instance.parent.TryGetComp<CompCEDoubleTapWeapon>();
                    ceDoubleTap?.ApplyMode();
                    Verb selectedVerb = ceDoubleTap?.CurrentVerb;
                    bool shouldSwap = selectedVerb != null || (comp.altModeActive && comp.CanUseSharpshooterMode && comp.AltVerb != null);
                    
                    foreach (var command in __result)
                    {
                        if (shouldSwap && command is Command_VerbTarget cmdVerb)
                        {
                            cmdVerb.verb = selectedVerb ?? comp.AltVerb;
                        }
                        updated.Add(command);
                    }

                    Pawn wielder = comp.Wielder;
                    bool isPlayerPawn = wielder != null && wielder.Faction != null && wielder.Faction.IsPlayer;

                    if (isPlayerPawn && comp.CanUseSharpshooterMode && (ceDoubleTap?.ModeVerb != null || comp.AltVerb != null))
                    {
                        var iconTex = ContentFinder<UnityEngine.Texture2D>.Get("Icon/HD_Sharpshooter", false);
                        if (iconTex == null)
                        {
                            iconTex = BaseContent.BadTex;
                        }

                        var switchCommand = new Command_Action
                        {
                            defaultLabel = "HD_SharpshooterMode_Command".Translate(comp.CurrentModeLabel).Resolve(),
                            defaultDesc = comp.ModeDescription,
                            icon = iconTex, 
                            action = () => 
                            {
                                Pawn pawn = comp.Wielder;
                                if (pawn != null)
                                {
                                    Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_SwitchSharpshooterMode"), pawn, comp.parent);
                                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                                }
                            }
                        };
                        updated.Add(switchCommand);
                    }
                    
                    __result = updated;
                }
            }
        }
    }

    public class CompProperties_CEDoubleTapWeapon : CompProperties
    {
        public int doubleTapVerbIndex = 0;
        public int normalVerbIndex = 1;
        public int normalBurstShotCount = 1;
        public int doubleTapBurstShotCount = 2;
        public int ticksBetweenBurstShots = 3;

        public CompProperties_CEDoubleTapWeapon()
        {
            compClass = typeof(CompCEDoubleTapWeapon);
        }
    }

    public class CompCEDoubleTapWeapon : ThingComp
    {
        private static readonly FieldInfo CachedBurstShotCountField = AccessTools.Field(typeof(Verb), "cachedBurstShotCount");
        private static readonly FieldInfo CachedTicksBetweenBurstShotsField = AccessTools.Field(typeof(Verb), "cachedTicksBetweenBurstShots");

        public CompProperties_CEDoubleTapWeapon Props => (CompProperties_CEDoubleTapWeapon)props;

        public override void PostPostMake()
        {
            base.PostPostMake();
            ApplyMode();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            ApplyMode();
        }

        public Verb CurrentVerb => DoubleTapActive ? DoubleTapVerb ?? NormalVerb : NormalVerb ?? DoubleTapVerb;

        public Verb ModeVerb => DoubleTapVerb;

        private bool DoubleTapActive
        {
            get
            {
                CompSharpshooterWeapon sharpshooter = parent.TryGetComp<CompSharpshooterWeapon>();
                return sharpshooter != null && sharpshooter.altModeActive && sharpshooter.CanUseSharpshooterMode;
            }
        }

        public void ApplyMode()
        {
            Verb doubleTapVerb = DoubleTapVerb;
            if (doubleTapVerb?.verbProps == null)
            {
                return;
            }

            doubleTapVerb.verbProps.burstShotCount = Props.doubleTapBurstShotCount;
            doubleTapVerb.verbProps.ticksBetweenBurstShots = Props.ticksBetweenBurstShots;
            ResetVerbCaches(doubleTapVerb);

            Verb normalVerb = NormalVerb;
            if (normalVerb?.verbProps != null)
            {
                normalVerb.verbProps.burstShotCount = Props.normalBurstShotCount;
                normalVerb.verbProps.ticksBetweenBurstShots = Props.ticksBetweenBurstShots;
                ResetVerbCaches(normalVerb);
            }

            ApplyCEFireModesBurstCount(Props.doubleTapBurstShotCount);
        }

        private Verb DoubleTapVerb => VerbAtIndex(Props.doubleTapVerbIndex);

        private Verb NormalVerb => VerbAtIndex(Props.normalVerbIndex);

        private Verb VerbAtIndex(int index)
        {
            CompEquippable eq = parent.GetComp<CompEquippable>();
            if (eq != null && eq.AllVerbs.Count > index)
            {
                return eq.AllVerbs[index];
            }
            return null;
        }

        private static void ResetVerbCaches(Verb verb)
        {
            CachedBurstShotCountField?.SetValue(verb, null);
            CachedTicksBetweenBurstShotsField?.SetValue(verb, null);
        }

        private void ApplyCEFireModesBurstCount(int burstShotCount)
        {
            FieldInfo propsField = AccessTools.Field(typeof(ThingComp), "props");
            foreach (ThingComp comp in parent.AllComps)
            {
                if (comp?.GetType().FullName != "CombatExtended.CompFireModes")
                {
                    continue;
                }

                object compProps = propsField?.GetValue(comp);
                FieldInfo burstField = compProps == null ? null : AccessTools.Field(compProps.GetType(), "aimedBurstShotCount");
                burstField?.SetValue(compProps, burstShotCount);
            }
        }
    }

}
