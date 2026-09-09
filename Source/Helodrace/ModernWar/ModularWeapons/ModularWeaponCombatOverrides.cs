using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    [HarmonyPatch(typeof(Verb_LaunchProjectile), "get_Projectile")]
    public static class Patch_Verb_Projectile_ModularWeapon
    {
        [HarmonyPostfix]
        public static void Postfix(Verb_LaunchProjectile __instance, ref ThingDef __result)
        {
            // CE resolves the projectile from CompAmmoUser.CurrentAmmo. Replacing that
            // result with a vanilla projectile would silently defeat CE ammo selection.
            // Keep the main assembly CE-agnostic by identifying the verb assembly.
            if (__instance?.GetType().Assembly.GetName().Name == "CombatExtended")
                return;

            CompModularWeaponNode comp = __instance?.EquipmentSource
                ?.TryGetComp<CompModularWeaponNode>();
            ThingDef replacement = comp?.ProjectileOverride;
            if (replacement != null) __result = replacement;
        }
    }

    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class Patch_Verb_CastSound_ModularWeapon
    {
        private static readonly FieldInfo CastField = AccessTools.Field(
            typeof(VerbProperties),
            nameof(VerbProperties.soundCast));
        private static readonly FieldInfo TailField = AccessTools.Field(
            typeof(VerbProperties),
            nameof(VerbProperties.soundCastTail));
        private static readonly MethodInfo ResolveCastMethod = AccessTools.Method(
            typeof(Patch_Verb_CastSound_ModularWeapon),
            nameof(ResolveCast));
        private static readonly MethodInfo ResolveTailMethod = AccessTools.Method(
            typeof(Patch_Verb_CastSound_ModularWeapon),
            nameof(ResolveTail));

        public static SoundDef ResolveCast(SoundDef original, Verb verb)
        {
            SoundDef replacement = verb?.EquipmentSource
                ?.TryGetComp<CompModularWeaponNode>()
                ?.SoundCastOverride;
            return replacement ?? original;
        }

        public static SoundDef ResolveTail(SoundDef original, Verb verb)
        {
            SoundDef replacement = verb?.EquipmentSource
                ?.TryGetComp<CompModularWeaponNode>()
                ?.SoundCastTailOverride;
            return replacement ?? original;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode != OpCodes.Ldfld
                    || !(instruction.operand is FieldInfo field))
                    continue;

                if (field == CastField)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, ResolveCastMethod);
                }
                else if (field == TailField)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, ResolveTailMethod);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_Verb_MuzzleFlash_ModularWeapon
    {
        [HarmonyPostfix]
        public static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (!__result) return;

            CompModularWeaponNode comp = __instance?.EquipmentSource
                ?.TryGetComp<CompModularWeaponNode>();
            ModularWeaponCycleUtility.NotifyShot(__instance, comp);
            EffecterDef effecterDef = comp?.MuzzleFlashEffecter;
            Thing caster = __instance?.caster;
            if (effecterDef == null || caster == null || !caster.Spawned) return;

            Vector3 from = caster.DrawPos;
            Vector3 target = __instance.CurrentTarget.CenterVector3;
            Vector3 direction = target - from;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();

            float aimAngle = direction.AngleFlat();
            Pawn pawn = caster as Pawn;
            float distanceFactor = pawn?.ageTracker?.CurLifeStage
                ?.equipmentDrawDistanceFactor ?? 1f;
            Vector3 gunCenter = from
                + new Vector3(
                    0f,
                    0f,
                    0.4f + __instance.EquipmentSource.def.equippedDistanceOffset)
                    .RotatedBy(aimAngle) * distanceFactor;
            Vector3 muzzle = gunCenter + direction * comp.MuzzleFlashDistance;
            Vector3 offset = muzzle - caster.Position.ToVector3Shifted();
            offset.y = 0f;

            TargetInfo source = new TargetInfo(caster.Position, caster.Map);
            IntVec3 aimCell = (from + direction * 20f).ToIntVec3();
            if (!aimCell.InBounds(caster.Map)) aimCell = __instance.CurrentTarget.Cell;
            TargetInfo destination = new TargetInfo(aimCell, caster.Map);

            Effecter effecter = new Effecter(effecterDef)
            {
                offset = offset,
                scale = comp.MuzzleFlashScale
            };
            effecter.Trigger(source, destination);
            effecter.Cleanup();
        }
    }
}
