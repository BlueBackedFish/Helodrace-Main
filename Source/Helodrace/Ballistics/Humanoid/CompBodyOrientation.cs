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
        public List<ThingDef> weaponDefs;

        public StanceParameters Parameters => new StanceParameters(torsoOffsetDegrees, headOffsetDegrees);

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
    /// Torso and head yaw of a humanlike pawn (design doc section 6.1).
    /// Engaged = aiming a ranged attack or a melee attack at a target (player pawns only while drafted):
    /// torso = aim direction + stance offset, head = aim direction, and the stored Rot4 is the sprite of that
    /// torso, so the side drawn is the side the ballistic hit uses.
    /// Not engaged (undrafted, or drafted without a target): the vanilla four-way facing is used untouched.
    /// Nothing is stored: the yaw is derived from the pawn's current stance each time it is read.
    /// </summary>
    public sealed class CompBodyOrientation : ThingComp
    {
        private float spriteYaw = -1f;
        private bool engaged;
        private float aimYaw;
        private bool writing;
        private StanceProfileDef profile;
        private ThingDef profileWeapon;

        private Pawn Pawn => parent as Pawn;

        /// <summary>True while the continuous yaw is in use.</summary>
        public bool Aiming
        {
            get { Sample(); return engaged; }
        }

        public float AimYaw
        {
            get { Sample(); return aimYaw; }
        }

        public StanceProfileDef Profile
        {
            get { Sample(); return profile; }
        }

        public float TorsoYaw
        {
            get
            {
                Sample();
                return BodyOrientationRules.TorsoYaw(engaged, aimYaw, parent.Rotation.AsAngle, Stance);
            }
        }

        public float HeadYaw
        {
            get
            {
                Sample();
                return BodyOrientationRules.HeadYaw(engaged, aimYaw, parent.Rotation.AsAngle, Stance);
            }
        }

        private StanceParameters Stance => profile?.Parameters ?? default;

        public override void CompTick()
        {
            base.CompTick();
            KeepSprite();
        }

        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);
            KeepSprite();
        }

        /// <summary>While engaged, hold the sprite of the torso even on ticks nobody assigns a rotation.</summary>
        internal void KeepSprite()
        {
            Pawn pawn = Pawn;
            if (pawn == null) return;
            Sample();
            if (!engaged)
            {
                spriteYaw = -1f;
                return;
            }
            Write(pawn, Rot4.FromAngleFlat(Sprite(pawn)));
        }

        /// <summary>
        /// Thing.Rotation setter prefix. Not engaged: the requested facing is stored as is (vanilla). Engaged:
        /// the stored facing is the sprite of the aiming torso.
        /// </summary>
        internal Rot4 Filter(Rot4 requested)
        {
            if (writing) return requested;
            Pawn pawn = Pawn;
            if (pawn == null) return requested;
            Sample();
            if (!engaged)
            {
                spriteYaw = -1f;
                return requested;
            }
            return Rot4.FromAngleFlat(Sprite(pawn));
        }

        private float Sprite(Pawn pawn)
        {
            float torso = BodyOrientationRules.TorsoYaw(true, aimYaw, 0f, Stance);
            spriteYaw = BodyOrientationRules.SpriteYaw(torso, spriteYaw < 0f ? pawn.Rotation.AsAngle : spriteYaw);
            return spriteYaw;
        }

        private void Write(Pawn pawn, Rot4 rotation)
        {
            if (pawn.Rotation == rotation) return;
            writing = true;
            try { pawn.Rotation = rotation; }
            finally { writing = false; }
        }

        /// <summary>Reads the pawn's engagement from its current stance (no caching: stances change mid-tick).</summary>
        private void Sample()
        {
            Pawn pawn = Pawn;
            if (pawn == null)
            {
                engaged = false;
                return;
            }
            ThingDef weapon = pawn.equipment?.Primary?.def;
            if (profile == null || weapon != profileWeapon)
            {
                profileWeapon = weapon;
                profile = StanceProfileDef.For(weapon);
            }
            engaged = TryEngagedAim(pawn, out aimYaw);
        }

        /// <summary>
        /// Aim direction of a pawn that is fighting: the tactical aim angle, or the target of its current ranged
        /// or melee attack stance (warm-up, burst, cooldown). Player pawns must be drafted; pawns that cannot be
        /// drafted (enemies, allies, mental-state pawns) only need the attack target. Shield bearers keep the
        /// vanilla facing: the shield arc is defined on it.
        /// </summary>
        private static bool TryEngagedAim(Pawn pawn, out float yaw)
        {
            yaw = 0f;
            if (!pawn.Spawned || pawn.Dead || pawn.Downed) return false;
            if (pawn.drafter != null && pawn.IsColonistPlayerControlled && !pawn.Drafted) return false;
            if (TacticalAimUtility.IsAiming(pawn))
            {
                yaw = BallisticGeometry.NormalizeDegrees(TacticalAimUtility.VisualAimAngle(pawn));
            }
            else if (pawn.stances?.curStance is Stance_Busy busy && busy.focusTarg.IsValid && busy.verb != null)
            {
                UnityEngine.Vector3 delta = busy.focusTarg.CenterVector3 - pawn.DrawPos;
                if (delta.x * delta.x + delta.z * delta.z < 0.0001f) return false;
                yaw = BallisticGeometry.NormalizeDegrees((float)(Math.Atan2(delta.x, delta.z) * BallisticGeometry.RadToDeg));
            }
            else
            {
                return false;
            }
            return DirectionalBallisticShieldUtility.WornShield(pawn) == null;
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
    /// Pawn facing is assigned from many places. While a pawn is engaged the setter stores the sprite of its
    /// aiming torso for all of them; otherwise the requested value passes through unchanged.
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

    /// <summary>
    /// An attack stance starts or ends the engagement in the middle of a tick: update the sprite at once, so the
    /// drawn side never lags the torso the hit uses.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.SetStance))]
    public static class Patch_PawnStanceTracker_BodyOrientation
    {
        public static void Postfix(Pawn_StanceTracker __instance)
        {
            __instance?.pawn?.TryGetComp<CompBodyOrientation>()?.KeepSprite();
        }
    }
}
