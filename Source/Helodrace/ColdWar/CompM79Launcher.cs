using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class CompProperties_M79Launcher : CompProperties
    {
        public List<ThingDef> allowedAmmoDefs = new List<ThingDef>();
        public int reloadTicks = 90;
        public int casingDelayTicks = 42;
        public ThingDef casingMoteDef;
        public float buckshotAccuracyTouch = -1f;
        public float buckshotAccuracyShort = -1f;
        public float buckshotAccuracyMedium = -1f;
        public float buckshotAccuracyLong = -1f;

        public CompProperties_M79Launcher()
        {
            compClass = typeof(CompM79Launcher);
        }
    }

    public sealed class CompM79Launcher : ThingComp
    {
        private static readonly HashSet<CompM79Launcher> pendingDelayedEffects = new HashSet<CompM79Launcher>();
        private static readonly List<CompM79Launcher> pendingDelayedEffectsSnapshot = new List<CompM79Launcher>();

        private bool loaded;
        private ThingDef selectedAmmoDef;
        private int casingDueTick = -1;
        private int reloadRequestDueTick = -1;
        private float casingAngle;
        private int lastNoAmmoMessageTick = -9999;

        public CompProperties_M79Launcher Props => (CompProperties_M79Launcher)props;
        public bool Loaded => loaded;
        public ThingDef SelectedAmmoDef => selectedAmmoDef ?? Props.allowedAmmoDefs.FirstOrDefault();
        public ThingDef SelectedProjectileDef => SelectedAmmoDef?.projectileWhenLoaded;
        public bool BuckshotSelected => SelectedAmmoDef?.defName == "HD_40mmM576MP_Round";

        public bool TryGetBuckshotAccuracy(StatDef stat, out float accuracy)
        {
            accuracy = -1f;
            if (!BuckshotSelected)
            {
                return false;
            }

            if (stat == StatDefOf.AccuracyTouch)
            {
                accuracy = Props.buckshotAccuracyTouch;
            }
            else if (stat == StatDefOf.AccuracyShort)
            {
                accuracy = Props.buckshotAccuracyShort;
            }
            else if (stat == StatDefOf.AccuracyMedium)
            {
                accuracy = Props.buckshotAccuracyMedium;
            }
            else if (stat == StatDefOf.AccuracyLong)
            {
                accuracy = Props.buckshotAccuracyLong;
            }

            return accuracy >= 0f;
        }

        public Pawn Wielder => (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref loaded, "loaded", false);
            Scribe_Defs.Look(ref selectedAmmoDef, "selectedAmmoDef");
            Scribe_Values.Look(ref casingDueTick, "casingDueTick", -1);
            Scribe_Values.Look(ref reloadRequestDueTick, "reloadRequestDueTick", -1);
            Scribe_Values.Look(ref casingAngle, "casingAngle");
            Scribe_Values.Look(ref lastNoAmmoMessageTick, "lastNoAmmoMessageTick", -9999);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && !Props.allowedAmmoDefs.Contains(SelectedAmmoDef))
            {
                selectedAmmoDef = Props.allowedAmmoDefs.FirstOrDefault();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit && (casingDueTick >= 0 || reloadRequestDueTick >= 0))
            {
                pendingDelayedEffects.Add(this);
            }

        }

        public override string CompInspectStringExtra()
        {
            return "HD_M79_Inspect".Translate(
                SelectedAmmoDef?.LabelCap ?? "None".Translate(),
                loaded ? "HD_M79_Loaded".Translate() : "HD_M79_Unloaded".Translate());
        }

        public bool HasSelectedAmmo(Pawn pawn)
        {
            return InventoryAmmoUtility.Count(pawn, SelectedAmmoDef) > 0;
        }

        private List<ThingDef> AvailableAmmoDefs(Pawn pawn)
        {
            return Props.allowedAmmoDefs
                .Where(ammoDef => InventoryAmmoUtility.Count(pawn, ammoDef) > 0)
                .ToList();
        }

        public void RequestReloadAfterBlockedShot(Pawn pawn)
        {
            if (loaded || pawn?.Map == null)
            {
                return;
            }

            if (!HasSelectedAmmo(pawn))
            {
                TryStartReloadJob(pawn, true);
                return;
            }

            if (reloadRequestDueTick < 0)
            {
                reloadRequestDueTick = Find.TickManager.TicksGame + 1;
                pendingDelayedEffects.Add(this);
            }
        }

        public bool TryStartReloadJob(Pawn pawn, bool showMessage = true)
        {
            if (loaded || pawn?.Map == null || SelectedAmmoDef == null)
            {
                return false;
            }

            if (!HasSelectedAmmo(pawn))
            {
                if (showMessage && pawn.Faction == Faction.OfPlayer)
                {
                    int tick = Find.TickManager.TicksGame;
                    if (tick - lastNoAmmoMessageTick > 120)
                    {
                        lastNoAmmoMessageTick = tick;
                        Messages.Message("HD_M79_NoAmmo".Translate(SelectedAmmoDef.LabelCap), parent,
                            MessageTypeDefOf.RejectInput, false);
                    }
                }
                return false;
            }

            JobDef reloadDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_ReloadM79FromInventory");
            if (reloadDef == null || pawn.CurJobDef == reloadDef)
            {
                return false;
            }

            Job job = JobMaker.MakeJob(reloadDef, parent);
            return pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public bool TryFinishReload(Pawn pawn)
        {
            if (loaded || !InventoryAmmoUtility.TryConsume(pawn, SelectedAmmoDef))
            {
                return false;
            }

            loaded = true;
            return true;
        }

        public void NotifyShotFired(Verb verb)
        {
            loaded = false;
            Pawn wielder = Wielder;
            if (wielder == null)
            {
                return;
            }

            Vector3 direction = verb.CurrentTarget.CenterVector3 - wielder.DrawPos;
            casingAngle = direction.AngleFlat() + 90f + Rand.Range(-12f, 12f);
            casingDueTick = Find.TickManager.TicksGame + Mathf.Max(1, Props.casingDelayTicks);
            pendingDelayedEffects.Add(this);
        }

        public static void TickPendingDelayedEffects()
        {
            if (pendingDelayedEffects.Count == 0)
            {
                return;
            }

            pendingDelayedEffectsSnapshot.Clear();
            pendingDelayedEffectsSnapshot.AddRange(pendingDelayedEffects);
            foreach (CompM79Launcher launcher in pendingDelayedEffectsSnapshot)
            {
                if (launcher?.parent == null)
                {
                    pendingDelayedEffects.Remove(launcher);
                    continue;
                }

                launcher.TickDelayedEffects();
                if (launcher.casingDueTick < 0 && launcher.reloadRequestDueTick < 0)
                {
                    pendingDelayedEffects.Remove(launcher);
                }
            }
            pendingDelayedEffectsSnapshot.Clear();
        }

        public void TickDelayedEffects()
        {
            if (reloadRequestDueTick >= 0 && Find.TickManager.TicksGame >= reloadRequestDueTick)
            {
                reloadRequestDueTick = -1;
                TryStartReloadJob(Wielder, false);
            }

            if (casingDueTick < 0 || Find.TickManager.TicksGame < casingDueTick)
            {
                return;
            }

            casingDueTick = -1;
            SpawnCasing();
            TryStartReloadJob(Wielder, false);
        }

        private void SpawnCasing()
        {
            Pawn wielder = Wielder;
            if (wielder?.Map == null || Props.casingMoteDef == null)
            {
                return;
            }

            MoteThrown casing = ThingMaker.MakeThing(Props.casingMoteDef) as MoteThrown;
            if (casing == null)
            {
                return;
            }

            Vector3 origin = wielder.DrawPos;
            casing.exactPosition = origin + new Vector3(0f, 0f, 0.18f);
            casing.exactRotation = Rand.Range(0f, 360f);
            casing.rotationRate = Rand.Range(-720f, 720f);
            casing.Scale = 0.8f;
            casing.SetVelocity(casingAngle, Rand.Range(1.5f, 2.2f));
            GenSpawn.Spawn(casing, origin.ToIntVec3(), wielder.Map);
        }

        public void SetSelectedAmmo(ThingDef ammoDef)
        {
            if (ammoDef != null && Props.allowedAmmoDefs.Contains(ammoDef) && ammoDef != SelectedAmmoDef)
            {
                if (loaded && SelectedAmmoDef != null && Wielder?.inventory != null)
                {
                    Thing unloadedRound = ThingMaker.MakeThing(SelectedAmmoDef);
                    if (!Wielder.inventory.innerContainer.TryAdd(unloadedRound))
                    {
                        unloadedRound.Destroy(DestroyMode.Vanish);
                    }
                }

                selectedAmmoDef = ammoDef;
                loaded = false;
            }
        }

        private void ShowAmmoMenu()
        {
            List<FloatMenuOption> options = AvailableAmmoDefs(Wielder).Select(ammoDef =>
                new FloatMenuOption(
                    ammoDef == SelectedAmmoDef
                        ? "HD_M79_AmmoSelected".Translate(ammoDef.LabelCap)
                        : "HD_M79_SelectAmmo".Translate(ammoDef.LabelCap),
                    () => SetSelectedAmmo(ammoDef))).ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Pawn wielder = Wielder;
            if (wielder == null || wielder.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = loaded ? "HD_M79_Loaded".Translate().ToString() : "HD_M79_Reload".Translate().ToString(),
                defaultDesc = "HD_M79_ReloadDesc".Translate(SelectedAmmoDef?.LabelCap ?? "None".Translate()).ToString(),
                icon = SelectedAmmoDef?.uiIcon ?? BaseContent.BadTex,
                Disabled = loaded,
                disabledReason = "HD_M79_AlreadyLoaded".Translate().ToString(),
                action = () => TryStartReloadJob(wielder)
            };

            List<ThingDef> availableAmmoDefs = AvailableAmmoDefs(wielder);
            if (availableAmmoDefs.Count == 0)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "HD_M79_AmmoLabel".Translate(SelectedAmmoDef?.LabelCap ?? "None".Translate()).ToString(),
                defaultDesc = "HD_M79_AmmoDesc".Translate().ToString(),
                icon = SelectedAmmoDef?.uiIcon ?? BaseContent.BadTex,
                action = ShowAmmoMenu
            };
        }
    }

    public sealed class Verb_ShootM79 : Verb_ShootShotgun
    {
        private CompM79Launcher Launcher => EquipmentSource?.TryGetComp<CompM79Launcher>();

        public override ThingDef Projectile => Launcher?.SelectedProjectileDef ?? base.Projectile;

        protected override int PelletCount => Launcher?.BuckshotSelected == true ? 20 : 1;

        protected override bool TryCastShot()
        {
            bool fired = base.TryCastShot();

            if (fired)
            {
                Launcher?.NotifyShotFired(this);
            }
            return fired;
        }
    }

    [HarmonyPatch(typeof(StatExtension), nameof(StatExtension.GetStatValue))]
    public static class Patch_StatExtension_GetStatValue_M79Buckshot
    {
        public static void Postfix(Thing thing, StatDef stat, ref float __result)
        {
            CompM79Launcher launcher = thing?.TryGetComp<CompM79Launcher>();
            if (launcher == null || !launcher.TryGetBuckshotAccuracy(stat, out float configuredAccuracy))
            {
                return;
            }

            // Scale the final value instead of replacing it so weapon quality
            // and any other stat modifiers continue to affect accuracy.
            float baseAccuracy = thing.def.GetStatValueAbstract(stat);
            __result = baseAccuracy > 0f
                ? Mathf.Clamp01(__result * configuredAccuracy / baseAccuracy)
                : Mathf.Clamp01(configuredAccuracy);
        }
    }

    public sealed class JobDriver_ReloadM79FromInventory : JobDriver
    {
        private const TargetIndex WeaponInd = TargetIndex.A;
        private Thing Weapon => job.GetTarget(WeaponInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Weapon?.TryGetComp<CompM79Launcher>() == null);
            this.FailOn(() => Weapon.TryGetComp<CompM79Launcher>()?.Wielder != pawn);
            this.FailOn(() => Weapon.TryGetComp<CompM79Launcher>()?.Loaded == true);
            this.FailOn(() => Weapon.TryGetComp<CompM79Launcher>()?.HasSelectedAmmo(pawn) != true);

            Toil reload = Toils_General.Wait(Weapon.TryGetComp<CompM79Launcher>().Props.reloadTicks);
            reload.WithProgressBarToilDelay(WeaponInd);
            yield return reload;

            yield return new Toil
            {
                initAction = () => Weapon.TryGetComp<CompM79Launcher>()?.TryFinishReload(pawn),
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    public static class M79VerbUtility
    {
        public static bool Prefix(Verb instance, ref bool result)
        {
            CompM79Launcher launcher = instance?.EquipmentSource?.TryGetComp<CompM79Launcher>();
            if (launcher == null || launcher.Loaded)
            {
                return true;
            }

            launcher.RequestReloadAfterBlockedShot(instance.CasterPawn);
            result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_Verb_TryStartCastOn_M79_Primary
    {
        public static bool Prefix(Verb __instance, ref bool __result) => M79VerbUtility.Prefix(__instance, ref __result);
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_Verb_TryStartCastOn_M79_Destination
    {
        public static bool Prefix(Verb __instance, ref bool __result) => M79VerbUtility.Prefix(__instance, ref __result);
    }
}
