using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    public sealed class CompProperties_ModularUnderbarrelLauncher : CompProperties
    {
        public List<ThingDef> allowedAmmoDefs = new List<ThingDef>();
        public int reloadTicks = 90;

        public CompProperties_ModularUnderbarrelLauncher()
        {
            compClass = typeof(CompModularUnderbarrelLauncher);
        }
    }

    // Stored on the detachable launcher, so its chamber follows the part between rifles.
    public sealed class CompModularUnderbarrelLauncher : ThingComp
    {
        private bool loaded;
        private ThingDef selectedAmmoDef;

        public CompProperties_ModularUnderbarrelLauncher Props =>
            (CompProperties_ModularUnderbarrelLauncher)props;
        public bool Loaded => loaded;
        public ThingDef SelectedAmmoDef => selectedAmmoDef ?? Props.allowedAmmoDefs.FirstOrDefault();
        public ThingDef ProjectileDef => SelectedAmmoDef?.projectileWhenLoaded;
        public bool BuckshotSelected => SelectedAmmoDef?.defName == "HD_40mmM576MP_Round";

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref loaded, "underbarrelLoaded");
            Scribe_Defs.Look(ref selectedAmmoDef, "underbarrelSelectedAmmo");
            if (Scribe.mode == LoadSaveMode.PostLoadInit
                && !Props.allowedAmmoDefs.Contains(selectedAmmoDef))
                selectedAmmoDef = Props.allowedAmmoDefs.FirstOrDefault();
        }

        public override string CompInspectStringExtra()
        {
            return "HD_M203_Inspect".Translate(
                SelectedAmmoDef?.LabelCap ?? "None".Translate(),
                loaded ? "HD_M79_Loaded".Translate() : "HD_M79_Unloaded".Translate());
        }

        public bool HasAmmo(Pawn pawn) => InventoryAmmoUtility.Count(pawn, SelectedAmmoDef) > 0;

        public bool TryReload(Pawn pawn)
        {
            if (loaded || !InventoryAmmoUtility.TryConsume(pawn, SelectedAmmoDef)) return false;
            loaded = true;
            return true;
        }

        public void NotifyFired() => loaded = false;

        private void SelectAmmo(ThingDef ammoDef, Pawn pawn)
        {
            if (ammoDef == null || !Props.allowedAmmoDefs.Contains(ammoDef)
                || ammoDef == SelectedAmmoDef) return;
            if (loaded)
            {
                Thing round = ThingMaker.MakeThing(SelectedAmmoDef);
                if (pawn?.inventory?.innerContainer?.TryAdd(round) != true)
                {
                    round.Destroy(DestroyMode.Vanish);
                    return;
                }
            }
            selectedAmmoDef = ammoDef;
            loaded = false;
        }

        public IEnumerable<Gizmo> GetEquippedGizmos(Pawn pawn, Thing weapon)
        {
            if (pawn?.Faction != Faction.OfPlayer || weapon == null) yield break;
            CompEquippable equippable = weapon.TryGetComp<CompEquippable>();
            Verb_ShootModularUnderbarrel verb = equippable?.AllVerbs
                .OfType<Verb_ShootModularUnderbarrel>().FirstOrDefault();
            if (verb == null) yield break;

            yield return new Command_Action
            {
                defaultLabel = "HD_M203_Fire".Translate().ToString(),
                defaultDesc = "HD_M203_FireDesc".Translate().ToString(),
                icon = SelectedAmmoDef?.uiIcon ?? TexCommand.Attack,
                Disabled = !loaded || !pawn.Drafted || pawn.Downed,
                disabledReason = !loaded ? "HD_M79_Unloaded".Translate().ToString()
                    : "HD_M203_DraftRequired".Translate().ToString(),
                action = () => Find.Targeter.BeginTargeting(new TargetingParameters
                {
                    canTargetPawns = true,
                    canTargetBuildings = true,
                    canTargetItems = true,
                    canTargetLocations = true,
                    validator = target => target.IsValid && pawn.Map != null
                        && target.Cell.InBounds(pawn.Map)
                        && verb.CanHitTarget(target.Thing != null
                            ? new LocalTargetInfo(target.Thing)
                            : new LocalTargetInfo(target.Cell))
                }, target =>
                {
                    if (loaded && pawn.equipment?.Primary == weapon)
                        verb.TryStartCastOn(target, false, false, false, false);
                })
            };

            yield return new Command_Action
            {
                defaultLabel = "HD_M203_Reload".Translate().ToString(),
                defaultDesc = "HD_M79_ReloadDesc".Translate(
                    SelectedAmmoDef?.LabelCap ?? "None".Translate()).ToString(),
                icon = SelectedAmmoDef?.uiIcon ?? BaseContent.BadTex,
                Disabled = loaded || !HasAmmo(pawn),
                disabledReason = loaded ? "HD_M79_AlreadyLoaded".Translate().ToString()
                    : "HD_M79_NoAmmo".Translate(
                        SelectedAmmoDef?.LabelCap ?? "None".Translate()).ToString(),
                action = () =>
                {
                    JobDef reload = DefDatabase<JobDef>.GetNamedSilentFail("HD_ReloadModularUnderbarrel");
                    if (reload != null) pawn.jobs.TryTakeOrderedJob(
                        JobMaker.MakeJob(reload, weapon), JobTag.Misc);
                }
            };

            List<ThingDef> available = Props.allowedAmmoDefs
                .Where(def => InventoryAmmoUtility.Count(pawn, def) > 0).ToList();
            if (available.Count == 0) yield break;
            yield return new Command_Action
            {
                defaultLabel = "HD_M79_AmmoLabel".Translate(
                    SelectedAmmoDef?.LabelCap ?? "None".Translate()).ToString(),
                defaultDesc = "HD_M79_AmmoDesc".Translate().ToString(),
                icon = SelectedAmmoDef?.uiIcon ?? BaseContent.BadTex,
                action = () => Find.WindowStack.Add(new FloatMenu(available.Select(def =>
                    new FloatMenuOption(def == SelectedAmmoDef
                        ? "HD_M79_AmmoSelected".Translate(def.LabelCap)
                        : "HD_M79_SelectAmmo".Translate(def.LabelCap),
                        () => SelectAmmo(def, pawn))).ToList()))
            };
        }
    }

    public static class ModularUnderbarrelLauncherUtility
    {
        public static CompModularUnderbarrelLauncher Installed(Thing weapon)
        {
            CompModularWeaponNode root = weapon?.TryGetComp<CompModularWeaponNode>();
            if (root?.Props.isAssemblyRoot != true) return null;
            foreach (ModularRenderNode node in root.RenderSnapshot())
            {
                CompModularUnderbarrelLauncher launcher = node?.thing
                    ?.TryGetComp<CompModularUnderbarrelLauncher>();
                if (launcher == null || node.comp == null) continue;
                bool barrel = false;
                bool trigger = false;
                for (int i = 0; i < node.comp.ChildCount; i++)
                {
                    barrel |= node.comp.SocketIdAt(i) == "launcher_barrel";
                    trigger |= node.comp.SocketIdAt(i) == "launcher_trigger";
                }
                if (barrel && trigger) return launcher;
            }
            return null;
        }
    }

    public sealed class Verb_ShootModularUnderbarrel : Verb_ShootShotgun
    {
        private CompModularUnderbarrelLauncher Launcher =>
            ModularUnderbarrelLauncherUtility.Installed(EquipmentSource);

        public override ThingDef Projectile => Launcher?.ProjectileDef ?? base.Projectile;
        protected override int PelletCount => Launcher?.BuckshotSelected == true ? 20 : 1;

        protected override bool TryCastShot()
        {
            CompModularUnderbarrelLauncher launcher = Launcher;
            if (launcher?.Loaded != true) return false;
            bool fired = base.TryCastShot();
            if (fired) launcher.NotifyFired();
            return fired;
        }
    }

    public sealed class JobDriver_ReloadModularUnderbarrel : JobDriver
    {
        private Thing Weapon => job.targetA.Thing;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Weapon == null || pawn.equipment?.Primary != Weapon
                || ModularUnderbarrelLauncherUtility.Installed(Weapon) == null);
            this.FailOn(() => ModularUnderbarrelLauncherUtility.Installed(Weapon)?.Loaded == true);
            this.FailOn(() => ModularUnderbarrelLauncherUtility.Installed(Weapon)?.HasAmmo(pawn) != true);
            Toil reload = Toils_General.Wait(
                ModularUnderbarrelLauncherUtility.Installed(Weapon).Props.reloadTicks);
            reload.WithProgressBarToilDelay(TargetIndex.A);
            yield return reload;
            yield return new Toil
            {
                initAction = () => ModularUnderbarrelLauncherUtility.Installed(Weapon)?.TryReload(pawn),
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_ModularUnderbarrel
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            if (values != null)
                foreach (Gizmo gizmo in values) yield return gizmo;
            Thing weapon = __instance?.equipment?.Primary;
            CompModularUnderbarrelLauncher launcher =
                ModularUnderbarrelLauncherUtility.Installed(weapon);
            if (launcher != null)
                foreach (Gizmo gizmo in launcher.GetEquippedGizmos(__instance, weapon))
                    yield return gizmo;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[]
    {
        typeof(LocalTargetInfo), typeof(bool), typeof(bool),
        typeof(bool), typeof(bool)
    })]
    public static class Patch_Verb_UnderbarrelAvailablePrimary
    {
        [HarmonyPrefix]
        public static bool Prefix(Verb __instance, ref bool __result)
            => ModularUnderbarrelFireUtility.CanStart(__instance, ref __result);
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[]
    {
        typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool),
        typeof(bool), typeof(bool), typeof(bool)
    })]
    public static class Patch_Verb_UnderbarrelAvailable
    {
        [HarmonyPrefix]
        public static bool Prefix(Verb __instance, ref bool __result)
            => ModularUnderbarrelFireUtility.CanStart(__instance, ref __result);
    }

    internal static class ModularUnderbarrelFireUtility
    {
        public static bool CanStart(Verb verb, ref bool result)
        {
            if (!(verb is Verb_ShootModularUnderbarrel)) return true;
            CompModularUnderbarrelLauncher launcher =
                ModularUnderbarrelLauncherUtility.Installed(verb.EquipmentSource);
            if (launcher?.Loaded == true) return true;
            result = false;
            return false;
        }
    }
}
