using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helodrace.ModernWar;
using Helodrace.Tactical;
using RimWorld;
using Verse;

namespace Helodrace.Ballistics
{
    /// <summary>Aiming stance of one weapon family (Defs/Ballistics/StanceProfiles.xml). [game]</summary>
    public sealed class StanceProfileDef : Def
    {
        public StanceFamily family;
        public float torsoOffsetDegrees;
        public float headOffsetDegrees;
        public int updateIntervalTicks = 3;
        public List<ThingDef> weaponDefs;

        public StanceParameters Parameters => new StanceParameters(torsoOffsetDegrees, headOffsetDegrees, updateIntervalTicks);

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (Math.Abs(torsoOffsetDegrees) > 45f) yield return defName + " torso offset leaves the target behind the shoulder.";
        }

        private static Dictionary<ThingDef, StanceProfileDef> byWeapon;
        private static Dictionary<StanceFamily, StanceProfileDef> byFamily;

        /// <summary>Profile for a wielded weapon (null weapon = unarmed). Explicit weaponDefs beat the heuristic.</summary>
        public static StanceProfileDef For(ThingDef weapon)
        {
            if (byFamily == null)
            {
                byFamily = new Dictionary<StanceFamily, StanceProfileDef>();
                byWeapon = new Dictionary<ThingDef, StanceProfileDef>();
                foreach (StanceProfileDef profile in DefDatabase<StanceProfileDef>.AllDefsListForReading)
                {
                    byFamily[profile.family] = profile;
                }
            }
            if (weapon == null) return Family(StanceFamily.Unarmed);
            if (byWeapon.TryGetValue(weapon, out StanceProfileDef cached)) return cached;
            StanceProfileDef explicitProfile = DefDatabase<StanceProfileDef>.AllDefsListForReading
                .FirstOrDefault(p => p.weaponDefs?.Contains(weapon) == true);
            StanceProfileDef result = explicitProfile ?? Family(BodyOrientationRules.Classify(Facts(weapon)));
            byWeapon[weapon] = result;
            return result;
        }

        private static StanceProfileDef Family(StanceFamily family) =>
            byFamily.TryGetValue(family, out StanceProfileDef profile) ? profile
                : byFamily.TryGetValue(StanceFamily.Rifle, out profile) ? profile : null;

        internal static WeaponStanceFacts Facts(ThingDef weapon)
        {
            VerbProperties verb = weapon.Verbs?.FirstOrDefault(v => v?.defaultProjectile != null);
            if (verb == null || !weapon.IsRangedWeapon) return new WeaponStanceFacts(false, 0f, 0f, 0, false, false);
            ThingDef projectile = verb.defaultProjectile;
            bool shotgun = typeof(Projectile_ShotgunPellet).IsAssignableFrom(projectile.thingClass);
            bool explosive = typeof(Projectile_Explosive).IsAssignableFrom(projectile.thingClass)
                || (projectile.projectile?.explosionRadius ?? 0f) > 0f;
            return new WeaponStanceFacts(true, weapon.GetStatValueAbstract(StatDefOf.Mass), verb.range,
                verb.burstShotCount, shotgun, explosive);
        }
    }

    public sealed class CompProperties_BodyOrientation : CompProperties
    {
        public CompProperties_BodyOrientation()
        {
            compClass = typeof(CompBodyOrientation);
        }
    }

    /// <summary>
    /// Continuous torso and head yaw of a humanlike pawn (design doc section 6.1). Whatever assigns the pawn's
    /// Rot4 (rotation tracker, jobs, stances, Ironhide forced facing, tactical aim) states an intent; the torso
    /// takes that intent (plus the aiming offset) without a turn-rate limit, and the stored Rot4 is its sprite.
    /// The side drawn is therefore the side that the ballistic hit uses.
    /// </summary>
    public sealed class CompBodyOrientation : ThingComp
    {
        private readonly BodyOrientationState state = new BodyOrientationState();
        private float spriteYaw = -1f;
        private float intentYaw;
        private int lastSampleTick = int.MinValue;
        private int lastFacingTick = int.MinValue;
        private bool aiming;
        private float aimYaw;
        private bool writing;
        private bool disabled = true;
        private float savedTorso;
        private float savedHead;
        private StanceProfileDef profile;
        private ThingDef profileWeapon;

        private Pawn Pawn => parent as Pawn;
        private static int Now => Find.TickManager?.TicksGame ?? 0;

        public bool Aiming => aiming;
        public float AimYaw => aimYaw;
        public StanceProfileDef Profile => profile;
        public float SpriteYaw => spriteYaw;

        public float TorsoYaw
        {
            get { Sync(Now); return state.TorsoYaw; }
        }

        public float HeadYaw
        {
            get { Sync(Now); return state.HeadYaw; }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                savedTorso = state.TorsoYaw;
                savedHead = state.HeadYaw;
            }
            Scribe_Values.Look(ref savedTorso, "torsoYaw");
            Scribe_Values.Look(ref savedHead, "headYaw");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                state.Reset(savedTorso, Now);
                state.HeadYaw = BodyOrientationRules.ClampHead(savedTorso, savedHead);
                intentYaw = savedTorso;
                spriteYaw = -1f;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (Pawn == null) return;
            float facing = Pawn.Rotation.AsAngle;
            if (!respawningAfterLoad || !state.Initialized)
            {
                state.Reset(facing, Now);
                intentYaw = facing;
            }
            spriteYaw = facing;
            disabled = !Active(Pawn);
        }

        public override void CompTick()
        {
            base.CompTick();
            TickFallback();
        }

        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);
            TickFallback();
        }

        private static bool Active(Pawn pawn) => pawn.Spawned && !pawn.Dead && !pawn.Downed;

        private void TickFallback()
        {
            // Keep turning toward the last intent even on ticks nobody assigns a rotation.
            Pawn pawn = Pawn;
            int now = Now;
            if (pawn == null || lastFacingTick == now || disabled || !Active(pawn)) return;
            Update(now, intentYaw);
            Write(pawn, Rot4.FromAngleFlat(spriteYaw));
        }

        /// <summary>
        /// Every assignment of Pawn.Rotation passes through here (Thing.Rotation setter prefix). The requested
        /// facing becomes the intent; the value actually stored is the sprite of the turned torso.
        /// </summary>
        internal Rot4 Filter(Rot4 requested)
        {
            if (writing) return requested;
            Pawn pawn = Pawn;
            if (pawn == null) return requested;
            if (!Active(pawn))
            {
                disabled = true;
                return requested;
            }
            int now = Now;
            if (disabled)
            {
                // Spawned, stood up or revived: start from the facing it is given.
                disabled = false;
                state.Reset(requested.AsAngle, now);
                spriteYaw = intentYaw = requested.AsAngle;
                lastFacingTick = now;
                return requested;
            }
            lastFacingTick = now;
            float intent = requested.AsAngle;
            if (pawn.pather != null && pawn.pather.Moving && pawn.pather.nextCell.IsValid && pawn.pather.nextCell != pawn.Position)
            {
                IntVec3 step = pawn.pather.nextCell - pawn.Position;
                float heading = BallisticGeometry.NormalizeDegrees((float)(Math.Atan2(step.x, step.z) * BallisticGeometry.RadToDeg));
                // The requested Rot4 is the rounding of the path step: turn to the step itself.
                if (Math.Abs(BallisticGeometry.DeltaDegrees(heading, intent)) <= 45f) intent = heading;
            }
            Update(now, intent);
            return Rot4.FromAngleFlat(spriteYaw);
        }

        private void Write(Pawn pawn, Rot4 rotation)
        {
            if (pawn.Rotation == rotation) return;
            writing = true;
            try { pawn.Rotation = rotation; }
            finally { writing = false; }
        }

        private void Update(int now, float intent)
        {
            Pawn pawn = Pawn;
            intentYaw = intent;
            if (lastSampleTick == int.MinValue || now - lastSampleTick >= (profile?.updateIntervalTicks ?? 1))
            {
                lastSampleTick = now;
                SampleStance(pawn);
            }
            // Shield bearers keep the shield on the forced facing: no bladed offset.
            bool shield = DirectionalBallisticShieldUtility.WornShield(pawn) != null;
            StanceParameters stance = profile?.Parameters ?? state.Stance;
            float torsoTarget = shield ? intentYaw : BodyOrientationRules.TorsoTarget(aiming, aimYaw, intentYaw, stance);
            float headTarget = BodyOrientationRules.HeadTarget(aiming, aimYaw, torsoTarget, stance);
            if (!state.Initialized) state.Reset(torsoTarget, now);
            // No turn-rate limit: take the new targets before choosing the sprite, so both show the same yaw.
            state.SetTargets(torsoTarget, headTarget);
            state.Advance(now);
            spriteYaw = BodyOrientationRules.SpriteYaw(state.TorsoYaw, spriteYaw < 0f ? pawn.Rotation.AsAngle : spriteYaw);
        }

        /// <summary>Integrates the turn to a tick without changing targets (impact reads).</summary>
        private void Sync(int now)
        {
            if (!state.Initialized) state.Reset(parent.Rotation.AsAngle, now);
            state.Advance(now);
        }

        private void SampleStance(Pawn pawn)
        {
            ThingDef weapon = pawn.equipment?.Primary?.def;
            if (profile == null || weapon != profileWeapon)
            {
                profileWeapon = weapon;
                profile = StanceProfileDef.For(weapon);
                if (profile != null) state.Stance = profile.Parameters;
            }
            aiming = TryAim(pawn, out aimYaw);
        }

        private static bool TryAim(Pawn pawn, out float yaw)
        {
            yaw = 0f;
            if (TacticalAimUtility.IsAiming(pawn))
            {
                yaw = BallisticGeometry.NormalizeDegrees(TacticalAimUtility.VisualAimAngle(pawn));
                return true;
            }
            if (pawn.stances?.curStance is Stance_Busy busy && busy.focusTarg.IsValid && busy.verb != null
                && !busy.verb.IsMeleeAttack)
            {
                UnityEngine.Vector3 delta = busy.focusTarg.CenterVector3 - pawn.DrawPos;
                if (delta.x * delta.x + delta.z * delta.z < 0.0001f) return false;
                yaw = BallisticGeometry.NormalizeDegrees((float)(Math.Atan2(delta.x, delta.z) * BallisticGeometry.RadToDeg));
                return true;
            }
            return false;
        }
    }

    [StaticConstructorOnStartup]
    internal static class BodyOrientationInjection
    {
        static BodyOrientationInjection()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.race?.Humanlike != true || def.comps?.Any(c => c is CompProperties_BodyOrientation) == true) continue;
                // Corpse defs can share the race's comp list: give the race its own list.
                def.comps = def.comps == null ? new List<CompProperties>() : new List<CompProperties>(def.comps);
                def.comps.Add(new CompProperties_BodyOrientation());
            }
        }
    }

    /// <summary>
    /// Pawn facing is assigned from many places. Filtering the setter keeps the stored Rot4 derived from the
    /// torso yaw for all of them; the requested value is kept as the intent.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_Thing_Rotation_BodyOrientation
    {
        public static MethodBase TargetMethod() => AccessTools.PropertySetter(typeof(Thing), nameof(Thing.Rotation));

        [HarmonyPriority(Priority.Last)]
        public static void Prefix(Thing __instance, ref Rot4 value)
        {
            if (!(__instance is Pawn pawn)) return;
            CompBodyOrientation comp = pawn.TryGetComp<CompBodyOrientation>();
            if (comp != null) value = comp.Filter(value);
        }
    }
}
