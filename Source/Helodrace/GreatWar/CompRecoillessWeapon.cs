using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public class AmmoCaliberExtension : DefModExtension
    {
        public string caliber;
    }

    public class CompProperties_RecoillessWeapon : CompProperties
    {
        public ThingDef ammoDef;
        public List<ThingDef> allowedAmmoDefs;
        public int reloadTicks = 180;
        public int selfReloadTicks = -1;
        public int crewReloadTicks = -1;

        public CompProperties_RecoillessWeapon()
        {
            compClass = typeof(CompRecoillessWeapon);
        }
    }

    public enum RecoillessReloadMode
    {
        Self,
        Crew
    }

    public class CompRecoillessWeapon : ThingComp
    {
        private bool loaded;
        private int lastUnloadedMessageTick = -9999;
        private RecoillessReloadMode reloadMode = RecoillessReloadMode.Self;
        private Pawn assignedLoader;
        private ThingDef selectedAmmoDef;
        private bool fallBackToSelfWhenCrewAmmoDepleted;

        public CompProperties_RecoillessWeapon Props => (CompProperties_RecoillessWeapon)props;

        public bool Loaded => loaded;
        public RecoillessReloadMode ReloadMode => reloadMode;
        public Pawn AssignedLoader => assignedLoader;
        public ThingDef SelectedAmmoDef => selectedAmmoDef ?? DefaultAmmoDef;
        public ThingDef SelectedProjectileDef => SelectedAmmoDef?.projectileWhenLoaded;

        public ThingDef DefaultAmmoDef => Props.ammoDef ?? AllowedAmmoDefs.FirstOrDefault();
        public int SelfReloadTicks => Props.selfReloadTicks > 0 ? Props.selfReloadTicks : Props.reloadTicks;
        public int CrewReloadTicks => Props.crewReloadTicks > 0 ? Props.crewReloadTicks : Props.reloadTicks;

        public IEnumerable<ThingDef> AllowedAmmoDefs
        {
            get
            {
                if (Props.allowedAmmoDefs != null && Props.allowedAmmoDefs.Count > 0)
                {
                    return Props.allowedAmmoDefs;
                }

                return Props.ammoDef != null ? new[] { Props.ammoDef } : Enumerable.Empty<ThingDef>();
            }
        }

        public Pawn Wielder
        {
            get
            {
                if (parent.ParentHolder is Pawn_EquipmentTracker equipmentTracker)
                {
                    return equipmentTracker.pawn;
                }
                return null;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref loaded, "loaded", false);
            Scribe_Values.Look(ref lastUnloadedMessageTick, "lastUnloadedMessageTick", -9999);
            Scribe_Values.Look(ref reloadMode, "reloadMode", RecoillessReloadMode.Self);
            Scribe_References.Look(ref assignedLoader, "assignedLoader");
            Scribe_Defs.Look(ref selectedAmmoDef, "selectedAmmoDef");
            Scribe_Values.Look(ref fallBackToSelfWhenCrewAmmoDepleted, "fallBackToSelfWhenCrewAmmoDepleted", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && !AllowedAmmoDefs.Contains(SelectedAmmoDef))
            {
                selectedAmmoDef = DefaultAmmoDef;
            }
        }

        public override string CompInspectStringExtra()
        {
            return loaded ? "HD_RecoillessWeapon_Loaded".Translate().ToString() : "HD_RecoillessWeapon_Unloaded".Translate().ToString();
        }

        public void Load()
        {
            loaded = true;
        }

        public void ConsumeLoadedRound()
        {
            loaded = false;
        }

        public bool TryStartReloadJob(Pawn pawn)
        {
            if (pawn?.Map == null || SelectedAmmoDef == null)
            {
                return false;
            }

            if (reloadMode == RecoillessReloadMode.Crew)
            {
                TryFallbackToSelfReload(pawn);
            }

            if (reloadMode == RecoillessReloadMode.Crew)
            {
                return TryStartCrewReloadJob(pawn);
            }

            if (TryStartSelfReloadJob(pawn))
            {
                return true;
            }

            if (pawn.Faction == Faction.OfPlayer)
            {
                Messages.Message("HD_RecoillessWeapon_NoReloadRound_Inventory".Translate(), parent, MessageTypeDefOf.RejectInput, false);
            }
            return false;
        }

        public bool CanAutoReload(Pawn pawn)
        {
            if (pawn?.Map == null || SelectedAmmoDef == null)
            {
                return false;
            }

            if (reloadMode == RecoillessReloadMode.Crew)
            {
                TryFallbackToSelfReload(pawn);
            }

            if (reloadMode == RecoillessReloadMode.Crew)
            {
                return AssignedLoaderWithAmmoFor(pawn) != null;
            }

            return HasInventoryAmmo(pawn);
        }

        public bool TryStartCrewReloadJob(Pawn weaponUser)
        {
            Pawn loader = AssignedLoaderReadyFor(weaponUser);
            if (loader == null)
            {
                if (weaponUser.Faction == Faction.OfPlayer)
                {
                    Messages.Message("HD_RecoillessWeapon_NoAssignedLoader_Inventory".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }

            if (loader.Position.DistanceToSquared(weaponUser.Position) > 2)
            {
                return TryStartLoaderStandbyJob();
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_AssistReloadRecoillessWeapon");
            if (jobDef == null)
            {
                Log.ErrorOnce("Helodrace: HD_AssistReloadRecoillessWeapon JobDef is missing.", 97160202);
                return false;
            }

            Job job = JobMaker.MakeJob(jobDef, weaponUser, parent);
            return loader.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private bool TryStartSelfReloadJob(Pawn pawn)
        {
            if (!HasInventoryAmmo(pawn))
            {
                return false;
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_ReloadRecoillessWeaponFromBag");
            if (jobDef == null)
            {
                Log.ErrorOnce("Helodrace: HD_ReloadRecoillessWeaponFromBag JobDef is missing.", 97160204);
                return false;
            }

            Job job = JobMaker.MakeJob(jobDef, parent);
            return pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public bool HasInventoryAmmo(Pawn pawn)
        {
            return InventoryAmmoUtility.Count(pawn, SelectedAmmoDef) > 0;
        }

        public bool TryConsumeInventoryAmmo(Pawn pawn)
        {
            return InventoryAmmoUtility.TryConsume(pawn, SelectedAmmoDef);
        }

        public bool IsAssignedLoader(Pawn pawn)
        {
            return reloadMode == RecoillessReloadMode.Crew && assignedLoader == pawn;
        }

        public void ConfigureCrew(Pawn loader, bool fallBackToSelfAfterCrewAmmo)
        {
            assignedLoader = loader;
            reloadMode = loader == null ? RecoillessReloadMode.Self : RecoillessReloadMode.Crew;
            fallBackToSelfWhenCrewAmmoDepleted = fallBackToSelfAfterCrewAmmo;
            if (loader != null && Wielder?.Spawned == true)
            {
                TryStartLoaderStandbyJob();
            }
        }

        private void TryFallbackToSelfReload(Pawn weaponUser)
        {
            if (!fallBackToSelfWhenCrewAmmoDepleted
                || reloadMode != RecoillessReloadMode.Crew
                || AssignedLoaderWithAmmoFor(weaponUser) != null
                || !HasInventoryAmmo(weaponUser))
            {
                return;
            }

            reloadMode = RecoillessReloadMode.Self;
            fallBackToSelfWhenCrewAmmoDepleted = false;
        }

        public void SetSelectedAmmo(ThingDef ammoDef)
        {
            if (ammoDef != null && AllowedAmmoDefs.Contains(ammoDef))
            {
                selectedAmmoDef = ammoDef;
            }
        }

        private void ToggleReloadMode()
        {
            reloadMode = reloadMode == RecoillessReloadMode.Self ? RecoillessReloadMode.Crew : RecoillessReloadMode.Self;
            if (reloadMode == RecoillessReloadMode.Crew)
            {
                TryStartLoaderStandbyJob();
            }
        }

        private void BeginAssignLoader()
        {
            Pawn wielder = Wielder;
            if (wielder?.Map == null)
            {
                return;
            }

            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetLocations = false,
                validator = target =>
                {
                    Pawn pawn = target.Thing as Pawn;
                    return pawn != null
                        && pawn != wielder
                        && pawn.Spawned
                        && !pawn.Dead
                        && !pawn.Downed
                        && pawn.Faction == wielder.Faction
                        && pawn.Map == wielder.Map
                        && pawn.CanReach(wielder, PathEndMode.Touch, Danger.Deadly);
                }
            }, target =>
            {
                assignedLoader = target.Thing as Pawn;
                reloadMode = RecoillessReloadMode.Crew;
                TryStartLoaderStandbyJob();
            });
        }

        public bool TryStartLoaderStandbyJob()
        {
            Pawn wielder = Wielder;
            Pawn loader = assignedLoader;
            if (wielder?.Map == null || loader?.Map != wielder.Map || loader.Dead || loader.Downed)
            {
                return false;
            }

            if (!TryFindLoaderStandbyCell(loader, wielder, out IntVec3 cell))
            {
                return false;
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_StandbyRecoillessLoader");
            if (jobDef == null)
            {
                Log.ErrorOnce("Helodrace: HD_StandbyRecoillessLoader JobDef is missing.", 97160205);
                return false;
            }

            Job job = JobMaker.MakeJob(jobDef, wielder, parent, cell);
            return loader.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static bool TryFindLoaderStandbyCell(Pawn loader, Pawn wielder, out IntVec3 cell)
        {
            foreach (IntVec3 candidate in GenAdj.CellsAdjacent8Way(wielder).OrderBy(c => c.DistanceToSquared(loader.Position)))
            {
                if (candidate.InBounds(wielder.Map)
                    && candidate.Standable(wielder.Map)
                    && !candidate.IsForbidden(loader)
                    && loader.CanReserveAndReach(candidate, PathEndMode.OnCell, Danger.Deadly))
                {
                    cell = candidate;
                    return true;
                }
            }

            cell = IntVec3.Invalid;
            return false;
        }

        private Pawn AssignedLoaderReadyFor(Pawn weaponUser)
        {
            if (assignedLoader == null
                || assignedLoader == weaponUser
                || assignedLoader.Dead
                || assignedLoader.Downed
                || assignedLoader.Map != weaponUser.Map
                || !assignedLoader.CanReach(weaponUser, PathEndMode.Touch, Danger.Deadly)
                || !HasInventoryAmmo(assignedLoader))
            {
                return null;
            }

            return assignedLoader;
        }

        private Pawn AssignedLoaderWithAmmoFor(Pawn weaponUser)
        {
            if (assignedLoader == null
                || assignedLoader == weaponUser
                || assignedLoader.Dead
                || assignedLoader.Downed
                || assignedLoader.Map != weaponUser.Map
                || !HasInventoryAmmo(assignedLoader))
            {
                return null;
            }

            return assignedLoader;
        }

        public void ShowAmmoFloatMenu()
        {
            List<FloatMenuOption> options = AllowedAmmoDefs
                .Select(ammoDef => new FloatMenuOption(
                    ammoDef == SelectedAmmoDef
                        ? "HD_RecoillessWeapon_AmmoSelected".Translate(ammoDef.label)
                        : "HD_RecoillessWeapon_SelectAmmo".Translate(ammoDef.label),
                    () => SetSelectedAmmo(ammoDef)))
                .ToList();

            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn wielder = Wielder;
            if (wielder == null || wielder.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = loaded ? "HD_RecoillessWeapon_Loaded".Translate().ToString() : "HD_RecoillessWeapon_Reload_Label".Translate().ToString(),
                defaultDesc = loaded ? "HD_RecoillessWeapon_AlreadyLoaded".Translate().ToString() : "HD_RecoillessWeapon_Reload_Desc_Inventory".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("Weapon/GreatWar/Ammo/HD_m6a3HEAT", false) ?? BaseContent.BadTex,
                Disabled = loaded,
                disabledReason = "HD_RecoillessWeapon_AlreadyLoaded".Translate().ToString(),
                action = () => TryStartReloadJob(wielder)
            };

            yield return new Command_Action
            {
                defaultLabel = reloadMode == RecoillessReloadMode.Self
                    ? "HD_RecoillessWeapon_ModeSelf".Translate().ToString()
                    : "HD_RecoillessWeapon_ModeCrew".Translate().ToString(),
                defaultDesc = "HD_RecoillessWeapon_ModeDesc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", false) ?? BaseContent.BadTex,
                action = ToggleReloadMode
            };

            if (reloadMode == RecoillessReloadMode.Crew)
            {
                yield return new Command_Action
                {
                    defaultLabel = assignedLoader == null
                        ? "HD_RecoillessWeapon_AssignLoader".Translate().ToString()
                        : "HD_RecoillessWeapon_AssignedLoader".Translate(assignedLoader.LabelShort).ToString(),
                    defaultDesc = "HD_RecoillessWeapon_AssignLoaderDesc_Inventory".Translate().ToString(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/ForPrisoners", false) ?? BaseContent.BadTex,
                    action = BeginAssignLoader
                };

                if (assignedLoader != null)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "HD_RecoillessWeapon_LoaderStandby".Translate().ToString(),
                        defaultDesc = "HD_RecoillessWeapon_LoaderStandbyDesc".Translate().ToString(),
                        icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt", false) ?? BaseContent.BadTex,
                        action = () => TryStartLoaderStandbyJob()
                    };
                }
            }

            if (reloadMode == RecoillessReloadMode.Self)
            {
                yield return new Command_Action
                {
                    defaultLabel = "HD_RecoillessWeapon_AmmoLabel".Translate(SelectedAmmoDef?.label ?? "None".Translate()).ToString(),
                    defaultDesc = "HD_RecoillessWeapon_AmmoDesc".Translate().ToString(),
                    icon = ContentFinder<Texture2D>.Get("Weapon/GreatWar/Ammo/HD_m6a3HEAT", false) ?? BaseContent.BadTex,
                    action = ShowAmmoFloatMenu
                };
            }
        }

        public void NotifyReloadUnavailable(Pawn pawn)
        {
            if (pawn?.Faction != Faction.OfPlayer)
            {
                return;
            }

            int ticksGame = Find.TickManager.TicksGame;
            if (ticksGame - lastUnloadedMessageTick < 120)
            {
                return;
            }

            lastUnloadedMessageTick = ticksGame;
            string key = reloadMode == RecoillessReloadMode.Crew
                ? "HD_RecoillessWeapon_NoAssignedLoader_Inventory"
                : "HD_RecoillessWeapon_NoReloadRound_Inventory";
            Messages.Message(key.Translate(), parent, MessageTypeDefOf.RejectInput, false);
        }
    }

    public class JobDriver_ReloadRecoillessWeaponFromBag : JobDriver
    {
        private const TargetIndex WeaponInd = TargetIndex.A;

        protected Thing Weapon => job.GetTarget(WeaponInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Weapon?.TryGetComp<CompRecoillessWeapon>() == null);
            this.FailOn(() => Weapon?.TryGetComp<CompRecoillessWeapon>()?.Wielder != pawn);
            this.FailOn(() => Weapon?.TryGetComp<CompRecoillessWeapon>()?.Loaded == true);
            this.FailOn(() => Weapon.TryGetComp<CompRecoillessWeapon>()?.HasInventoryAmmo(pawn) != true);

            Toil reload = Toils_General.Wait(Weapon.TryGetComp<CompRecoillessWeapon>().SelfReloadTicks);
            reload.WithProgressBarToilDelay(WeaponInd);
            yield return reload;

            yield return new Toil
            {
                initAction = delegate
                {
                    CompRecoillessWeapon weaponComp = Weapon.TryGetComp<CompRecoillessWeapon>();
                    if (weaponComp != null && !weaponComp.Loaded && weaponComp.TryConsumeInventoryAmmo(pawn))
                    {
                        weaponComp.Load();
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    public class CompProperties_M6RocketBag : CompProperties
    {
        public ThingDef rocketDef;
        public List<ThingDef> allowedAmmoDefs;
        public int maxStoredRockets = 4;
        public int maxStoredRounds = -1;

        public CompProperties_M6RocketBag()
        {
            compClass = typeof(CompM6RocketBag);
        }
    }

    public class CompM6RocketBag : ThingComp
    {
        private Dictionary<ThingDef, int> desiredAmmo = new Dictionary<ThingDef, int>();
        // Kept only to migrate ammunition stored by saves made with the old pouch system.
        private Dictionary<ThingDef, int> storedAmmo = new Dictionary<ThingDef, int>();
        private int storedRockets;

        public CompProperties_M6RocketBag Props => (CompProperties_M6RocketBag)props;

        public ThingDef RocketDef => Props.rocketDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("HD_Rocket_M6A3HEAT");

        public IEnumerable<ThingDef> AllowedAmmoDefs => InventoryAmmoUtility.AllAmmoDefs;

        public Pawn Wearer
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

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref desiredAmmo, "desiredAmmo", LookMode.Def, LookMode.Value);
            Scribe_Collections.Look(ref storedAmmo, "storedAmmo", LookMode.Def, LookMode.Value);
            Scribe_Values.Look(ref storedRockets, "storedRockets", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (storedAmmo == null)
                {
                    storedAmmo = new Dictionary<ThingDef, int>();
                }

                if (desiredAmmo == null)
                {
                    desiredAmmo = new Dictionary<ThingDef, int>();
                }

                if (storedRockets > 0 && RocketDef != null && !storedAmmo.ContainsKey(RocketDef))
                {
                    storedAmmo[RocketDef] = storedRockets;
                }

                TryMigrateLegacyStoredAmmo();
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            TryMigrateLegacyStoredAmmo();
        }

        public override string CompInspectStringExtra()
        {
            int configured = desiredAmmo?.Count(entry => entry.Value > 0) ?? 0;
            float mass = desiredAmmo?.Where(entry => entry.Value > 0)
                .Sum(entry => entry.Key.GetStatValueAbstract(StatDefOf.Mass) * entry.Value) ?? 0f;
            return "HD_WeaponLoadout_Inspect".Translate(configured, mass.ToStringMass());
        }

        public int DesiredCountFor(ThingDef ammoDef)
        {
            return ammoDef != null && desiredAmmo != null && desiredAmmo.TryGetValue(ammoDef, out int count)
                ? Mathf.Max(0, count)
                : 0;
        }

        public void SetDesiredCount(ThingDef ammoDef, int count)
        {
            if (ammoDef == null || !InventoryAmmoUtility.IsAmmo(ammoDef))
            {
                return;
            }

            count = Mathf.Clamp(count, 0, 9999);
            if (count == 0)
            {
                desiredAmmo.Remove(ammoDef);
            }
            else
            {
                desiredAmmo[ammoDef] = count;
            }
        }

        private void TryMigrateLegacyStoredAmmo()
        {
            Pawn wearer = Wearer;
            if (wearer?.inventory == null || storedAmmo == null || storedAmmo.All(entry => entry.Value <= 0))
            {
                return;
            }

            foreach (KeyValuePair<ThingDef, int> entry in storedAmmo.ToList())
            {
                if (entry.Key == null || entry.Value <= 0)
                {
                    continue;
                }

                Thing ammo = ThingMaker.MakeThing(entry.Key);
                ammo.stackCount = entry.Value;
                if (wearer.inventory.innerContainer.TryAdd(ammo))
                {
                    storedAmmo[entry.Key] = 0;
                }
                else
                {
                    ammo.Destroy(DestroyMode.Vanish);
                }
            }
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

            yield return new Command_Action
            {
                defaultLabel = "HD_WeaponLoadout_Gizmo_Label".Translate().ToString(),
                defaultDesc = "HD_WeaponLoadout_Gizmo_Desc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("Weapon/GreatWar/Ammo/HD_m6a3HEAT", false) ?? BaseContent.BadTex,
                action = () => Find.WindowStack.Add(new Dialog_WeaponLoadout(this))
            };
        }
    }

    public class JobDriver_StandbyRecoillessLoader : JobDriver
    {
        private const TargetIndex WeaponUserInd = TargetIndex.A;
        private const TargetIndex WeaponInd = TargetIndex.B;
        private const TargetIndex StandbyCellInd = TargetIndex.C;

        protected Pawn WeaponUser => job.GetTarget(WeaponUserInd).Pawn;
        protected Thing Weapon => job.GetTarget(WeaponInd).Thing;
        protected IntVec3 StandbyCell => job.GetTarget(StandbyCellInd).Cell;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(StandbyCell, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => WeaponUser == null || WeaponUser.Dead || WeaponUser.Map != pawn.Map);
            this.FailOn(() => Weapon?.TryGetComp<CompRecoillessWeapon>() == null);
            this.FailOn(() => Weapon.TryGetComp<CompRecoillessWeapon>()?.IsAssignedLoader(pawn) != true);

            yield return Toils_Goto.GotoCell(StandbyCellInd, PathEndMode.OnCell);

            Toil wait = new Toil
            {
                tickAction = delegate
                {
                    CompRecoillessWeapon weaponComp = Weapon?.TryGetComp<CompRecoillessWeapon>();
                    if (WeaponUser == null
                        || WeaponUser.Dead
                        || WeaponUser.Map != pawn.Map
                        || pawn.Position.DistanceToSquared(WeaponUser.Position) > 2)
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        return;
                    }

                    if (weaponComp != null
                        && !weaponComp.Loaded
                        && weaponComp.HasInventoryAmmo(pawn))
                    {
                        weaponComp.TryStartCrewReloadJob(WeaponUser);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Never
            };
            yield return wait;
        }
    }

    public class JobDriver_AssistReloadRecoillessWeapon : JobDriver
    {
        private const TargetIndex WeaponUserInd = TargetIndex.A;
        private const TargetIndex WeaponInd = TargetIndex.B;

        protected Pawn WeaponUser => job.GetTarget(WeaponUserInd).Pawn;
        protected Thing Weapon => job.GetTarget(WeaponInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => WeaponUser == null || WeaponUser.Dead || WeaponUser.Map != pawn.Map);
            this.FailOn(() => Weapon?.TryGetComp<CompRecoillessWeapon>() == null);
            this.FailOn(() => Weapon?.TryGetComp<CompRecoillessWeapon>()?.Wielder != WeaponUser);
            this.FailOn(() => Weapon?.TryGetComp<CompRecoillessWeapon>()?.Loaded == true);
            this.FailOn(() => Weapon.TryGetComp<CompRecoillessWeapon>()?.HasInventoryAmmo(pawn) != true);

            yield return Toils_Goto.GotoThing(WeaponUserInd, PathEndMode.Touch);

            Toil reload = Toils_General.Wait(Weapon.TryGetComp<CompRecoillessWeapon>().CrewReloadTicks);
            reload.WithProgressBarToilDelay(WeaponUserInd);
            reload.FailOnCannotTouch(WeaponUserInd, PathEndMode.Touch);
            yield return reload;

            yield return new Toil
            {
                initAction = delegate
                {
                    CompRecoillessWeapon weaponComp = Weapon.TryGetComp<CompRecoillessWeapon>();
                    if (weaponComp != null && !weaponComp.Loaded && weaponComp.TryConsumeInventoryAmmo(pawn))
                    {
                        weaponComp.Load();
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

    }

    public static class InventoryAmmoUtility
    {
        private static List<ThingDef> cachedAmmoDefs;

        public static IEnumerable<ThingDef> AllAmmoDefs
        {
            get
            {
                if (cachedAmmoDefs == null)
                {
                    cachedAmmoDefs = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(IsAmmo)
                        .OrderBy(def => def.label)
                        .ToList();
                }

                return cachedAmmoDefs;
            }
        }

        public static bool IsAmmo(ThingDef def)
        {
            return def != null
                && (def.projectileWhenLoaded != null
                    || def.thingCategories?.Any(category => category.GetModExtension<AmmoCaliberExtension>() != null) == true)
                && def.EverHaulable
                && def.stackLimit > 0;
        }

        public static int Count(Pawn pawn, ThingDef ammoDef)
        {
            return pawn?.inventory?.Count(ammoDef) ?? 0;
        }

        public static bool TryConsume(Pawn pawn, ThingDef ammoDef)
        {
            Thing ammo = pawn?.inventory?.innerContainer?.FirstOrDefault(thing => thing.def == ammoDef);
            if (ammo == null)
            {
                return false;
            }

            ammo.SplitOff(1).Destroy(DestroyMode.Vanish);
            return true;
        }

        public static CompM6RocketBag EquippedLoadout(Pawn pawn)
        {
            return pawn?.apparel?.WornApparel?
                .Select(apparel => apparel.TryGetComp<CompM6RocketBag>())
                .FirstOrDefault(comp => comp != null);
        }
    }

    [HarmonyPatch(typeof(JobGiver_TakeForInventoryStock), "TryGiveJob")]
    public static class Patch_JobGiver_TakeForInventoryStock_WeaponLoadout
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result != null || pawn?.Map == null || pawn.inventory == null)
            {
                return;
            }

            CompM6RocketBag loadout = InventoryAmmoUtility.EquippedLoadout(pawn);
            if (loadout == null)
            {
                return;
            }

            foreach (ThingDef ammoDef in loadout.AllowedAmmoDefs)
            {
                int deficit = loadout.DesiredCountFor(ammoDef) - InventoryAmmoUtility.Count(pawn, ammoDef);
                if (deficit <= 0)
                {
                    continue;
                }

                Thing ammo = GenClosest.ClosestThingReachable(
                    pawn.Position,
                    pawn.Map,
                    ThingRequest.ForDef(ammoDef),
                    PathEndMode.Touch,
                    TraverseParms.For(pawn, Danger.Deadly),
                    9999f,
                    thing => thing.Spawned
                        && !thing.IsForbidden(pawn)
                        && pawn.CanReserve(thing, 1, 1));
                if (ammo == null)
                {
                    continue;
                }

                int capacityCount = MassUtility.CountToPickUpUntilOverEncumbered(pawn, ammo);
                if (capacityCount <= 0)
                {
                    continue;
                }

                __result = JobMaker.MakeJob(JobDefOf.TakeInventory, ammo);
                __result.count = Mathf.Min(deficit, ammo.stackCount, capacityCount);
                return;
            }
        }
    }

    [HarmonyPatch(typeof(JobGiver_DropUnusedInventory), "TryGiveJob")]
    public static class Patch_JobGiver_DropUnusedInventory_WeaponLoadout
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            Thing target = __result?.targetA.Thing;
            CompM6RocketBag loadout = InventoryAmmoUtility.EquippedLoadout(pawn);
            if (target == null || loadout == null || !InventoryAmmoUtility.IsAmmo(target.def))
            {
                return;
            }

            int excess = InventoryAmmoUtility.Count(pawn, target.def) - loadout.DesiredCountFor(target.def);
            if (excess <= 0)
            {
                __result = null;
                return;
            }

            __result.count = Mathf.Min(excess, target.stackCount);
        }
    }

    public static class RecoillessVerbPatches
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            CompRecoillessWeapon comp = __instance.EquipmentSource?.TryGetComp<CompRecoillessWeapon>();
            if (comp == null)
            {
                return true;
            }

            if (comp.Loaded)
            {
                return true;
            }

            Pawn pawn = __instance.CasterPawn;
            if (pawn != null)
            {
                RecoillessReloadScheduler.Schedule(pawn, comp.parent);
            }

            __result = false;
            return false;
        }

        public static void Postfix(Verb __instance, bool __result)
        {
            if (!__result)
            {
                return;
            }

            __instance.EquipmentSource?.TryGetComp<CompRecoillessWeapon>()?.ConsumeLoadedRound();
        }
    }

    public static class RecoillessReloadScheduler
    {
        private struct ScheduledReload
        {
            public Thing weapon;
            public int tick;
        }

        private static readonly Dictionary<Pawn, ScheduledReload> scheduledReloads = new Dictionary<Pawn, ScheduledReload>();

        public static void Schedule(Pawn pawn, Thing weapon)
        {
            if (pawn == null || weapon == null)
            {
                return;
            }

            scheduledReloads[pawn] = new ScheduledReload
            {
                weapon = weapon,
                tick = Find.TickManager.TicksGame + 1
            };
        }

        public static void TryRun(Pawn pawn)
        {
            if (pawn == null || !scheduledReloads.TryGetValue(pawn, out ScheduledReload scheduled))
            {
                return;
            }

            if (Find.TickManager.TicksGame < scheduled.tick)
            {
                return;
            }

            scheduledReloads.Remove(pawn);

            CompRecoillessWeapon comp = scheduled.weapon.TryGetComp<CompRecoillessWeapon>();
            if (comp == null || comp.Loaded || comp.Wielder != pawn)
            {
                return;
            }

            if (comp.CanAutoReload(pawn))
            {
                comp.TryStartReloadJob(pawn);
                return;
            }

            comp.NotifyReloadUnavailable(pawn);
            if (pawn.CurJobDef == JobDefOf.AttackStatic)
            {
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    public static class Patch_Pawn_Tick_RecoillessReloadScheduler
    {
        public static void Prefix(Pawn __instance)
        {
            RecoillessReloadScheduler.TryRun(__instance);
            __instance?.equipment?.Primary?.TryGetComp<CompM79Launcher>()?.TickDelayedEffects();
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_RecoillessWeapon
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            CompRecoillessWeapon comp = __instance?.equipment?.Primary?.TryGetComp<CompRecoillessWeapon>();
            if (comp != null)
            {
                __result = __result.Concat(comp.CompGetGizmosExtra());
            }

            CompM79Launcher m79 = __instance?.equipment?.Primary?.TryGetComp<CompM79Launcher>();
            if (m79 != null)
            {
                __result = __result.Concat(m79.CompGetGizmosExtra());
            }

            __result = __result.Concat(InventoryGrenadeUtility.GetGizmos(__instance));
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_Verb_TryStartCastOn_RecoillessWeapon_Primary
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            return RecoillessVerbPatches.Prefix(__instance, ref __result);
        }

        public static void Postfix(Verb __instance, bool __result)
        {
            RecoillessVerbPatches.Postfix(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_Verb_TryStartCastOn_RecoillessWeapon_WithDestination
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            return RecoillessVerbPatches.Prefix(__instance, ref __result);
        }

        public static void Postfix(Verb __instance, bool __result)
        {
            RecoillessVerbPatches.Postfix(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "get_Projectile")]
    public static class Patch_VerbLaunchProjectile_Projectile_RecoillessWeapon
    {
        public static void Postfix(Verb_LaunchProjectile __instance, ref ThingDef __result)
        {
            ThingDef projectileDef = __instance.EquipmentSource?.TryGetComp<CompRecoillessWeapon>()?.SelectedProjectileDef;
            if (projectileDef != null)
            {
                __result = projectileDef;
            }
        }
    }
}
