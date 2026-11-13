using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace Helodrace
{
    // The mode is a preference. Preparation is a separate, passive clock;
    // it never owns a job, stops a path or changes movement speed.
    public sealed class SharpshooterPreparationClock : IExposable
    {
        public int StartedAt = -1;
        public IntVec3 Position = IntVec3.Invalid;
        public void Reset() { StartedAt = -1; Position = IntVec3.Invalid; }
        public void Update(bool selected, bool moving, IntVec3 position, int tick)
        {
            if (!selected || moving) { Reset(); return; }
            if (StartedAt < 0 || Position.IsValid && Position != position) StartedAt = tick;
            Position = position;
        }
        public int Remaining(int tick, int duration) => duration <= 0 ? 0
            : StartedAt < 0 ? duration : System.Math.Max(0, duration - System.Math.Max(0, tick - StartedAt));
        public void ExposeData()
        {
            Scribe_Values.Look(ref StartedAt, "startedAt", -1);
            Scribe_Values.Look(ref Position, "position", IntVec3.Invalid);
        }
    }
    public static class SharpshooterModePolicy
    {
        public static bool CanSwitch(bool cooldown, bool bursting) => !cooldown && !bursting;
    }
    public class CompProperties_SharpshooterWeapon : CompProperties
    {
        public int altVerbIndex = 1;
        public float altCooldownMultiplier = 1.0f;
        public float altAccuracyMultiplier = 1.0f;
        // Zero means immediate fire. Positive values require stationary setup.
        public int stationaryPreparationTicks;
        public List<string> aiModeTags;
        public bool DefensiveAI { get; private set; }
        public override void ResolveReferences(ThingDef parentDef)
        {
            base.ResolveReferences(parentDef);
            DefensiveAI = aiModeTags?.Contains("Defensive") == true;
        }
        public string normalModeLabelKey = "HD_SharpshooterMode_Normal";
        public string altModeLabelKey = "HD_SharpshooterMode_Label";
        public string modeDescriptionKey = "HD_SharpshooterMode_Desc";
        public CompProperties_SharpshooterWeapon() { compClass = typeof(CompSharpshooterWeapon); }
    }
    public class CompSharpshooterWeapon : ThingComp
    {
        public CompProperties_SharpshooterWeapon Props => (CompProperties_SharpshooterWeapon)props;
        public bool altModeActive, aiModeOwned;
        private bool aiReleasePending;
        public SharpshooterPreparationClock Preparation = new SharpshooterPreparationClock();
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref altModeActive, "altModeActive");
            Scribe_Values.Look(ref aiModeOwned, "aiModeOwned");
            Scribe_Values.Look(ref aiReleasePending, "aiReleasePending");
            Scribe_Deep.Look(ref Preparation, "stationaryPreparation");
            // A missing/null deep node clears the field initializer. Every
            // loaded weapon still needs a clock before drop/down notifications.
            if(Preparation==null) Preparation=new SharpshooterPreparationClock();
        }
        // Inventory guns cannot grant a mode or borrow the carrier's clock.
        public Pawn Wielder => (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;
        public bool CanUseSharpshooterMode
        {
            get
            {
                Pawn pawn = Wielder;
                if (pawn == null || pawn.equipment?.Primary != parent) return false;
                if (BTXUtility.IsHelod(pawn)) return true;
                GeneDef gene = DefDatabase<GeneDef>.GetNamedSilentFail("HD_Gene_Sharpshooter");
                return gene != null && pawn.genes?.HasActiveGene(gene) == true;
            }
        }
        public Verb AltVerb
        {
            get
            {
                var eq = parent.GetComp<CompEquippable>();
                return eq != null && Props.altVerbIndex >= 0 && eq.AllVerbs.Count > Props.altVerbIndex
                    ? eq.AllVerbs[Props.altVerbIndex] : null;
            }
        }
        public Verb SelectedVerb
        {
            get
            {
                var doubleTap = parent.TryGetComp<CompCEDoubleTapWeapon>();
                if (doubleTap != null) return doubleTap.CurrentVerb;
                if (altModeActive && CanUseSharpshooterMode && AltVerb != null) return AltVerb;
                var eq = parent.GetComp<CompEquippable>();
                if (eq != null) foreach (Verb verb in eq.AllVerbs) if (verb.verbProps.isPrimary) return verb;
                return null;
            }
        }
        // Auxiliary weapons on the same equipment keep their own firing verb.
        public bool IsModeVerb(Verb verb)
        {
            if (verb == null || verb.IsMeleeAttack) return false;
            var doubleTap = parent.TryGetComp<CompCEDoubleTapWeapon>();
            if (doubleTap != null) return doubleTap.IsModeVerb(verb);
            if (verb == AltVerb) return true;
            var eq = parent.GetComp<CompEquippable>();
            if (eq != null)
                foreach (Verb candidate in eq.AllVerbs)
                    if (candidate.verbProps.isPrimary) return verb == candidate;
            return false;
        }
        private bool CycleBusy(Pawn pawn)
        {
            bool bursting = false;
            var eq = parent.GetComp<CompEquippable>();
            if (eq != null) foreach (Verb verb in eq.AllVerbs) if (verb.Bursting) { bursting = true; break; }
            return !SharpshooterModePolicy.CanSwitch(pawn?.stances?.curStance is Stance_Cooldown, bursting);
        }
        public bool CanSwitchMode => Wielder != null && !CycleBusy(Wielder) && CanUseSharpshooterMode
            && (AltVerb != null || parent.TryGetComp<CompCEDoubleTapWeapon>()?.ModeVerb != null);
        public bool TrySetMode(bool enabled, bool ai = false)
        {
            if (altModeActive == enabled) return true;
            if (!CanSwitchMode) return false;
            ApplySelection(enabled, ai);
            return true;
        }
        public bool PerformSwitch() => TrySetMode(!altModeActive);
        private void ApplySelection(bool enabled, bool ai)
        {
            CancelWeaponWarmup(Wielder);
            altModeActive = enabled; aiModeOwned = ai && enabled; aiReleasePending = false;
            Preparation.Reset();
            if (Wielder != null) TickEquipped(Wielder, GenTicks.TicksGame);
            parent.TryGetComp<CompCEDoubleTapWeapon>()?.ApplyMode();
        }
        private void CancelWeaponWarmup(Pawn pawn)
        {
            if (pawn?.stances?.curStance is Stance_Warmup warmup && IsModeVerb(warmup.verb))
            {
                pawn.stances.CancelBusyStanceSoft();
                warmup.verb.Reset();
            }
        }
        public bool AltModeEffective => ReadyToFire && altModeActive && CanUseSharpshooterMode;
        public bool ReadyToFire
        {
            get
            {
                if (!altModeActive || !CanUseSharpshooterMode || Props.stationaryPreparationTicks <= 0) return true;
                Pawn pawn = Wielder;
                TickEquipped(pawn, GenTicks.TicksGame);
                return !altModeActive || Preparation.Remaining(GenTicks.TicksGame, Props.stationaryPreparationTicks) == 0;
            }
        }
        public void TickEquipped(Pawn pawn, int tick)
        {
            if (aiReleasePending && !CycleBusy(pawn)) { ApplySelection(false, false); return; }
            if (!altModeActive || Props.stationaryPreparationTicks <= 0) return;
            bool moving = pawn.pather?.MovingNow == true || pawn.Downed || pawn.Dead;
            Preparation.Update(true, moving, pawn.Position, tick);
        }
        public void NotifyMovementStarted(Pawn pawn)
        {
            if (!altModeActive || Props.stationaryPreparationTicks <= 0) return;
            Preparation.Reset(); CancelWeaponWarmup(pawn);
        }
        public void ForgetAIOwnership() { aiModeOwned = aiReleasePending = false; }
        public void ReleaseAIMode()
        {
            if (!aiModeOwned) return;
            if (Wielder == null) { altModeActive = false; ForgetAIOwnership(); Preparation.Reset(); return; }
            if (!TrySetMode(false)) aiReleasePending = true;
        }
        public override void Notify_Unequipped(Pawn pawn)
        {
            Preparation.Reset(); ReleaseAIMode();
            base.Notify_Unequipped(pawn);
        }
        public override void Notify_Equipped(Pawn pawn)
        {
            Preparation.Reset();
            base.Notify_Equipped(pawn);
        }
        public string CurrentModeLabel
        {
            get
            {
                string label = (altModeActive ? Props.altModeLabelKey : Props.normalModeLabelKey).Translate().Resolve();
                if (altModeActive && CanUseSharpshooterMode && !ReadyToFire)
                    label += " " + "HD_SharpshooterMode_Preparing".Translate((Preparation.Remaining(GenTicks.TicksGame,
                        Props.stationaryPreparationTicks) / 60f).ToString("0.0")).Resolve();
                return label;
            }
        }
        public string ModeDescription => Props.modeDescriptionKey.Translate().Resolve() + "\n"
            + "HD_SharpshooterMode_InstantSwitch".Translate().Resolve()
            + (Props.stationaryPreparationTicks > 0 ? "\n" + "HD_SharpshooterMode_StationarySetup"
                .Translate((Props.stationaryPreparationTicks / 60f).ToString("0.0")).Resolve() : "");
    }
    [HarmonyPatch(typeof(StatExtension), "GetStatValue")]
    public static class Patch_StatExtension_GetStatValue
    {
        public static void Postfix(Thing thing, StatDef stat, ref float __result)
        {
            if (stat != StatDefOf.RangedWeapon_Cooldown) return;
            var comp = thing.TryGetComp<CompSharpshooterWeapon>();
            if (comp?.AltModeEffective == true) __result *= comp.Props.altCooldownMultiplier;
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
                    ceDoubleTap.EnsureModeApplied();
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
                if (comp != null && comp.AltModeEffective && verb == comp.SelectedVerb)
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
            if (Waylith.Math.Mathf.Approximately(multiplier, 1.0f) || multiplier <= 0f)
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
            return value is float number ? Waylith.Math.Mathf.RoundToInt(number * 10000f) : 0;
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
                __result = Waylith.Math.Mathf.Clamp01(__result * multiplier);
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
                    ceDoubleTap.EnsureModeApplied();
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
                    ceDoubleTap?.EnsureModeApplied();
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
                        var iconTex = ContentFinder<UnityEngine.Texture2D>.Get("Icons/HD_Sharpshooter", false);
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
                                comp.PerformSwitch();
                            }
                        };
                        if (!comp.CanSwitchMode) switchCommand.Disable("HD_SharpshooterMode_CooldownLocked".Translate().Resolve());
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

        public bool IsModeVerb(Verb verb) => verb != null && (verb == NormalVerb || verb == DoubleTapVerb);

        private bool DoubleTapActive
        {
            get
            {
                CompSharpshooterWeapon sharpshooter = parent.TryGetComp<CompSharpshooterWeapon>();
                return sharpshooter != null && sharpshooter.altModeActive && sharpshooter.CanUseSharpshooterMode;
            }
        }

        // The PrimaryVerb getters run many times per tick. The written values never change, so
        // they only need applying once the verbs exist; mode toggles still call ApplyMode.
        private bool modeApplied;

        public void EnsureModeApplied()
        {
            if (!modeApplied)
            {
                ApplyMode();
            }
        }

        public void ApplyMode()
        {
            Verb doubleTapVerb = DoubleTapVerb;
            if (doubleTapVerb?.verbProps == null)
            {
                return;
            }

            modeApplied = true;

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

        private static readonly Dictionary<System.Type, FieldInfo> aimedBurstFields = new Dictionary<System.Type, FieldInfo>();

        private void ApplyCEFireModesBurstCount(int burstShotCount)
        {
            foreach (ThingComp comp in parent.AllComps)
            {
                if (comp?.GetType().FullName != "CombatExtended.CompFireModes")
                {
                    continue;
                }

                object compProps = comp.props;
                if (compProps == null)
                {
                    continue;
                }

                System.Type propsType = compProps.GetType();
                if (!aimedBurstFields.TryGetValue(propsType, out FieldInfo burstField))
                {
                    burstField = AccessTools.Field(propsType, "aimedBurstShotCount");
                    aimedBurstFields[propsType] = burstField;
                }

                burstField?.SetValue(compProps, burstShotCount);
            }
        }
    }

}
