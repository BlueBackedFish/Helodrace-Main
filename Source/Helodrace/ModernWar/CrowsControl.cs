using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    [DefOf]
    public static class CrowsDefOf
    {
        public static JobDef HD_OperateCrows;
        public static JobDef HD_HaulCrowsMountMaterial;
        public static JobDef HD_InstallCrowsWeapon;
        public static ThingDef HD_Building_CrowsII;
        static CrowsDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(CrowsDefOf));
    }

    public static class CrowsControlUtility
    {
        public static bool Powered(Building building) => building?.Spawned == true
            && building.TryGetComp<CompPowerTrader>()?.PowerOn == true
            && building.TryGetComp<CompFlickable>()?.SwitchIsOn != false
            && !building.Map.gameConditionManager.ElectricityDisabled(building.Map);

        public static bool CanOperate(Pawn pawn, Building_CrowsComputer computer) => pawn != null && computer != null
            && pawn.Spawned && !pawn.Dead && !pawn.Downed && !pawn.InMentalState
            && pawn.RaceProps.Humanlike && pawn.Map == computer.Map && pawn.Faction == computer.Faction
            && !pawn.WorkTagIsDisabled(WorkTags.Violent)
            && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
            && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Sight);

        public static Command_Action LinkCommand(Building owner)
        {
            return new Command_Action
            {
                defaultLabel = "HD_Crows_Connect".Translate(),
                defaultDesc = "HD_Crows_ConnectDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack"),
                action = () => Find.Targeter.BeginTargeting(new TargetingParameters
                {
                    canTargetBuildings = true,
                    canTargetPawns = false,
                    canTargetItems = false,
                    canTargetLocations = false,
                    validator = t => owner.Spawned && t.Thing?.Spawned == true
                        && t.Thing.Map == owner.Map && t.Thing.Faction == owner.Faction
                        && (owner is Building_CrowsComputer ? t.Thing is Building_Crows : t.Thing is Building_CrowsComputer)
                }, target =>
                {
                    Building_CrowsComputer computer = owner as Building_CrowsComputer ?? target.Thing as Building_CrowsComputer;
                    Building_Crows turret = owner as Building_Crows ?? target.Thing as Building_Crows;
                    computer?.Connect(turret);
                })
            };
        }

        public static Command_Action DisconnectCommand(System.Action disconnect) => new Command_Action
        {
            defaultLabel = "HD_Crows_Disconnect".Translate(),
            defaultDesc = "HD_Crows_DisconnectDesc".Translate(),
            icon = ContentFinder<Texture2D>.Get("UI/Commands/Cancel"),
            action = disconnect
        };
    }

    public sealed class Building_CrowsComputer : Building
    {
        private Building_Crows linkedTurret;
        private Graphic controllerGraphic;
        public Building_Crows LinkedTurret => linkedTurret;
        public bool Connected => linkedTurret?.Spawned == true && Spawned
            && linkedTurret.Map == Map && linkedTurret.Faction == Faction && linkedTurret.Computer == this;
        public bool CanUseNow => Connected && CrowsControlUtility.Powered(this)
            && linkedTurret.HasWeapon && CrowsControlUtility.Powered(linkedTurret);

        // Both images use identical camera registration; the complete variant
        // also preserves the controller's occlusion behind existing desk parts.
        public override Graphic Graphic => Connected
            ? controllerGraphic ?? (controllerGraphic = GraphicDatabase.Get<Graphic_Multi>(
                "Buildings/ComputerDesk/CROWSController/HD_ComputerDesk_CROWS", ShaderDatabase.Cutout,
                def.graphicData.drawSize, Color.white, Color.white, def.graphicData))
            : base.Graphic;

        public Pawn Operator
        {
            get
            {
                if (!CanUseNow || !InteractionCell.InBounds(Map)) return null;
                foreach (Thing thing in InteractionCell.GetThingList(Map))
                {
                    if (thing is Pawn pawn && CrowsControlUtility.CanOperate(pawn, this)
                        && !this.IsForbidden(pawn) && !linkedTurret.IsForbidden(pawn)
                        && pawn.CurJob?.def == CrowsDefOf.HD_OperateCrows
                        && pawn.CurJob.targetA.Thing == this && pawn.CurJob.targetB.Thing == linkedTurret
                        && pawn.jobs.curDriver is JobDriver_OperateCrows driver && driver.AtControls)
                        return pawn;
                }
                return null;
            }
        }

        public void Connect(Building_Crows turret)
        {
            if (turret == null || !Spawned || !turret.Spawned || turret.Map != Map || turret.Faction != Faction) return;
            if (Connected && linkedTurret == turret) return;
            Disconnect();
            turret.Computer?.Disconnect();
            linkedTurret = turret;
            turret.Computer = this;
        }

        public void Disconnect()
        {
            Building_Crows old = linkedTurret;
            linkedTurret = null;
            if (old?.Computer == this)
            {
                old.Computer = null;
                old.StopRemoteFire();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref linkedTurret, "crowsTurret");
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Disconnect();
            base.DeSpawn(mode);
        }

        public override void SetFaction(Faction newFaction, Pawn recruiter = null)
        {
            if (newFaction != Faction) Disconnect();
            base.SetFaction(newFaction, recruiter);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (Faction != Faction.OfPlayer) yield break;
            yield return CrowsControlUtility.LinkCommand(this);
            if (linkedTurret != null) yield return CrowsControlUtility.DisconnectCommand(Disconnect);
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (Connected) GenDraw.DrawLineBetween(DrawPos, linkedTurret.DrawPos);
        }

        public override string GetInspectString() => base.GetInspectString()
            + (Connected ? "\n" + "HD_Crows_Linked".Translate(linkedTurret.LabelShort)
                + "\n" + (Operator != null ? "HD_Crows_Operator".Translate(Operator.LabelShort) : "HD_Crows_Unmanned".Translate())
                : "\n" + "HD_Crows_Unlinked".Translate());

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn pawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(pawn)) yield return option;
            if (!CrowsControlUtility.CanOperate(pawn, this)) yield break;
            string label = "HD_Crows_Operate".Translate();
            string reason = !Connected ? "HD_Crows_Unlinked".Translate().ToString()
                : !linkedTurret.HasWeapon ? "HD_Crows_NoWeapon".Translate().ToString()
                : !CanUseNow ? "HD_Crows_NoPower".Translate().ToString()
                : this.IsForbidden(pawn) || linkedTurret.IsForbidden(pawn) ? "Forbidden".Translate().ToString()
                : !pawn.CanReach(this, PathEndMode.InteractionCell, Danger.Deadly) ? "NoPath".Translate().ToString()
                : !pawn.CanReserve(this) || !pawn.CanReserve(InteractionCell) ? "Reserved".Translate().ToString() : null;
            if (reason != null)
            {
                yield return new FloatMenuOption(label + ": " + reason, null);
                yield break;
            }
            yield return new FloatMenuOption(label, () => pawn.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(CrowsDefOf.HD_OperateCrows, this, linkedTurret), JobTag.Misc));
        }
    }

    public sealed partial class Building_Crows : Building_TurretGun
    {
        private Building_CrowsComputer computer;
        public Building_CrowsComputer Computer { get => computer; internal set => computer = value; }
        // Vanilla restricts forced targeting to a local CompMannable. This
        // station instead keeps the normal target command with remote control.
        protected override bool CanSetForcedTarget => HasWeapon && Faction == Faction.OfPlayer;
        public bool CanFireNow => HasWeapon && CrowsControlUtility.Powered(this) && Active && !IsStunned
            && GetComp<CompCrowsAmmo>()?.HasFuel == true
            && computer?.LinkedTurret == this && computer.Operator != null;

        public void StopRemoteFire()
        {
            burstWarmupTicksLeft = 0;
            currentTargetInt = LocalTargetInfo.Invalid;
            AttackVerb?.Reset();
        }

        protected override void Tick()
        {
            if (!CanFireNow) StopRemoteFire();
            // Retain vanilla power/fuel comps, cooldown and targeting. The
            // dedicated verb blocks acquisition and every shot when unmanned.
            base.Tick();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref computer, "crowsComputer");
            ExposeWeaponMountData();
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            CancelWeaponInstallation();
            computer?.Disconnect();
            base.DeSpawn(mode);
        }

        public override void SetFaction(Faction newFaction, Pawn recruiter = null)
        {
            if (newFaction != Faction) { CancelWeaponInstallation(); computer?.Disconnect(); }
            base.SetFaction(newFaction, recruiter);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (Faction != Faction.OfPlayer) yield break;
            foreach (Gizmo gizmo in WeaponMountGizmos()) yield return gizmo;
            yield return CrowsControlUtility.LinkCommand(this);
            if (computer != null) yield return CrowsControlUtility.DisconnectCommand(() => computer?.Disconnect());
        }

        public override string GetInspectString() => base.GetInspectString() + "\n" + WeaponMountInspectString() + "\n"
            + (computer?.Connected == true ? "HD_Crows_Linked".Translate(computer.LabelShort) + "\n"
                + (computer.Operator != null ? "HD_Crows_Ready".Translate() : "HD_Crows_Unmanned".Translate())
                : "HD_Crows_Unlinked".Translate());
    }

    public sealed class Verb_Crows : Verb_Shoot
    {
        public override bool Available() => Caster is Building_Crows turret && turret.CanFireNow && base.Available();

        protected override bool TryCastShot()
        {
            if (!(Caster is Building_Crows turret) || !turret.CanFireNow) return false;
            bool fired = base.TryCastShot();
            if (fired) turret.GetComp<CompCrowsGraphic>()?.NotifyShotFired();
            return fired;
        }
    }

    public sealed class JobDriver_OperateCrows : JobDriver
    {
        private Building_CrowsComputer Computer => TargetThingA as Building_CrowsComputer;
        public bool AtControls => CurToilIndex == 1 && pawn.Position == Computer?.InteractionCell;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
            && pawn.Reserve(Computer.InteractionCell, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(() => Computer?.CanUseNow != true || Computer.LinkedTurret != job.targetB.Thing
                || !CrowsControlUtility.CanOperate(pawn, Computer));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            Toil operate = ToilMaker.MakeToil("OperateCrows");
            operate.defaultCompleteMode = ToilCompleteMode.Never;
            operate.handlingFacing = true;
            operate.tickIntervalAction = delta =>
            {
                if (pawn.Position != Computer.InteractionCell) { EndJobWith(JobCondition.Incompletable); return; }
                pawn.rotationTracker.FaceTarget(Computer);
                pawn.GainComfortFromCellIfPossible(delta);
            };
            yield return operate;
        }
    }
}
