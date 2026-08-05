using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    public class CompProperties_DirectionalBallisticShield : CompProperties
    {
        public float frontalArcDegrees = 90f;
        public float sharpArmorRating = 2f;
        public float rangedAccuracyMultiplier = 0.75f;
        public float minimumRangedAccuracyMultiplier = 0.35f;
        public float viewportHitChance = 0.08f;
        public float viewportDamagePerHit = 0.08f;
        public float durabilityFailureThreshold = 0.5f;
        public float durabilityDamageOnBlock = 0.5f;
        public float durabilityDamageOnPenetrate = 1f;
        public float sidewaysMovementSpeedMultiplier = 0.65f;
        public float backwardMovementSpeedMultiplier = 0.45f;
        public float stationaryAllyCoverBlockChance = 0.75f;
        public List<string> allowedWeaponDefs;

        public CompProperties_DirectionalBallisticShield()
        {
            compClass = typeof(CompDirectionalBallisticShield);
        }
    }

    public class CompDirectionalBallisticShield : ThingComp
    {
        private bool processingShieldDamage;
        private float viewportDamage;
        private bool forcedFacingActive;
        private IntVec3 forcedFacingCell = IntVec3.Invalid;

        public CompProperties_DirectionalBallisticShield Props =>
            (CompProperties_DirectionalBallisticShield)props;

        public Apparel Shield => parent as Apparel;

        public Pawn Wearer => Shield?.Wearer;

        public float ViewportDamage => Mathf.Clamp01(viewportDamage);

        public bool ForcedFacingActive => forcedFacingActive
            && forcedFacingCell.IsValid
            && Wearer != null;

        public bool UsesWeaponWhitelist => !Props.allowedWeaponDefs.NullOrEmpty();

        public float CurrentRangedAccuracyMultiplier => Mathf.Lerp(
            Props.rangedAccuracyMultiplier,
            Props.minimumRangedAccuracyMultiplier,
            ViewportDamage);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref viewportDamage, "ironHideViewportDamage", 0f);
            Scribe_Values.Look(ref forcedFacingActive, "ironHideForcedFacingActive", false);
            Scribe_Values.Look(
                ref forcedFacingCell,
                "ironHideForcedFacingCell",
                IntVec3.Invalid);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                viewportDamage = Mathf.Clamp01(viewportDamage);
                if (!forcedFacingCell.IsValid)
                {
                    forcedFacingActive = false;
                }
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            ClearForcedFacing();
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            EnforceWeaponWhitelist(pawn);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            EnforceWeaponWhitelist(Wearer);
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn pawn = Wearer;
            if (pawn == null || pawn.Faction != Faction.OfPlayer || !pawn.Spawned)
            {
                yield break;
            }

            yield return new Command_Toggle
            {
                defaultLabel = "HD_IronHideShield_ForcedFacing_Label".Translate().ToString(),
                defaultDesc = "HD_IronHideShield_ForcedFacing_Desc".Translate().ToString(),
                icon = TexCommand.Attack,
                isActive = () => ForcedFacingActive,
                toggleAction = () =>
                {
                    if (ForcedFacingActive)
                    {
                        ClearForcedFacing();
                    }
                    else
                    {
                        BeginForcedFacingTargeting();
                    }
                }
            };
        }

        public override string CompInspectStringExtra()
        {
            if (ViewportDamage <= 0f)
            {
                return "HD_IronHideShield_ViewportIntact".Translate();
            }

            return "HD_IronHideShield_ViewportStatus".Translate(
                ViewportDamage.ToStringPercent(),
                CurrentRangedAccuracyMultiplier.ToStringPercent());
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            yield return new StatDrawEntry(
                StatCategoryDefOf.Apparel,
                "HD_IronHideShield_ViewportDamage".Translate().Resolve(),
                ViewportDamage.ToStringPercent(),
                "HD_IronHideShield_ViewportDamage_Desc".Translate().Resolve(),
                5500);

            yield return new StatDrawEntry(
                StatCategoryDefOf.Apparel,
                "HD_IronHideShield_CurrentAccuracy".Translate().Resolve(),
                CurrentRangedAccuracyMultiplier.ToStringPercent(),
                "HD_IronHideShield_CurrentAccuracy_Desc".Translate().Resolve(),
                5499);

            yield return new StatDrawEntry(
                StatCategoryDefOf.Apparel,
                "HD_IronHideShield_AllyCover".Translate().Resolve(),
                Props.stationaryAllyCoverBlockChance.ToStringPercent(),
                "HD_IronHideShield_AllyCover_Desc".Translate().Resolve(),
                5498);

            if (UsesWeaponWhitelist)
            {
                yield return new StatDrawEntry(
                    StatCategoryDefOf.Apparel,
                    "HD_BallisticShield_AllowedWeapons".Translate().Resolve(),
                    AllowedWeaponLabels(),
                    "HD_BallisticShield_AllowedWeapons_Desc".Translate().Resolve(),
                    5497);
            }
        }

        public bool IsWeaponAllowed(ThingDef weaponDef)
        {
            return !UsesWeaponWhitelist
                || weaponDef != null
                    && Props.allowedWeaponDefs.Contains(weaponDef.defName);
        }

        private string AllowedWeaponLabels()
        {
            List<string> labels = new List<string>();
            foreach (string defName in Props.allowedWeaponDefs)
            {
                ThingDef weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                labels.Add(weaponDef?.LabelCap.ToString() ?? defName);
            }

            return labels.ToCommaList(false);
        }

        private void EnforceWeaponWhitelist(Pawn pawn)
        {
            ThingWithComps weapon = pawn?.equipment?.Primary;
            if (!UsesWeaponWhitelist
                || weapon == null
                || IsWeaponAllowed(weapon.def)
                || !pawn.Spawned)
            {
                return;
            }

            if (pawn.equipment.TryDropEquipment(
                weapon,
                out ThingWithComps droppedWeapon,
                pawn.Position,
                false)
                && PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                Messages.Message(
                    "HD_BallisticShield_WeaponDropped".Translate(
                        droppedWeapon.LabelCap,
                        parent.LabelCap),
                    pawn,
                    MessageTypeDefOf.CautionInput,
                    false);
            }
        }

        public float MovementSpeedMultiplier(IntVec3 from, IntVec3 to)
        {
            if (!ForcedFacingActive || !from.IsValid || !to.IsValid || from == to)
            {
                return 1f;
            }

            if (!TryGetForcedRotation(from, out Rot4 forcedRotation))
            {
                return 1f;
            }

            float movementAngle = (to - from).AngleFlat;
            float angleDifference = Mathf.Abs(
                Mathf.DeltaAngle(forcedRotation.AsAngle, movementAngle));
            if (angleDifference >= 134.5f)
            {
                return Mathf.Clamp(Props.backwardMovementSpeedMultiplier, 0.1f, 1f);
            }

            if (angleDifference > 45.5f)
            {
                return Mathf.Clamp(Props.sidewaysMovementSpeedMultiplier, 0.1f, 1f);
            }

            return 1f;
        }

        public void ApplyForcedFacing(Pawn pawn)
        {
            if (ForcedFacingActive
                && pawn != null
                && !pawn.Dead
                && !pawn.Downed
                && TryGetForcedRotation(pawn.Position, out Rot4 forcedRotation))
            {
                pawn.Rotation = forcedRotation;
            }
        }

        private bool TryGetForcedRotation(IntVec3 origin, out Rot4 rotation)
        {
            if (ForcedFacingActive && origin.IsValid && forcedFacingCell != origin)
            {
                rotation = Pawn_RotationTracker.RotFromAngleBiased(
                    (forcedFacingCell - origin).AngleFlat);
                return true;
            }

            rotation = Rot4.Invalid;
            return false;
        }

        private void BeginForcedFacingTargeting()
        {
            Pawn pawn = Wearer;
            if (pawn?.Map == null)
            {
                return;
            }

            TargetingParameters parameters = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetBuildings = true,
                canTargetItems = true,
                canTargetPawns = true,
                validator = target => target.Cell.InBounds(pawn.Map)
                    && target.Cell != pawn.Position
            };
            Find.Targeter.BeginTargeting(parameters, target =>
            {
                Pawn currentWearer = Wearer;
                if (currentWearer?.Map != pawn.Map
                    || !target.Cell.InBounds(pawn.Map)
                    || target.Cell == currentWearer.Position)
                {
                    return;
                }

                forcedFacingCell = target.Cell;
                forcedFacingActive = true;
                ApplyForcedFacing(currentWearer);
            });
        }

        private void ClearForcedFacing()
        {
            forcedFacingActive = false;
            forcedFacingCell = IntVec3.Invalid;
        }

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            if (processingShieldDamage)
            {
                return;
            }

            Pawn pawn = Wearer;
            if (!CanAttemptBlock(pawn, dinfo) || !IsWithinFrontalArc(pawn, dinfo))
            {
                return;
            }

            TryDamageViewport(pawn);

            float effectiveArmor = Mathf.Max(Props.sharpArmorRating - dinfo.ArmorPenetrationInt, 0f);
            float durabilityRatio = parent.MaxHitPoints > 0
                ? Mathf.Clamp01((float)parent.HitPoints / parent.MaxHitPoints)
                : 0f;
            float failureThreshold = Mathf.Clamp01(Props.durabilityFailureThreshold);
            float durabilityFactor = failureThreshold <= 0f
                || durabilityRatio >= failureThreshold
                ? 1f
                : durabilityRatio / failureThreshold;
            float blockChance = Mathf.Clamp01(effectiveArmor) * durabilityFactor;
            bool blocked = Rand.Value < blockChance;
            DamageShield(dinfo, blocked
                ? Props.durabilityDamageOnBlock
                : Props.durabilityDamageOnPenetrate);

            if (!blocked)
            {
                if (pawn.Spawned)
                {
                    MoteMaker.ThrowText(
                        pawn.DrawPos,
                        pawn.Map,
                        "HD_BallisticShield_Penetrated".Translate(),
                        1.9f);
                }

                return;
            }

            absorbed = true;
            if (pawn.Spawned)
            {
                pawn.Drawer.Notify_DamageApplied(dinfo);
                EffecterDefOf.Deflect_Metal.Spawn().Trigger(pawn, dinfo.Instigator ?? pawn);
            }
        }

        private void TryDamageViewport(Pawn pawn)
        {
            if (!Rand.Chance(Mathf.Clamp01(Props.viewportHitChance)))
            {
                return;
            }

            viewportDamage = Mathf.Clamp01(
                viewportDamage + Mathf.Max(Props.viewportDamagePerHit, 0f));
            if (pawn.Spawned)
            {
                MoteMaker.ThrowText(
                    pawn.DrawPos,
                    pawn.Map,
                    "HD_IronHideShield_ViewportHit".Translate(),
                    1.9f);
            }
        }

        private static bool CanAttemptBlock(Pawn pawn, DamageInfo dinfo)
        {
            if (pawn == null
                || pawn.Dead
                || pawn.Downed
                || dinfo.IgnoreArmor
                || dinfo.Def == null
                || dinfo.Def.isExplosive
                || dinfo.Def.ignoreShields
                || dinfo.Def.armorCategory != DamageArmorCategoryDefOf.Sharp)
            {
                return false;
            }

            return dinfo.Def == DamageDefOf.Bullet
                || dinfo.Def.isRanged
                || dinfo.Weapon?.IsRangedWeapon == true;
        }

        private bool IsWithinFrontalArc(Pawn pawn, DamageInfo dinfo)
        {
            if (!TryGetDirectionTowardAttacker(pawn, dinfo, out float attackerDirection))
            {
                return false;
            }

            float halfArc = Mathf.Clamp(Props.frontalArcDegrees, 0f, 360f) * 0.5f;
            return Mathf.Abs(Mathf.DeltaAngle(pawn.Rotation.AsAngle, attackerDirection)) <= halfArc;
        }

        private static bool TryGetDirectionTowardAttacker(
            Pawn pawn,
            DamageInfo dinfo,
            out float attackerDirection)
        {
            if (dinfo.Angle >= 0f)
            {
                attackerDirection = Mathf.Repeat(dinfo.Angle + 180f, 360f);
                return true;
            }

            Thing source = dinfo.Instigator;
            if (source != null
                && source.PositionHeld.IsValid
                && source.PositionHeld != pawn.PositionHeld)
            {
                attackerDirection = (source.PositionHeld - pawn.PositionHeld).AngleFlat;
                return true;
            }

            attackerDirection = 0f;
            return false;
        }

        private void DamageShield(DamageInfo incomingDamage, float factor)
        {
            float amount = Mathf.Max(1f, incomingDamage.Amount * Mathf.Max(factor, 0f));
            processingShieldDamage = true;
            try
            {
                parent.TakeDamage(new DamageInfo(
                    DamageDefOf.Deterioration,
                    amount,
                    0f,
                    incomingDamage.Angle,
                    incomingDamage.Instigator,
                    weapon: incomingDamage.Weapon));
            }
            finally
            {
                processingShieldDamage = false;
            }
        }
    }

    public static class DirectionalBallisticShieldUtility
    {
        public static CompDirectionalBallisticShield WornShield(Pawn pawn)
        {
            if (pawn?.apparel?.WornApparel == null)
            {
                return null;
            }

            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                CompDirectionalBallisticShield shield =
                    apparel?.TryGetComp<CompDirectionalBallisticShield>();
                if (shield != null)
                {
                    return shield;
                }
            }

            return null;
        }

        public static bool TryFindStationaryShieldCover(
            Pawn protectedPawn,
            IntVec3 threatCell,
            out Pawn shieldBearer,
            out CompDirectionalBallisticShield shield)
        {
            shieldBearer = null;
            shield = null;
            if (protectedPawn?.Map == null
                || !protectedPawn.Spawned
                || !threatCell.IsValid
                || threatCell == protectedPawn.Position)
            {
                return false;
            }

            Map map = protectedPawn.Map;
            float threatAngle = (threatCell - protectedPawn.Position).AngleFlat;
            int protectedDistanceSquared =
                protectedPawn.Position.DistanceToSquared(threatCell);

            foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(protectedPawn))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                foreach (Thing thing in map.thingGrid.ThingsListAtFast(cell))
                {
                    Pawn candidate = thing as Pawn;
                    if (candidate == null
                        || candidate == protectedPawn
                        || candidate.Dead
                        || candidate.Downed
                        || candidate.Faction != protectedPawn.Faction
                        || !candidate.Spawned
                        || candidate.pather?.Moving == true
                        || candidate.Position.DistanceToSquared(threatCell)
                            >= protectedDistanceSquared)
                    {
                        continue;
                    }

                    float coverDirection =
                        (candidate.Position - protectedPawn.Position).AngleFlat;
                    if (Mathf.Abs(Mathf.DeltaAngle(threatAngle, coverDirection)) > 45.5f)
                    {
                        continue;
                    }

                    CompDirectionalBallisticShield candidateShield =
                        WornShield(candidate);
                    if (candidateShield == null)
                    {
                        continue;
                    }

                    float directionTowardThreat =
                        (threatCell - candidate.Position).AngleFlat;
                    float halfArc = Mathf.Clamp(
                        candidateShield.Props.frontalArcDegrees,
                        0f,
                        360f) * 0.5f;
                    if (Mathf.Abs(Mathf.DeltaAngle(
                        candidate.Rotation.AsAngle,
                        directionTowardThreat)) > halfArc)
                    {
                        continue;
                    }

                    shieldBearer = candidate;
                    shield = candidateShield;
                    return true;
                }
            }

            return false;
        }

        public static bool TryFindShieldPeekSource(
            Pawn shooter,
            LocalTargetInfo target,
            out IntVec3 peekSource)
        {
            peekSource = IntVec3.Invalid;
            if (shooter?.Map == null
                || !target.IsValid
                || !target.Cell.IsValid
                || !TryFindStationaryShieldCover(
                    shooter,
                    target.Cell,
                    out Pawn shieldBearer,
                    out _))
            {
                return false;
            }

            IntVec3 right = shieldBearer.Rotation.RighthandCell;
            IntVec3 first = shooter.thingIDNumber % 2 == 0
                ? shooter.Position + right
                : shooter.Position - right;
            IntVec3 second = shooter.thingIDNumber % 2 == 0
                ? shooter.Position - right
                : shooter.Position + right;

            if (CanUsePeekSource(first, target.Cell, shooter.Map))
            {
                peekSource = first;
                return true;
            }

            if (CanUsePeekSource(second, target.Cell, shooter.Map))
            {
                peekSource = second;
                return true;
            }

            return false;
        }

        private static bool CanUsePeekSource(
            IntVec3 source,
            IntVec3 target,
            Map map)
        {
            return source.InBounds(map)
                && source.Walkable(map)
                && GenSight.LineOfSight(source, target, map);
        }
    }

    [HarmonyPatch]
    public static class Patch_EquipmentUtility_BallisticShieldWeaponWhitelist
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(EquipmentUtility),
                nameof(EquipmentUtility.CanEquip),
                new[]
                {
                    typeof(Thing),
                    typeof(Pawn),
                    typeof(string).MakeByRefType(),
                    typeof(bool)
                })
                ?? AccessTools.Method(
                    typeof(EquipmentUtility),
                    nameof(EquipmentUtility.CanEquip),
                    new[]
                    {
                        typeof(Thing),
                        typeof(Pawn),
                        typeof(string).MakeByRefType()
                    });
        }

        public static void Postfix(
            Thing thing,
            Pawn pawn,
            ref bool __result,
            ref string cantReason)
        {
            if (!__result
                || thing == null
                || pawn?.apparel?.WornApparel == null
                || pawn.equipment == null)
            {
                return;
            }

            if (thing is Apparel shieldApparel)
            {
                CompDirectionalBallisticShield shield =
                    shieldApparel.TryGetComp<CompDirectionalBallisticShield>();
                ThingWithComps weapon = pawn.equipment.Primary;
                if (shield?.UsesWeaponWhitelist == true
                    && weapon != null
                    && !shield.IsWeaponAllowed(weapon.def))
                {
                    Reject(
                        shieldApparel.LabelCap,
                        weapon.LabelCap,
                        ref __result,
                        ref cantReason);
                }

                return;
            }

            if (!thing.def.IsWeapon)
            {
                return;
            }

            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                CompDirectionalBallisticShield shield =
                    apparel.TryGetComp<CompDirectionalBallisticShield>();
                if (shield?.UsesWeaponWhitelist == true
                    && !shield.IsWeaponAllowed(thing.def))
                {
                    Reject(
                        apparel.LabelCap,
                        thing.LabelCap,
                        ref __result,
                        ref cantReason);
                    return;
                }
            }
        }

        private static void Reject(
            TaggedString shieldLabel,
            TaggedString weaponLabel,
            ref bool result,
            ref string cantReason)
        {
            result = false;
            cantReason = "HD_BallisticShield_WeaponIncompatible".Translate(
                weaponLabel,
                shieldLabel);
        }
    }

    [HarmonyPatch(
        typeof(CoverUtility),
        nameof(CoverUtility.CalculateCoverGiverSet))]
    public static class Patch_CoverUtility_IronHideStationaryCover
    {
        public static void Postfix(
            LocalTargetInfo target,
            IntVec3 shooterLoc,
            Map map,
            ref List<CoverInfo> __result)
        {
            Pawn protectedPawn = target.Thing as Pawn;
            if (protectedPawn == null
                || map == null
                || !DirectionalBallisticShieldUtility.TryFindStationaryShieldCover(
                    protectedPawn,
                    shooterLoc,
                    out Pawn shieldBearer,
                    out CompDirectionalBallisticShield shield))
            {
                return;
            }

            if (__result == null)
            {
                __result = new List<CoverInfo>();
            }

            foreach (CoverInfo cover in __result)
            {
                if (cover.Thing == shieldBearer)
                {
                    return;
                }
            }

            __result.Add(new CoverInfo(
                shieldBearer,
                Mathf.Clamp01(shield.Props.stationaryAllyCoverBlockChance)));
        }
    }

    [HarmonyPatch(
        typeof(CoverUtility),
        nameof(CoverUtility.CalculateOverallBlockChance))]
    public static class Patch_CoverUtility_IronHideStationaryCoverChance
    {
        public static void Postfix(
            LocalTargetInfo target,
            IntVec3 shooterLoc,
            Map map,
            ref float __result)
        {
            Pawn protectedPawn = target.Thing as Pawn;
            if (protectedPawn == null
                || map == null
                || !DirectionalBallisticShieldUtility.TryFindStationaryShieldCover(
                    protectedPawn,
                    shooterLoc,
                    out _,
                    out CompDirectionalBallisticShield shield))
            {
                return;
            }

            float shieldBlockChance = Mathf.Clamp01(
                shield.Props.stationaryAllyCoverBlockChance);
            __result += (1f - __result) * shieldBlockChance;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryFindShootLineFromTo))]
    public static class Patch_Verb_IronHideShieldPeekShooting
    {
        public static void Postfix(
            Verb __instance,
            IntVec3 root,
            LocalTargetInfo targ,
            ref ShootLine resultingLine,
            ref bool __result)
        {
            Pawn shooter = __instance?.CasterPawn;
            if (shooter == null
                || __instance.verbProps?.Ranged != true
                || root != shooter.Position)
            {
                return;
            }

            if (DirectionalBallisticShieldUtility.TryFindShieldPeekSource(
                shooter,
                targ,
                out IntVec3 peekSource))
            {
                resultingLine = new ShootLine(peekSource, targ.Cell);
                __result = true;
            }
        }
    }

    [HarmonyPatch(
        typeof(ApparelGraphicRecordGetter),
        nameof(ApparelGraphicRecordGetter.TryGetGraphicApparel))]
    public static class Patch_ApparelGraphicRecord_IronHideTransparency
    {
        public static void Postfix(
            Apparel apparel,
            ref ApparelGraphicRecord rec,
            bool __result)
        {
            if (!__result
                || apparel?.TryGetComp<CompDirectionalBallisticShield>() == null
                || rec.graphic == null)
            {
                return;
            }

            Graphic original = rec.graphic;
            rec.graphic = GraphicDatabase.Get<Graphic_Multi>(
                original.path,
                ShaderDatabase.Transparent,
                original.drawSize,
                original.color);
        }
    }

    [HarmonyPatch]
    public static class Patch_PawnRotationTracker_IronHideForcedFacing
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            string[] methodNames =
            {
                nameof(Pawn_RotationTracker.UpdateRotation),
                nameof(Pawn_RotationTracker.Face),
                nameof(Pawn_RotationTracker.FaceCell),
                "FaceAdjacentCell",
                nameof(Pawn_RotationTracker.FaceTarget)
            };

            foreach (string methodName in methodNames)
            {
                MethodInfo method = AccessTools.Method(typeof(Pawn_RotationTracker), methodName);
                if (method != null)
                {
                    yield return method;
                }
            }
        }

        public static void Postfix(Pawn ___pawn)
        {
            CompDirectionalBallisticShield shield =
                DirectionalBallisticShieldUtility.WornShield(___pawn);
            shield?.ApplyForcedFacing(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "SetupMoveIntoNextCell")]
    public static class Patch_PawnPathFollower_IronHideDirectionalMovement
    {
        public static void Postfix(
            Pawn ___pawn,
            IntVec3 ___nextCell,
            ref float ___nextCellCostLeft,
            ref float ___nextCellCostTotal)
        {
            CompDirectionalBallisticShield shield =
                DirectionalBallisticShieldUtility.WornShield(___pawn);
            if (shield == null || ___pawn == null)
            {
                return;
            }

            float speedMultiplier = shield.MovementSpeedMultiplier(
                ___pawn.Position,
                ___nextCell);
            if (speedMultiplier >= 0.999f)
            {
                shield.ApplyForcedFacing(___pawn);
                return;
            }

            ___nextCellCostLeft /= speedMultiplier;
            ___nextCellCostTotal /= speedMultiplier;
            shield.ApplyForcedFacing(___pawn);
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_ShotReport_DirectionalBallisticShieldAccuracy
    {
        private static readonly FieldInfo FactorFromEquipmentField =
            AccessTools.Field(typeof(ShotReport), "factorFromEquipment");

        public static void Postfix(Thing caster, Verb verb, ref ShotReport __result)
        {
            if (!(caster is Pawn pawn)
                || verb?.verbProps == null
                || !verb.verbProps.Ranged
                || FactorFromEquipmentField == null)
            {
                return;
            }

            CompDirectionalBallisticShield shield =
                DirectionalBallisticShieldUtility.WornShield(pawn);
            if (shield == null)
            {
                return;
            }

            float multiplier = Mathf.Clamp(
                shield.CurrentRangedAccuracyMultiplier,
                0.01f,
                1f);
            object boxedReport = __result;
            float currentFactor = (float)FactorFromEquipmentField.GetValue(boxedReport);
            FactorFromEquipmentField.SetValue(boxedReport, currentFactor * multiplier);
            __result = (ShotReport)boxedReport;
        }
    }
}
