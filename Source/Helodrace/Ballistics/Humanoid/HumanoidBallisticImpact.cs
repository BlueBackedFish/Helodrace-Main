using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using Verse;

namespace Helodrace.Ballistics
{
    /// <summary>
    /// One physical bullet hitting one pawn (Docs/Ballistic_Protection_System.md phase 2). Vanilla owns the layer
    /// iteration (shield PreApplyDamage, then ArmorUtility.ApplyArmor per apparel outer-to-inner, then natural
    /// armour), so this scope carries the penetrator between those callbacks instead of running the pipeline
    /// stages itself. Every layer is recorded in the impact trace.
    /// </summary>
    public sealed class HumanoidBallisticImpact
    {
        [ThreadStatic] internal static HumanoidBallisticImpact Current;

        public static readonly ImpactTraceBuffer RecentTraces = new ImpactTraceBuffer(64);

        internal Pawn pawn;
        internal Projectile projectile;
        internal DamageDef damageDef;
        internal PenetratorClass penetratorClass;
        internal PenetratorState penetrator;
        internal float impactSpeed;
        internal float headingDegrees;
        internal ImpactContext context;
        internal BodyPartRecord part;
        /// <summary>
        /// Wound factor of layers met before the body stack (shields). Vanilla hands apparel comps a copy of the
        /// damage info, so the reduction is applied when GetPostArmorDamage starts.
        /// </summary>
        internal float pendingWoundFactor = 1f;
        private BodyHit hit;
        private BodyPartRecord hitPart;
        private bool hasHit;
        private int layerCount;
        private bool uncoveredNoted;
        internal readonly HashSet<Thing> resolvedThings = new HashSet<Thing>();
        internal readonly HashSet<Apparel> processedHosts = new HashSet<Apparel>();

        /// <summary>True when the round was stopped, or glanced off and left the body.</summary>
        public bool Stopped => penetrator.IsSpent || deflected;

        internal bool deflected;
        /// <summary>World-frame velocity of the glancing round after a ricochet.</summary>
        internal Vector3 deflectedVelocity;
        internal float impactDamage;
        internal float impactPenetration;
        private float hitYaw;

        // Game contract: Projectile.origin is protected; the launch point is a Unity vector.
        private static readonly AccessTools.FieldRef<Projectile, UnityEngine.Vector3> Origin =
            AccessTools.FieldRefAccess<Projectile, UnityEngine.Vector3>("origin");

        internal static HumanoidBallisticImpact For(Pawn pawn, DamageDef damageDef)
        {
            HumanoidBallisticImpact impact = Current;
            return impact != null && impact.pawn == pawn && impact.damageDef == damageDef ? impact : null;
        }

        internal static bool WasResolved(Thing armorThing)
        {
            HumanoidBallisticImpact impact = Current;
            return impact != null && armorThing != null && impact.resolvedThings.Contains(armorThing);
        }

        internal static HumanoidBallisticImpact TryBegin(Bullet bullet, Pawn pawn)
        {
            if (bullet.def.projectile?.damageDef == null) return null;
            float massKg, speed, diameterM;
            PenetratorClass penetratorClass;
            if (bullet is Projectile_Ricochet ricochet)
            {
                massKg = ricochet.massKg;
                speed = ricochet.speedMetersPerSecond;
                diameterM = ricochet.diameterM;
                penetratorClass = ricochet.penetratorClass;
            }
            else
            {
                BulletBallisticsExtension physics = bullet.def.GetModExtension<BulletBallisticsExtension>();
                if (physics == null || !physics.HasPhysics) return null;
                float distance = SpatialMath.HorizontalLength(bullet.ExactPosition.ToNative() - Origin(bullet).ToNative());
                float velocityFactor = bullet is Projectile_BallisticBullet ballistic
                    ? ballistic.ImpactVelocityFactor
                    : BulletBallisticsRules.RetainedVelocity(1f, distance, physics.velocityHalfDistance);
                speed = physics.referenceVelocityMetersPerSecond * velocityFactor;
                massKg = physics.projectileMassGrams * 0.001f;
                diameterM = physics.diameterMillimeters * 0.001f;
                penetratorClass = physics.penetratorClass;
            }
            // Game contract: the projectile's render rotation is its travel heading (0 = north, clockwise).
            float heading = bullet.ExactRotation.eulerAngles.y;
            // Phase 2 fires flat; descent from posture/height arrives with the orientation system (phase 3).
            Vector3 travel = BallisticGeometry.TravelDirection(heading, 0f);
            var penetrator = PenetratorState.Projectile(PenetrationMechanism.KineticBullet,
                massKg, travel * speed, 0f, diameterM);
            int tick = Find.TickManager?.TicksGame ?? 0;
            var impactEvent = new ImpactEvent(BallisticSeed.ForImpact(bullet.thingIDNumber, tick, pawn.thingIDNumber),
                tick, bullet.Launcher?.thingIDNumber ?? -1, pawn.thingIDNumber, bullet.def.defName,
                PenetrationMechanism.KineticBullet, bullet.ExactPosition.ToNative(), penetrator);
            var context = new ImpactContext();
            context.Reset(impactEvent, new BallisticFrame(pawn.DrawPos.ToNative(),
                pawn.TryGetComp<CompBodyOrientation>()?.TorsoYaw ?? pawn.Rotation.AsAngle), pawn);
            return new HumanoidBallisticImpact
            {
                pawn = pawn,
                projectile = bullet,
                damageDef = bullet.def.projectile.damageDef,
                penetratorClass = penetratorClass,
                penetrator = penetrator,
                impactSpeed = speed,
                headingDegrees = heading,
                impactDamage = bullet.DamageAmount,
                impactPenetration = bullet.ArmorPenetration,
                context = context
            };
        }

        /// <summary>Torso yaw from outside the pawn (crew seat in a vehicle frame); NaN = the pawn's own orientation.</summary>
        internal float yawOverride = float.NaN;

        /// <summary>
        /// A physical hit without a flying projectile (behind-armour fragments, a residual penetrator inside a vehicle):
        /// the pawn's worn layers resolve it like a bullet. massKg, speed and diameter describe the striking body;
        /// headingDegrees is its travel in the world, torsoYaw the pawn's facing. Returns the damage dealt.
        /// </summary>
        public static void ApplyPhysicalHit(Pawn pawn, Thing instigator, float damage, PenetratorClass penetratorClass,
            float massKg, float speed, float diameterM, float headingDegrees, float torsoYaw, ulong seed, string label)
        {
            if (pawn == null || pawn.Dead || damage <= 0f) return;
            Vector3 travel = BallisticGeometry.TravelDirection(headingDegrees, 0f);
            var penetrator = PenetratorState.Projectile(PenetrationMechanism.Fragment, massKg, travel * speed, 0f, diameterM);
            int tick = Find.TickManager?.TicksGame ?? 0;
            var impactEvent = new ImpactEvent(seed, tick, instigator?.thingIDNumber ?? -1, pawn.thingIDNumber, label,
                PenetrationMechanism.Fragment, Vector3.Zero, penetrator);
            var context = new ImpactContext();
            context.Reset(impactEvent, new BallisticFrame(Vector3.Zero, torsoYaw), pawn);
            var impact = new HumanoidBallisticImpact
            {
                pawn = pawn,
                damageDef = DamageDefOf.Bullet,
                penetratorClass = penetratorClass,
                penetrator = penetrator,
                impactSpeed = speed,
                headingDegrees = headingDegrees,
                impactDamage = damage,
                impactPenetration = 0f,
                context = context,
                yawOverride = torsoYaw
            };
            HumanoidBallisticImpact previous = Current;
            Current = impact;
            try
            {
                pawn.TakeDamage(new DamageInfo(DamageDefOf.Bullet, damage, 0f, headingDegrees, instigator));
                impact.Finish();
            }
            finally { Current = previous; }
        }

        internal readonly List<(Apparel host, bool perforated)> pendingCrazing = new List<(Apparel, bool)>();

        internal void Finish()
        {
            foreach (var crazing in pendingCrazing) VisorCrazingUtility.Craze(pawn, crazing.host, crazing.perforated);
            pendingCrazing.Clear();
            // Record impacts that met a physical layer, or a helmet that did not cover the hit point.
            if (layerCount == 0 && !uncoveredNoted) return;
            context.Outcome.Result = layerCount == 0 ? ImpactResult.None
                : deflected ? ImpactResult.Ricochet
                : Stopped ? ImpactResult.Stopped : ImpactResult.Perforation;
            context.Trace.Result = context.Outcome.Result;
            RecentTraces.Add(context.Trace);
            if (deflected && projectile != null) Projectile_Ricochet.TryLaunch(this);
        }

        /// <summary>Continuous torso yaw (phase 3); pawns without the comp use their Rot4 heading.</summary>
        internal float TorsoYaw => !float.IsNaN(yawOverride) ? yawOverride : pawn.TryGetComp<CompBodyOrientation>()?.TorsoYaw ?? pawn.Rotation.AsAngle;

        internal float HeadYaw => !float.IsNaN(yawOverride) ? yawOverride : pawn.TryGetComp<CompBodyOrientation>()?.HeadYaw ?? pawn.Rotation.AsAngle;

        /// <summary>Travel direction in the torso frame (+Z = chest). Shields are held toward the torso facing.</summary>
        internal Vector3 LocalTravel => TravelIn(TorsoYaw);

        private Vector3 TravelIn(float yaw) => new BallisticFrame(Vector3.Zero, yaw)
            .ToLocalDirection(BallisticGeometry.TravelDirection(headingDegrees, 0f));

        /// <summary>
        /// Impact point on the hit part, in the frame of the part: the head turns with the head yaw, everything
        /// else with the torso yaw. The Rot4 sprite is derived from the same torso yaw (CompBodyOrientation).
        /// </summary>
        internal BodyHit HitOn(BodyPartRecord bodyPart)
        {
            if (hasHit && hitPart == bodyPart) return hit;
            bool head = IsHead(bodyPart);
            float yaw = head ? HeadYaw : TorsoYaw;
            Vector3 localTravel = TravelIn(yaw);
            BallisticRng rng = context.StageRandom(ImpactStageIds.HitLocation);
            hit = BodyHitGeometry.Sample(ShapeOf(bodyPart), localTravel, ref rng);
            hitYaw = yaw;
            hitPart = bodyPart;
            hasHit = true;
            context.Trace.Enter(ImpactStageIds.HitLocation, TraceStageStatus.Ran);
            context.Trace.Note(bodyPart?.def?.defName ?? "none");
            context.Trace.Value(head ? "headYawDeg" : "torsoYawDeg", yaw, EvidenceGrade.Game);
            context.Trace.Value("azimuthDeg", hit.AzimuthDegrees, EvidenceGrade.Physical);
            context.Trace.Value("height", hit.HeightFraction, EvidenceGrade.Physical);
            context.Trace.Value("surfaceObliquityDeg", hit.SurfaceObliquity * BallisticGeometry.RadToDeg, EvidenceGrade.Physical);
            context.Trace.Leave();
            return hit;
        }

        /// <summary>Yaw of the frame the last hit point was sampled in (head or torso).</summary>
        internal float HitFrameYaw => hitYaw;

        /// <summary>Face parts sit in a helmet's open window; only a visor covers them.</summary>
        internal static bool IsFace(BodyPartRecord bodyPart)
        {
            string name = bodyPart?.def?.defName;
            return name == "Eye" || name == "Nose" || name == "Jaw";
        }

        internal static bool IsHead(BodyPartRecord bodyPart)
        {
            for (BodyPartRecord current = bodyPart; current != null; current = current.parent)
            {
                string name = current.def?.defName;
                if (name == "Head" || name == "Skull") return true;
                if (name == "Neck" || name == "Torso") return false;
            }
            return false;
        }

        private static Vector3 ShapeOf(BodyPartRecord bodyPart)
        {
            for (BodyPartRecord current = bodyPart; current != null; current = current.parent)
            {
                string name = current.def?.defName;
                if (name == "Torso") return BodyHitGeometry.Torso;
                if (name == "Head" || name == "Skull") return BodyHitGeometry.Head;
                if (name == "Neck") return BodyHitGeometry.Neck;
            }
            return BodyHitGeometry.Limb;
        }

        /// <summary>
        /// Runs the bullet through one layup and applies the result to the vanilla damage amount.
        /// Returns the absorbed energy (J) for the caller's durability bookkeeping.
        /// </summary>
        internal float ResolveLayup(ArmorLayupDef layup, float obliquity, float effectiveness, string label,
            ref float damageAmount, ref DamageDef currentDamageDef, out bool stopped, out int stoppingLayer,
            Vector3? strikeNormalLocal = null)
        {
            stopped = false;
            stoppingLayer = -1;
            if (Stopped || layup == null || layup.KineticLayers.Count == 0) return 0f;

            layerCount++;
            context.Trace.Enter(ImpactStageIds.LayerInteraction, TraceStageStatus.Ran);
            context.Trace.Note(label);
            context.Trace.Value(label + ".effectiveness", effectiveness, EvidenceGrade.Game);
            float speedIn = penetrator.Speed;

            // Strike face of a curved shell or a metal face: the round may glance off before penetrating.
            KineticMaterial face = layup.KineticLayers[0].Material;
            if (strikeNormalLocal.HasValue && face.RicochetCriticalDegrees > 0f)
            {
                Vector3 worldNormal = new BallisticFrame(Vector3.Zero, hitYaw).ToWorldDirection(strikeNormalLocal.Value);
                BallisticRng rng = context.StageRandom(label + ".ricochet");
                bool glanced = KineticBulletModel.TryRicochet(penetrator, worldNormal, face, ref rng,
                    out PenetratorState after, out float glanceJ, out float probability);
                context.Trace.Value(label + ".ricochetProbability", probability, EvidenceGrade.Game);
                if (glanced)
                {
                    float energyIn = penetrator.KineticEnergyJ;
                    deflected = true;
                    deflectedVelocity = after.Velocity;
                    penetrator = after;
                    stopped = true;
                    stoppingLayer = 0;
                    // Blunt trauma from the share of energy the shell took. [game]
                    float blunt = GenMath.RoundRandom(damageAmount * face.TraumaTransfer
                        * (energyIn > 0f ? glanceJ / energyIn : 0f));
                    damageAmount = blunt;
                    if (blunt > 0f && currentDamageDef.armorCategory == DamageArmorCategoryDefOf.Sharp)
                    {
                        currentDamageDef = DamageDefOf.Blunt;
                    }
                    context.Trace.Note("ricochet");
                    context.Trace.Value(label + ".ricochetSpeed", after.Speed, EvidenceGrade.Game);
                    context.Trace.Value(label + ".trauma", blunt, EvidenceGrade.Game);
                    context.Trace.Value(label + ".absorbedJ", glanceJ, EvidenceGrade.Physical);
                    context.Trace.Leave();
                    return glanceJ;
                }
            }
            KineticLayupResult result = KineticBulletModel.ResolveLayup(penetrator, penetratorClass,
                layup.KineticLayers, obliquity, effectiveness, context.Event.Seed, context.Trace, label);
            penetrator = result.Out;
            stoppingLayer = result.StoppingLayer;
            if (result.Perforated)
            {
                float factor = KineticBulletModel.WoundFactor(result.EnergyIn, result.EnergyOut);
                damageAmount *= factor;
                context.Trace.Value(label + ".woundFactor", factor, EvidenceGrade.Game);
                BulletFragmentationImpact fragmentation = BulletFragmentationImpact.Current;
                if (fragmentation != null && fragmentation.pawn == pawn && speedIn > 0f)
                {
                    // Fragmentation reads the slowed round instead of summed armour ratings.
                    fragmentation.velocityFactor *= penetrator.Speed / speedIn;
                }
            }
            else
            {
                stopped = true;
                float trauma = layup.TraumaTransferOf(result.StoppingLayer);
                float blunt = GenMath.RoundRandom(damageAmount * trauma);
                context.Trace.Value(label + ".trauma", blunt, EvidenceGrade.Game);
                damageAmount = blunt;
                if (blunt > 0f && currentDamageDef.armorCategory == DamageArmorCategoryDefOf.Sharp)
                {
                    currentDamageDef = DamageDefOf.Blunt;
                }
            }
            context.Trace.Value(label + ".absorbedJ", result.AbsorbedJ, EvidenceGrade.Physical);
            context.Trace.Leave();
            return result.AbsorbedJ;
        }

        /// <summary>Penetration factor for layers that keep the vanilla roll after physical layers slowed the round.</summary>
        internal float VanillaPenetrationFactor => KineticBulletModel.VanillaPenetrationFactor(impactSpeed, penetrator.Speed);

        internal void NoteUncovered(string label)
        {
            uncoveredNoted = true;
            context.Trace.Enter("Uncovered", TraceStageStatus.Skipped);
            context.Trace.Note(label);
            context.Trace.Leave();
        }

        internal void NoteVanillaLayer(string label)
        {
            context.Trace.Enter("VanillaArmor", TraceStageStatus.Ran);
            context.Trace.Note(label);
            context.Trace.Value("apFactor", VanillaPenetrationFactor, EvidenceGrade.Game);
            context.Trace.Leave();
        }
    }

    [HarmonyPatch(typeof(Bullet), "Impact")]
    public static class Patch_Bullet_BallisticImpactScope
    {
        public static void Prefix(Bullet __instance, Thing hitThing, bool blockedByShield,
            out HumanoidBallisticImpact __state)
        {
            __state = HumanoidBallisticImpact.Current;
            if (__state?.projectile == __instance)
            {
                return; // A subclass Impact already opened the scope through base.Impact.
            }
            HumanoidBallisticImpact.Current = !blockedByShield && hitThing is Pawn pawn && !(__instance is Projectile_ShotgunPellet)
                ? HumanoidBallisticImpact.TryBegin(__instance, pawn)
                : null;
        }

        public static Exception Finalizer(Exception __exception, Bullet __instance, HumanoidBallisticImpact __state)
        {
            HumanoidBallisticImpact current = HumanoidBallisticImpact.Current;
            if (current != null && current != __state && current.projectile == __instance)
            {
                current.Finish();
            }
            HumanoidBallisticImpact.Current = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ArmorUtility), nameof(ArmorUtility.GetPostArmorDamage))]
    public static class Patch_ArmorUtility_BallisticHitPart
    {
        public static void Prefix(Pawn pawn, ref float amount, BodyPartRecord part, DamageDef damageDef)
        {
            HumanoidBallisticImpact impact = HumanoidBallisticImpact.For(pawn, damageDef);
            if (impact == null) return;
            impact.part = part;
            amount *= impact.pendingWoundFactor;
            impact.pendingWoundFactor = 1f;
        }
    }

    /// <summary>
    /// Physical layers inside vanilla's apparel loop. A plate carrier resolves its installed plates first (they sit
    /// outside its soft armour), then itself. Apparel without a layup keeps the vanilla roll with penetration
    /// scaled to the slowed round.
    /// </summary>
    [HarmonyPatch(typeof(ArmorUtility), "ApplyArmor")]
    public static class Patch_ArmorUtility_BallisticLayers
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(ref float damAmount, ref float armorPenetration, Thing armorThing,
            ref DamageDef damageDef, Pawn pawn, ref bool metalArmor)
        {
            HumanoidBallisticImpact impact = HumanoidBallisticImpact.Current;
            if (impact == null || impact.pawn != pawn || impact.part == null
                || damageDef.armorCategory != DamageArmorCategoryDefOf.Sharp)
            {
                return true;
            }
            if (impact.Stopped)
            {
                // Remaining layers only see the blunt trauma; vanilla handles it with its own roll.
                return true;
            }

            if (armorThing is Apparel apparel)
            {
                if (apparel.TryGetComp<CompDirectionalBallisticShield>() != null)
                {
                    armorPenetration *= impact.VanillaPenetrationFactor;
                    return true; // Shields were resolved in PreApplyDamage.
                }

                CompModularArmor modular = apparel.TryGetComp<CompModularArmor>();
                if (modular != null && impact.processedHosts.Add(apparel))
                {
                    bool partsStopped = BallisticModularArmor.ResolveInstalledParts(impact, apparel, modular,
                        armorPenetration, ref damAmount, ref damageDef, ref metalArmor);
                    if (partsStopped && damAmount <= 0f)
                    {
                        return false;
                    }
                    if (impact.Stopped)
                    {
                        return true;
                    }
                }

                BallisticLayupExtension extension = apparel.def.GetModExtension<BallisticLayupExtension>();
                ArmorLayupDef layup = extension?.layup;
                if (layup != null)
                {
                    BodyHit hit = impact.HitOn(impact.part);
                    Vector3? strikeNormal = null;
                    if (extension.shell != null)
                    {
                        // A helmet shell covers the head above its brim, except the open face.
                        if (!HumanoidBallisticImpact.IsHead(impact.part) || HumanoidBallisticImpact.IsFace(impact.part)
                            || !extension.shell.Model.Contains(hit.AzimuthDegrees, hit.HeightFraction))
                        {
                            impact.NoteUncovered(apparel.def.defName);
                            return false;
                        }
                        strikeNormal = hit.LocalNormal;
                    }
                    else if (layup.KineticLayers[0].Material.Family == ArmorMaterialFamily.Metal)
                    {
                        strikeNormal = hit.LocalNormal;
                    }
                    float absorbed = impact.ResolveLayup(layup, hit.SurfaceObliquity, 1f, apparel.def.defName,
                        ref damAmount, ref damageDef, out bool stopped, out int stoppingLayer, strikeNormal);
                    impact.resolvedThings.Add(apparel);
                    metalArmor = stopped && (layup.metallicEffect || apparel.def.apparel.useDeflectMetalEffect);
                    // Same durability contract as other worn armour: wear from the absorbed energy.
                    int wear = GenMath.RoundRandom(absorbed * layup.WearPerJouleOf(stoppingLayer));
                    if (wear > 0)
                    {
                        apparel.TakeDamage(new DamageInfo(impact.damageDef, wear));
                    }
                    return false;
                }
            }

            armorPenetration *= impact.VanillaPenetrationFactor;
            impact.NoteVanillaLayer(armorThing?.def?.defName ?? "natural");
            return true;
        }
    }

    internal static class BallisticModularArmor
    {
        internal static float DirectionCenter(ModularArmorFacing facing)
        {
            if (facing == ModularArmorFacing.Right) return 90f;
            if (facing == ModularArmorFacing.Back) return 180f;
            if (facing == ModularArmorFacing.Left) return -90f;
            return 0f;
        }

        internal static CoverageRegion RegionOf(ModularArmorPositionDef position)
        {
            float[] centers = null;
            if (!position.protectedDirections.NullOrEmpty())
            {
                centers = new float[position.protectedDirections.Count];
                for (int i = 0; i < centers.Length; i++) centers[i] = DirectionCenter(position.protectedDirections[i]);
            }
            return new CoverageRegion(centers, position.directionArcDegrees,
                position.coverageHeight.min, position.coverageHeight.max);
        }

        /// <summary>Plate effectiveness from the facing plate's durability. [game]</summary>
        internal static float PlateEffectiveness(float healthRatio) => 0.4f + 0.6f * Math.Clamp(healthRatio, 0f, 1f);

        /// <summary>
        /// Resolves the physical parts of one carrier that cover the hit point. Parts without a layup keep the
        /// vanilla roll. Returns true when a part stopped the round.
        /// </summary>
        internal static bool ResolveInstalledParts(HumanoidBallisticImpact impact, Apparel host, CompModularArmor comp,
            float armorPenetration, ref float damAmount, ref DamageDef damageDef, ref bool metalArmor)
        {
            bool stoppedHere = false;
            IReadOnlyList<InstalledModularArmorPart> installed = comp.InstalledParts;
            for (int i = 0; i < installed.Count && damAmount > 0f && !impact.Stopped; i++)
            {
                InstalledModularArmorPart record = installed[i];
                ModularArmorPositionDef position = record?.EffectivePosition;
                if (record?.part == null || position == null || !position.Covers(impact.part))
                {
                    continue;
                }

                BodyHit hit = impact.HitOn(impact.part);
                CoverageRegion region = RegionOf(position);
                if (!region.Contains(hit.AzimuthDegrees, hit.HeightFraction, out int centerIndex))
                {
                    continue;
                }

                ArmorLayupDef layup = record.part.plateThingDef?.GetModExtension<BallisticLayupExtension>()?.layup
                    ?? record.part.ballisticLayup;
                if (layup == null)
                {
                    float rating = record.part.ArmorFor(damageDef.armorCategory);
                    if (rating > 0f)
                    {
                        bool metal = record.part.metallic;
                        impact.NoteVanillaLayer(record.part.defName);
                        ArmorUtilityReversePatch.ApplyArmor(ref damAmount, impact.VanillaPenetrationFactor
                            * armorPenetration, rating, host, ref damageDef, impact.pawn, ref metal);
                        BulletFragmentationImpact.RecordArmor(impact.pawn, rating, damAmount, damageDef);
                        if (damAmount <= 0f) { metalArmor |= metal; stoppedHere = true; }
                    }
                    continue;
                }

                float obliquity = hit.SurfaceObliquity;
                float effectiveness = 1f;
                ModularArmorFacing facing = ModularArmorFacing.Front;
                bool plate = record.part.plateThingDef != null;
                if (plate)
                {
                    facing = centerIndex >= 0 ? position.protectedDirections[centerIndex] : ModularArmorFacing.Front;
                    if (record.PlateForFacing(facing) == null)
                    {
                        continue;
                    }
                    effectiveness = PlateEffectiveness(record.PlateHealthRatioForFacing(facing));
                }
                // Phase 11: ceramic plates remember where they were hit (3 x 3 crack grid). [game]
                CompArmorPlateSet plateSet = plate ? record.PlateSet?.TryGetComp<CompArmorPlateSet>() : null;
                bool ceramic = plateSet != null && layup.KineticLayers.Any(l => l.Material.Family == ArmorMaterialFamily.Ceramic);
                int plateNumber = plateSet != null ? record.PlateNumberForFacing(facing) : 0;
                int crackCell = -1;
                float[] cracks = null;
                if (ceramic && centerIndex >= 0)
                {
                    float offset = BallisticGeometry.DeltaDegrees(DirectionCenter(position.protectedDirections[centerIndex]), hit.AzimuthDegrees);
                    crackCell = CrackGrid.CellOf(offset, region.HalfArcDegrees, hit.HeightFraction, region.HeightMin, region.HeightMax);
                    cracks = plateSet.CracksFor(plateNumber);
                    float crackFactor = CrackGrid.Effectiveness(cracks, crackCell);
                    effectiveness *= crackFactor;
                    impact.context.Trace.Enter("CrackGrid", TraceStageStatus.Ran);
                    impact.context.Trace.Value(record.part.defName + ".crackCell", crackCell, EvidenceGrade.Game);
                    impact.context.Trace.Value(record.part.defName + ".crackFactor", crackFactor, EvidenceGrade.Game);
                    impact.context.Trace.Leave();
                }
                if (centerIndex >= 0)
                {
                    // Plates and directional panels are flat: obliquity against the panel, not the body surface.
                    obliquity = hit.PlateObliquity(DirectionCenter(position.protectedDirections[centerIndex]));
                }

                float absorbed = impact.ResolveLayup(layup, obliquity, effectiveness, record.part.defName,
                    ref damAmount, ref damageDef, out bool stopped, out int stoppingLayer);
                if (cracks != null)
                {
                    CrackGrid.Record(cracks, crackCell, !stopped);
                    plateSet.SetCracks(plateNumber, cracks);
                }
                // Applied after the impact: adding a hediff mid-damage disturbs vanilla's injury pass.
                if (layup.KineticLayers.Any(l => VisorCrazingUtility.Transparent(l.Material.Id)))
                    impact.pendingCrazing.Add((host, !stopped));
                if (plate)
                {
                    float coefficient = record.PlateSet?.TryGetComp<CompArmorPlateSet>()?.DurabilityMaterialCoefficient ?? 1f;
                    record.DamagePlateForFacing(facing,
                        GenMath.RoundRandom(absorbed * coefficient * layup.WearPerJouleOf(stoppingLayer)), impact.damageDef);
                }
                if (stopped)
                {
                    metalArmor |= layup.metallicEffect || record.part.metallic;
                    stoppedHere = true;
                }
            }
            return stoppedHere;
        }
    }
}
