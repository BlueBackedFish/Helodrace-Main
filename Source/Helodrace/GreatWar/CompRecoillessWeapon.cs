using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
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
                return TryStartCrewReloadJob(pawn);
            }

            if (TryStartSelfBagReloadJob(pawn))
            {
                return true;
            }

            if (pawn.Faction == Faction.OfPlayer)
            {
                Messages.Message("HD_RecoillessWeapon_NoReloadRound".Translate(), parent, MessageTypeDefOf.RejectInput, false);
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
                return AssignedLoaderWithAmmoFor(pawn) != null;
            }

            return FindLoadedAmmoBag(pawn) != null;
        }

        public bool TryStartCrewReloadJob(Pawn weaponUser)
        {
            Pawn loader = AssignedLoaderReadyFor(weaponUser);
            if (loader == null)
            {
                if (weaponUser.Faction == Faction.OfPlayer)
                {
                    Messages.Message("HD_RecoillessWeapon_NoAssignedLoader".Translate(), parent, MessageTypeDefOf.RejectInput, false);
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

        private bool TryStartSelfBagReloadJob(Pawn pawn)
        {
            if (FindLoadedAmmoBag(pawn) == null)
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

        public CompM6RocketBag FindLoadedAmmoBag(Pawn pawn)
        {
            return pawn?.apparel?.WornApparel?
                .Select(apparel => apparel.TryGetComp<CompM6RocketBag>())
                .FirstOrDefault(comp => comp != null && comp.StoredCountFor(SelectedAmmoDef) > 0);
        }

        public bool IsAssignedLoader(Pawn pawn)
        {
            return reloadMode == RecoillessReloadMode.Crew && assignedLoader == pawn;
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
                        && pawn.CanReach(wielder, PathEndMode.Touch, Danger.Deadly)
                        && pawn.apparel?.WornApparel?.Any(apparel => apparel.TryGetComp<CompM6RocketBag>() != null) == true;
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
                || FindLoadedAmmoBag(assignedLoader) == null)
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
                || FindLoadedAmmoBag(assignedLoader) == null)
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
                defaultDesc = loaded ? "HD_RecoillessWeapon_AlreadyLoaded".Translate().ToString() : "HD_RecoillessWeapon_Reload_Desc".Translate().ToString(),
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
                    defaultDesc = "HD_RecoillessWeapon_AssignLoaderDesc".Translate().ToString(),
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
                ? "HD_RecoillessWeapon_NoAssignedLoader"
                : "HD_RecoillessWeapon_NoReloadRound";
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
            this.FailOn(() => Weapon.TryGetComp<CompRecoillessWeapon>()?.FindLoadedAmmoBag(pawn) == null);

            Toil reload = Toils_General.Wait(Weapon.TryGetComp<CompRecoillessWeapon>().SelfReloadTicks);
            reload.WithProgressBarToilDelay(WeaponInd);
            yield return reload;

            yield return new Toil
            {
                initAction = delegate
                {
                    CompRecoillessWeapon weaponComp = Weapon.TryGetComp<CompRecoillessWeapon>();
                    CompM6RocketBag bag = weaponComp?.FindLoadedAmmoBag(pawn);
                    if (bag != null && weaponComp != null && !weaponComp.Loaded && bag.TryConsumeAmmo(weaponComp.SelectedAmmoDef))
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
        private Dictionary<ThingDef, int> storedAmmo = new Dictionary<ThingDef, int>();
        private int storedRockets;

        public CompProperties_M6RocketBag Props => (CompProperties_M6RocketBag)props;

        public int StoredRockets => TotalStoredRounds;

        public ThingDef RocketDef => Props.rocketDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("HD_Rocket_M6A3HEAT");

        private int MaxStoredRounds => Props.maxStoredRounds > 0 ? Props.maxStoredRounds : Props.maxStoredRockets;
        public int MaxStoredRoundsForUI => MaxStoredRounds;
        public int TotalStoredRoundsForUI => TotalStoredRounds;

        public IEnumerable<ThingDef> AllowedAmmoDefs
        {
            get
            {
                if (Props.allowedAmmoDefs != null && Props.allowedAmmoDefs.Count > 0)
                {
                    return Props.allowedAmmoDefs;
                }

                ThingDef fallback = RocketDef;
                return fallback != null ? new[] { fallback } : Enumerable.Empty<ThingDef>();
            }
        }

        private int TotalStoredRounds => storedAmmo.Values.Sum();

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
            Scribe_Collections.Look(ref storedAmmo, "storedAmmo", LookMode.Def, LookMode.Value);
            Scribe_Values.Look(ref storedRockets, "storedRockets", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (storedAmmo == null)
                {
                    storedAmmo = new Dictionary<ThingDef, int>();
                }

                if (storedRockets > 0 && RocketDef != null && !storedAmmo.ContainsKey(RocketDef))
                {
                    storedAmmo[RocketDef] = storedRockets;
                }
            }
        }

        public override string CompInspectStringExtra()
        {
            string contents = TotalStoredRounds > 0
                ? string.Join(", ", storedAmmo.Where(entry => entry.Value > 0).Select(entry => $"{entry.Key.label}: {entry.Value}"))
                : "HD_M6RocketBag_Empty".Translate().ToString();
            return "HD_M6RocketBag_Contents".Translate(contents, TotalStoredRounds, MaxStoredRounds);
        }

        public bool TryStartLoadAmmoJobFromMap(ThingDef ammoDef)
        {
            Thing ammo = FindClosestLoadableAmmo(ammoDef);
            if (ammo == null)
            {
                Messages.Message("HD_M6RocketBag_NoAmmoOnMap".Translate(ammoDef.label), parent, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            return TryStartLoadAmmoJob(ammo);
        }

        private Thing FindClosestLoadableAmmo(ThingDef ammoDef)
        {
            Pawn wearer = Wearer;
            if (wearer?.Map == null || !CanAcceptAmmo(ammoDef))
            {
                return null;
            }

            return GenClosest.ClosestThingReachable(
                wearer.Position,
                wearer.Map,
                ThingRequest.ForDef(ammoDef),
                PathEndMode.Touch,
                TraverseParms.For(wearer, Danger.Deadly),
                9999f,
                thing => thing.Spawned
                    && thing.Map == wearer.Map
                    && !thing.IsForbidden(wearer)
                    && wearer.CanReserve(thing, 1, 1));
        }

        public bool TryStartLoadAmmoJob(Thing ammo)
        {
            Pawn wearer = Wearer;
            if (wearer == null || !CanLoadAmmo(ammo))
            {
                return false;
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_LoadM6RocketBag");
            if (jobDef == null)
            {
                Log.ErrorOnce("Helodrace: HD_LoadM6RocketBag JobDef is missing.", 97160203);
                return false;
            }

            Job job = JobMaker.MakeJob(jobDef, ammo, parent);
            return wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public bool CanLoadRocket(Thing rocket)
        {
            return CanLoadAmmo(rocket);
        }

        public bool CanLoadAmmo(Thing ammo)
        {
            Pawn wearer = Wearer;
            return wearer != null
                && ammo != null
                && CanAcceptAmmo(ammo.def)
                && ammo.Spawned
                && ammo.Map == wearer.Map
                && !ammo.IsForbidden(wearer)
                && wearer.CanReserveAndReach(ammo, PathEndMode.Touch, Danger.Deadly);
        }

        public bool CanAcceptAmmo(ThingDef ammoDef)
        {
            return ammoDef != null
                && AllowedAmmoDefs.Contains(ammoDef)
                && TotalStoredRounds < MaxStoredRounds;
        }

        public bool TryLoadRocket(Thing rocket)
        {
            return TryLoadAmmo(rocket);
        }

        public bool TryLoadAmmo(Thing ammo)
        {
            if (!CanLoadAmmo(ammo))
            {
                return false;
            }

            LoadAmmoFrom(ammo);
            return true;
        }

        private void LoadAmmoFrom(Thing ammo)
        {
            ThingDef ammoDef = ammo.def;
            ammo.SplitOff(1).Destroy(DestroyMode.Vanish);
            storedAmmo[ammoDef] = StoredCountFor(ammoDef) + 1;
        }

        public bool TryConsumeRocket()
        {
            return TryConsumeAmmo(RocketDef);
        }

        public int StoredCountFor(ThingDef ammoDef)
        {
            if (ammoDef == null || storedAmmo == null)
            {
                return 0;
            }

            return storedAmmo.TryGetValue(ammoDef, out int count) ? count : 0;
        }

        public bool TryConsumeAmmo(ThingDef ammoDef)
        {
            int count = StoredCountFor(ammoDef);
            if (count <= 0)
            {
                return false;
            }

            storedAmmo[ammoDef] = count - 1;
            return true;
        }

        public bool DropAmmo(ThingDef ammoDef)
        {
            Pawn wearer = Wearer;
            if (wearer?.Map == null || !TryConsumeAmmo(ammoDef))
            {
                return false;
            }

            Thing dropped = ThingMaker.MakeThing(ammoDef);
            dropped.stackCount = 1;
            GenPlace.TryPlaceThing(dropped, wearer.Position, wearer.Map, ThingPlaceMode.Near);
            return true;
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
            {
                yield return gizmo;
            }

            CompRecoillessWeapon weaponComp = AssignedRecoillessWeapon;
            if (weaponComp == null)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "HD_RecoillessWeapon_AmmoLabel".Translate(weaponComp.SelectedAmmoDef?.label ?? "None".Translate()).ToString(),
                defaultDesc = "HD_RecoillessWeapon_AmmoDesc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("Weapon/GreatWar/Ammo/HD_m6a3HEAT", false) ?? BaseContent.BadTex,
                action = weaponComp.ShowAmmoFloatMenu
            };
        }

        private CompRecoillessWeapon AssignedRecoillessWeapon
        {
            get
            {
                Pawn wearer = Wearer;
                if (wearer?.Map == null || wearer.Faction != Faction.OfPlayer)
                {
                    return null;
                }

                return wearer.Map.mapPawns.FreeColonistsSpawned
                    .Select(pawn => pawn.equipment?.Primary?.TryGetComp<CompRecoillessWeapon>())
                    .FirstOrDefault(comp => comp != null && comp.IsAssignedLoader(wearer));
            }
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
                        && weaponComp.FindLoadedAmmoBag(pawn) != null)
                    {
                        weaponComp.TryStartCrewReloadJob(WeaponUser);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Never
            };
            yield return wait;
        }
    }

    public class JobDriver_LoadM6RocketBag : JobDriver
    {
        private const TargetIndex RocketInd = TargetIndex.A;
        private const TargetIndex BagInd = TargetIndex.B;

        protected Thing Rocket => job.GetTarget(RocketInd).Thing;
        protected Thing Bag => job.GetTarget(BagInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Rocket, job, 1, 1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(RocketInd);
            this.FailOnForbidden(RocketInd);
            this.FailOn(() => Bag?.TryGetComp<CompM6RocketBag>() == null);
            this.FailOn(() => Bag?.TryGetComp<CompM6RocketBag>()?.Wearer != pawn);
            this.FailOn(() => Bag?.TryGetComp<CompM6RocketBag>()?.CanLoadAmmo(Rocket) != true);

            yield return Toils_Goto.GotoThing(RocketInd, PathEndMode.Touch);

            yield return new Toil
            {
                initAction = delegate
                {
                    Bag.TryGetComp<CompM6RocketBag>()?.TryLoadAmmo(Rocket);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
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
            this.FailOn(() => FindLoadedAmmoBag(pawn, Weapon.TryGetComp<CompRecoillessWeapon>().SelectedAmmoDef) == null);

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
                    CompM6RocketBag bag = FindLoadedAmmoBag(pawn, weaponComp?.SelectedAmmoDef);
                    if (bag != null && weaponComp != null && !weaponComp.Loaded && bag.TryConsumeAmmo(weaponComp.SelectedAmmoDef))
                    {
                        weaponComp.Load();
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        private static CompM6RocketBag FindLoadedAmmoBag(Pawn pawn, ThingDef ammoDef)
        {
            return pawn.apparel?.WornApparel?
                .Select(apparel => apparel.TryGetComp<CompM6RocketBag>())
                .FirstOrDefault(comp => comp != null && comp.StoredCountFor(ammoDef) > 0);
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
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_RecoillessWeapon
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            CompRecoillessWeapon comp = __instance?.equipment?.Primary?.TryGetComp<CompRecoillessWeapon>();
            if (comp == null)
            {
                return;
            }

            __result = __result.Concat(comp.CompGetGizmosExtra());
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
