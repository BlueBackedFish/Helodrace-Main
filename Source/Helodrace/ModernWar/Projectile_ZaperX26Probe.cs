using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Helodrace.ModernWar
{
    public sealed class CompProperties_ZaperX26 : CompProperties
    {
        public CompProperties_ZaperX26()
        {
            compClass = typeof(CompZaperX26);
        }
    }

    /// <summary>
    /// Pairs probe impacts from the same X26. A lone hit expires without
    /// shocking the target, while two hits on the same pawn start the tether.
    /// </summary>
    public sealed class CompZaperX26 : ThingComp
    {
        private const int ProbePairWindowTicks = 12;
        private const int ShockDurationTicks = 300;
        private const int RangedCooldownTicks = 144;
        private const int ContactCooldownTicks = 90;

        private Pawn pendingTarget;
        private int pendingProbeExpiresAt;
        private ZaperX26TetherEffect activeTether;
        private int nextRangedUseTick;
        private int nextContactUseTick;

        public bool HasActiveTether => activeTether != null && !activeTether.Destroyed && activeTether.Spawned;
        public Pawn Wearer => (parent.ParentHolder as Pawn_ApparelTracker)?.pawn;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref pendingTarget, "zaperPendingTarget");
            Scribe_Values.Look(ref pendingProbeExpiresAt, "zaperPendingProbeExpiresAt");
            Scribe_References.Look(ref activeTether, "zaperActiveTether");
            Scribe_Values.Look(ref nextRangedUseTick, "zaperNextRangedUseTick", 0);
            Scribe_Values.Look(ref nextContactUseTick, "zaperNextContactUseTick", 0);
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

            Command_Action fire = new Command_Action
            {
                defaultLabel = "HD_ZaperX26_Fire_Label".Translate(),
                defaultDesc = "HD_ZaperX26_Fire_Desc".Translate(),
                icon = ContentFinder<Texture2D>.Get(parent.def.graphicData.texPath, true),
                action = () => BeginTargeting("HD_ZaperX26Fire")
            };
            DisableIfUnavailable(fire, nextRangedUseTick, HasActiveTether);
            yield return fire;

            Command_Action contact = new Command_Action
            {
                defaultLabel = "HD_ZaperX26_Contact_Label".Translate(),
                defaultDesc = "HD_ZaperX26_Contact_Desc".Translate(),
                icon = ContentFinder<Texture2D>.Get(parent.def.graphicData.texPath, true),
                action = () => BeginTargeting("HD_ZaperX26Contact")
            };
            DisableIfUnavailable(contact, nextContactUseTick, false);
            yield return contact;
        }

        private void DisableIfUnavailable(Command command, int readyTick, bool tetherActive)
        {
            Pawn wearer = Wearer;
            if (wearer?.Spawned != true || wearer.Downed)
            {
                command.Disable("HD_ZaperX26_Unavailable".Translate());
            }
            else if (tetherActive)
            {
                command.Disable("HD_ZaperX26_TetherActive".Translate());
            }
            else if (readyTick > Find.TickManager.TicksGame)
            {
                command.Disable("HD_ZaperX26_Cooldown".Translate((readyTick - Find.TickManager.TicksGame).ToStringTicksToPeriod()));
            }
        }

        private void BeginTargeting(string jobDefName)
        {
            Pawn wearer = Wearer;
            if (wearer?.Map == null)
            {
                return;
            }

            TargetingParameters parameters = TargetingParameters.ForAttackAny();
            parameters.validator = target => target.Thing is Pawn pawn
                && pawn.Spawned
                && !pawn.Dead
                && pawn.Map == wearer.Map
                && pawn != wearer;
            Find.Targeter.BeginTargeting(parameters, target => StartUseJob(jobDefName, target.Thing as Pawn));
        }

        private void StartUseJob(string jobDefName, Pawn target)
        {
            Pawn wearer = Wearer;
            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(jobDefName);
            if (wearer?.jobs == null || target == null || jobDef == null)
            {
                return;
            }

            Job job = JobMaker.MakeJob(jobDef, target, parent);
            job.playerForced = true;
            wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public bool CanFireNow => Wearer?.Spawned == true
            && !Wearer.Downed
            && !HasActiveTether
            && Find.TickManager.TicksGame >= nextRangedUseTick;

        public bool CanContactNow => Wearer?.Spawned == true
            && !Wearer.Downed
            && Find.TickManager.TicksGame >= nextContactUseTick;

        public void MarkRangedUsed()
        {
            nextRangedUseTick = Find.TickManager.TicksGame + RangedCooldownTicks;
        }

        public void MarkContactUsed()
        {
            nextContactUseTick = Find.TickManager.TicksGame + ContactCooldownTicks;
        }

        public void RegisterProbeHit(Pawn launcher, Pawn target, Map map)
        {
            if (launcher == null || target == null || map == null || target.Map != map)
            {
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            if (pendingTarget == target && currentTick <= pendingProbeExpiresAt)
            {
                pendingTarget = null;
                pendingProbeExpiresAt = 0;
                ZaperX26TetherEffect.StartOrRefresh(this, launcher, target, map, ShockDurationTicks);
                return;
            }

            pendingTarget = target;
            pendingProbeExpiresAt = currentTick + ProbePairWindowTicks;
        }

        public void SetActiveTether(ZaperX26TetherEffect tether)
        {
            activeTether = tether;
        }

        public void ClearActiveTether(ZaperX26TetherEffect tether)
        {
            if (activeTether == tether)
            {
                activeTether = null;
            }
        }
    }

    public abstract class JobDriver_UseZaperX26 : JobDriver
    {
        protected Pawn TargetPawn => job.GetTarget(TargetIndex.A).Pawn;
        protected ThingWithComps Zaper => job.GetTarget(TargetIndex.B).Thing as ThingWithComps;
        protected CompZaperX26 ZaperComp => Zaper?.TryGetComp<CompZaperX26>();

        protected bool ZaperStillWorn => ZaperComp?.Wearer == pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected Toil FaceAndWait(int ticks)
        {
            Toil wait = Toils_General.Wait(ticks, TargetIndex.A);
            wait.tickAction = delegate
            {
                if (TargetPawn != null)
                {
                    pawn.rotationTracker.FaceTarget(TargetPawn);
                }
            };
            return wait;
        }
    }

    public sealed class JobDriver_ZaperX26Fire : JobDriver_UseZaperX26
    {
        private const float Range = 7.9f;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !ZaperStillWorn || ZaperComp?.CanFireNow != true);
            this.FailOn(() => TargetPawn == null || TargetPawn.Dead || TargetPawn.Map != pawn.Map);

            Toil approach = new Toil
            {
                initAction = delegate
                {
                    if (!CanShootFromCurrentPosition())
                    {
                        pawn.pather.StartPath(TargetPawn, PathEndMode.Touch);
                    }
                },
                tickAction = delegate
                {
                    if (CanShootFromCurrentPosition())
                    {
                        pawn.pather.StopDead();
                        ReadyForNextToil();
                    }
                    else if (!pawn.pather.Moving)
                    {
                        EndJobWith(JobCondition.Incompletable);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Never
            };
            yield return approach;
            yield return FaceAndWait(21);
            yield return new Toil
            {
                initAction = FireProbes,
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        private bool CanShootFromCurrentPosition()
        {
            return TargetPawn?.Spawned == true
                && pawn.Position.DistanceTo(TargetPawn.Position) <= Range
                && GenSight.LineOfSight(pawn.Position, TargetPawn.Position, pawn.Map);
        }

        private void FireProbes()
        {
            if (!CanShootFromCurrentPosition() || ZaperComp?.CanFireNow != true)
            {
                return;
            }

            ThingDef probeDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_ZaperX26_Probe");
            if (probeDef == null)
            {
                return;
            }

            ZaperComp.MarkRangedUsed();
            SoundDef launchSound = DefDatabase<SoundDef>.GetNamedSilentFail("Mortar_LaunchA");
            if (launchSound != null)
            {
                SoundInfo soundInfo = SoundInfo.InMap(new TargetInfo(pawn), MaintenanceType.None);
                soundInfo.volumeFactor = 0.45f;
                SoundStarter.PlayOneShot(launchSound, soundInfo);
            }

            for (int i = 0; i < 2; i++)
            {
                Projectile projectile = GenSpawn.Spawn(probeDef, pawn.Position, pawn.Map) as Projectile;
                projectile?.Launch(pawn, TargetPawn, TargetPawn, ProjectileHitFlags.IntendedTarget, false, Zaper);
            }
        }
    }

    public sealed class JobDriver_ZaperX26Contact : JobDriver_UseZaperX26
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !ZaperStillWorn || ZaperComp?.CanContactNow != true);
            this.FailOn(() => TargetPawn == null || TargetPawn.Dead || TargetPawn.Map != pawn.Map);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return FaceAndWait(18);
            yield return new Toil
            {
                initAction = Strike,
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        private void Strike()
        {
            if (ZaperComp?.CanContactNow != true
                || TargetPawn?.Spawned != true
                || !pawn.Position.AdjacentTo8WayOrInside(TargetPawn.Position))
            {
                return;
            }

            DamageInfo damage = new DamageInfo(DamageDefOf.Blunt, 1f, 0f, -1f, pawn, weapon: Zaper.def);
            DamageWorker.DamageResult result = TargetPawn.TakeDamage(damage);
            ZaperComp.MarkContactUsed();
            if (result.totalDamageDealt > 0f)
            {
                ZaperX26Utility.ApplyContactShock(TargetPawn);
            }
        }
    }

    /// <summary>
    /// A short-range X26 probe that keeps its deployment wire visually attached
    /// to the pawn that fired it for the duration of its flight.
    /// </summary>
    public sealed class Projectile_ZaperX26Probe : Projectile
    {
        private const float WireWidth = 0.022f;

        private static Material wireMaterial;

        internal static Material WireMaterial => wireMaterial ??
            (wireMaterial = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.30f, 0.28f, 0.24f)));

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map impactMap = Map;
            Pawn firingPawn = Launcher as Pawn;
            Pawn hitPawn = !blockedByShield ? hitThing as Pawn : null;
            CompZaperX26 zaperComp = equipment?.TryGetComp<CompZaperX26>();
            float impactAngle = ExactRotation.eulerAngles.y;
            Thing intendedThing = intendedTarget.Thing;
            ThingDef sourceWeaponDef = equipmentDef;

            base.Impact(hitThing, blockedByShield);

            if (hitPawn != null && firingPawn != null && zaperComp != null)
            {
                bool instigatorGuilty = !firingPawn.Drafted;
                DamageInfo probeDamage = new DamageInfo(
                    DamageDefOf.Bullet,
                    DamageAmount,
                    ArmorPenetration,
                    impactAngle,
                    firingPawn,
                    weapon: sourceWeaponDef,
                    intendedTarget: intendedThing,
                    instigatorGuilty: instigatorGuilty);
                DamageWorker.DamageResult result = hitPawn.TakeDamage(probeDamage);

                // Armor blocks the electrical circuit when no gunshot wound is
                // produced. Such a probe does not count toward the required pair.
                if (result.totalDamageDealt > 0f && result.wounded)
                {
                    zaperComp.RegisterProbeHit(firingPawn, hitPawn, impactMap);
                }
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            DrawFlightWire(drawLoc);
            base.DrawAt(drawLoc, flip);
        }

        private void DrawFlightWire(Vector3 probePosition)
        {
            if (!(Launcher is Pawn pawn) || !pawn.Spawned || pawn.Map != Map)
            {
                return;
            }

            DrawSaggingWire(pawn.DrawPos, probePosition, 0f, WireWidth);
        }

        internal static void DrawSaggingWire(Vector3 start, Vector3 end, float lateralOffset, float width)
        {
            Vector3 direction = end - start;
            Vector3 lateral = direction.sqrMagnitude > 0.001f
                ? new Vector3(-direction.z, 0f, direction.x).normalized * lateralOffset
                : Vector3.zero;
            start += lateral;
            end += lateral;

            float altitude = AltitudeLayer.MoteOverhead.AltitudeFor();
            start.y = altitude;
            end.y = altitude;
            Vector3 midpoint = Vector3.Lerp(start, end, 0.5f);
            midpoint.z -= Mathf.Min(0.12f, Vector3.Distance(start, end) * 0.014f);

            GenDraw.DrawLineBetween(start, midpoint, WireMaterial, width);
            GenDraw.DrawLineBetween(midpoint, end, WireMaterial, width);
        }
    }

    /// <summary>
    /// Persistent paired wires and shock loop after both probes have embedded.
    /// The effect owns the temporary hediff so visuals, sound, and penalties end
    /// on exactly the same tick.
    /// </summary>
    public sealed class ZaperX26TetherEffect : Thing
    {
        private const float MaximumConnectedDistance = 11f;
        private const float ActiveWireWidth = 0.026f;

        private Pawn launcher;
        private Pawn target;
        private ThingWithComps sourceWeapon;
        private int ticksLeft;
        private Hediff shockHediff;
        private Sustainer shockSustainer;

        public static void StartOrRefresh(CompZaperX26 sourceComp, Pawn launcher, Pawn target, Map map, int durationTicks)
        {
            ThingDef effectDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_ZaperX26_TetherEffect");
            if (effectDef == null)
            {
                Log.ErrorOnce("Helodrace: HD_ZaperX26_TetherEffect ThingDef is missing.", 26260001);
                return;
            }

            List<Thing> existingEffects = map.listerThings.ThingsOfDef(effectDef);
            for (int i = 0; i < existingEffects.Count; i++)
            {
                if (existingEffects[i] is ZaperX26TetherEffect existing && existing.target == target)
                {
                    existing.Initialize(sourceComp, launcher, target, durationTicks);
                    return;
                }
            }

            ZaperX26TetherEffect effect = ThingMaker.MakeThing(effectDef) as ZaperX26TetherEffect;
            if (effect == null)
            {
                return;
            }

            GenSpawn.Spawn(effect, target.Position, map);
            effect.Initialize(sourceComp, launcher, target, durationTicks);
        }

        public void Initialize(CompZaperX26 sourceComp, Pawn newLauncher, Pawn newTarget, int durationTicks)
        {
            sourceWeapon?.TryGetComp<CompZaperX26>()?.ClearActiveTether(this);
            sourceWeapon = sourceComp?.parent;
            launcher = newLauncher;
            target = newTarget;
            ticksLeft = Mathf.Max(ticksLeft, durationTicks);
            sourceComp?.SetActiveTether(this);
            EnsureShockHediff();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref launcher, "zaperLauncher");
            Scribe_References.Look(ref target, "zaperTarget");
            Scribe_References.Look(ref sourceWeapon, "zaperSourceWeapon");
            Scribe_Values.Look(ref ticksLeft, "zaperTicksLeft");
        }

        protected override void Tick()
        {
            base.Tick();

            if (!ConnectionIsValid())
            {
                Destroy();
                return;
            }

            EnsureShockHediff();
            MaintainShockSound();
            ticksLeft--;
            if (ticksLeft <= 0)
            {
                Destroy();
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (!ConnectionIsValid())
            {
                return;
            }

            Vector3 start = launcher.DrawPos;
            Vector3 end = target.DrawPos;
            Projectile_ZaperX26Probe.DrawSaggingWire(start, end, -0.045f, ActiveWireWidth);
            Projectile_ZaperX26Probe.DrawSaggingWire(start, end, 0.045f, ActiveWireWidth);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            EndShockSound();
            RemoveShockHediff();
            sourceWeapon?.TryGetComp<CompZaperX26>()?.ClearActiveTether(this);
            base.Destroy(mode);
        }

        private bool ConnectionIsValid()
        {
            return ticksLeft > 0
                && launcher != null
                && target != null
                && !launcher.Destroyed
                && !target.Destroyed
                && launcher.Spawned
                && target.Spawned
                && launcher.Map == Map
                && target.Map == Map
                && sourceWeapon?.ParentHolder is Pawn_ApparelTracker apparelTracker
                && apparelTracker.pawn == launcher
                && launcher.Position.DistanceTo(target.Position) <= MaximumConnectedDistance;
        }

        private void EnsureShockHediff()
        {
            if (target?.health == null)
            {
                return;
            }

            HediffDef shockDef = DefDatabase<HediffDef>.GetNamedSilentFail("HD_ZaperX26_Shock");
            if (shockDef == null)
            {
                return;
            }

            if (shockHediff == null || shockHediff.pawn != target)
            {
                shockHediff = target.health.hediffSet.GetFirstHediffOfDef(shockDef);
            }

            if (shockHediff == null)
            {
                shockHediff = target.health.AddHediff(shockDef);
            }
        }

        private void RemoveShockHediff()
        {
            if (shockHediff != null && target?.health?.hediffSet?.hediffs.Contains(shockHediff) == true)
            {
                target.health.RemoveHediff(shockHediff);
            }

            shockHediff = null;
        }

        private void MaintainShockSound()
        {
            if (shockSustainer == null || shockSustainer.Ended)
            {
                SoundDef loopDef = DefDatabase<SoundDef>.GetNamedSilentFail("HD_ZaperX26_ShockLoop");
                if (loopDef != null)
                {
                    SoundInfo soundInfo = SoundInfo.InMap(new TargetInfo(target), MaintenanceType.PerTick);
                    soundInfo.volumeFactor = 7f;
                    shockSustainer = SoundStarter.TrySpawnSustainer(loopDef, soundInfo);
                }
            }

            shockSustainer?.Maintain();
        }

        private void EndShockSound()
        {
            if (shockSustainer != null && !shockSustainer.Ended)
            {
                shockSustainer.End();
            }

            shockSustainer = null;
        }
    }

    public static class ZaperX26Utility
    {
        public static void ApplyContactShock(Pawn target)
        {
            if (target == null || target.Dead)
            {
                return;
            }

            SoundDef contactSound = DefDatabase<SoundDef>.GetNamedSilentFail("HD_ZaperX26_Contact");
            if (target.Map != null && contactSound != null)
            {
                contactSound.PlayOneShot(new TargetInfo(target.Position, target.Map));
            }

            HediffDef contactShockDef = DefDatabase<HediffDef>.GetNamedSilentFail("HD_ZaperX26_ContactShock");
            if (contactShockDef == null)
            {
                return;
            }

            Hediff existing = target.health.hediffSet.GetFirstHediffOfDef(contactShockDef);
            if (existing != null)
            {
                target.health.RemoveHediff(existing);
            }

            target.health.AddHediff(contactShockDef);
        }
    }
}
