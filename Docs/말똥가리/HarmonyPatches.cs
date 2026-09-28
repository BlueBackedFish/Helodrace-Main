using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using RimWorld;
using Verse;

namespace LGModularWeapons
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            Harmony harmony = new Harmony("LG.ModularWeapons");

            foreach (Type type in typeof(HarmonyPatches).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Log.Error("[LGModularWeapons] failed to apply patch " + type.Name
                            + " - other patches still active. " + e.Message);
                }
            }
        }
    }

    // CE(Combat Extended)도 같은 메서드(PawnWeaponGenerator.TryGenerateWeaponFor)에
    // 자기 자신의 Harmony 인스턴스("CombatExtended.HarmonyCE")로 Postfix를 걸어서
    // 그 안에서 무기에 맞는 탄약(예비 탄창 포함)을 생성한다.
    // (CombatExtended/Harmony/Harmony_PawnWeaponGenerator.cs:
    //  Harmony_PawnWeaponGenerator_TryGenerateWeaponFor.Postfix →
    //  LoadoutPropertiesExtension.GenerateLoadoutFor → TryGenerateAmmoFor)
    //
    // 문제는 Harmony가 "같은 메서드에 걸린 서로 다른 모드의 Postfix"끼리는
    // 실행 순서를 보장해주지 않는다는 것이다. 만약 CE의 Postfix가 우리(LG.ModularWeapons)
    // 의 Postfix보다 먼저 실행되면, CE는 아직 무작위 파츠(구경 개조 포함)가 적용되기 전인
    // "원래 구경"(예: 5.56mm) 기준으로 탄약을 생성해버린다. 이후 우리 Postfix가 파츠를
    // 확정하고 SetConfiguration → ApplyCEAmmoConfig로 실제 탄창(CompAmmoUser)의 구경은
    // 9mm로 바뀌지만, 이미 인벤토리에 생성되어 있는 예비 탄약 더미는 그대로 5.56mm로
    // 남아있게 되어 "9mm 컨버전인데 5.56mm 탄을 들고나오는" 버그가 발생한다.
    //
    // [HarmonyBefore("CombatExtended.HarmonyCE")]를 붙이면 Harmony의 패치 정렬기가
    // (모드 로드 순서와 무관하게) 우리 Postfix를 CE의 Postfix보다 항상 먼저 실행하도록
    // 강제한다. 그러면 이 Postfix가 끝나 파츠/구경이 완전히 확정된 뒤에야 CE가 탄약을
    // 생성하므로, CE는 항상 "이미 변환된" ammoSet 기준으로 올바른 탄약을 생성하게 된다.
    [HarmonyBefore("CombatExtended.HarmonyCE")]
    [HarmonyPatch(typeof(PawnWeaponGenerator), nameof(PawnWeaponGenerator.TryGenerateWeaponFor))]
    public static class Patch_TryGenerateWeaponFor
    {
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(PawnWeaponGenerator),
                nameof(PawnWeaponGenerator.TryGenerateWeaponFor)) != null;
        }

        public static void Postfix(Pawn pawn)
        {
            ThingWithComps weapon = pawn?.equipment?.Primary;
            CompWeaponModular comp = weapon?.GetComp<CompWeaponModular>();
            if (comp == null) return;

            PawnKindModularParts rules = pawn.kindDef?.GetModExtension<PawnKindModularParts>();
            if (rules == null) return;

            Rand.PushState(pawn.thingIDNumber ^ weapon.thingIDNumber);
            try
            {
                var config = new Dictionary<WeaponPartSlotDef, WeaponPartDef>();
                foreach (var slot in comp.ActiveSlots)
                {
                    WeaponPartDef fitted = comp.PartInSlot(slot);
                    if (fitted != null) config[slot] = fitted;
                }

                var locked = new HashSet<WeaponPartSlotDef>();
                FitRequired(comp, rules, config, locked);

                if (Rand.Chance(rules.chance))
                    FitOptional(comp, rules, config, locked);

                // Required SLOTS (slot.required) must always be filled regardless of the
                // chance gate. Run after optional so parts opened during optional are visible.
                FillRequired(comp, rules, config, locked);

                comp.SetConfiguration(config);
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static void FitRequired(CompWeaponModular comp, PawnKindModularParts rules,
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config,
            HashSet<WeaponPartSlotDef> locked)
        {
            if (rules.requiredParts.NullOrEmpty()) return;

            var remaining = new List<WeaponPartDef>();
            foreach (WeaponPartDef part in rules.requiredParts)
                if (part?.slot != null) remaining.Add(part);

            var failReasons = new Dictionary<WeaponPartDef, string>();

            for (int pass = 0; pass < 8 && remaining.Count > 0; pass++)
            {
                bool fittedAny = false;
                List<WeaponPartSlotDef> slots = comp.ComputeActiveSlots(config);

                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    WeaponPartDef part = remaining[i];

                    if (!slots.Contains(part.slot))
                    {
                        failReasons[part] = "slot " + part.slot.defName + " not on this weapon";
                        continue;
                    }

                    string reason;
                    if (!comp.CanFitPart(part, config, slots, out reason))
                    {
                        failReasons[part] = reason;
                        continue;
                    }

                    config[part.slot] = part;
                    locked.Add(part.slot);
                    remaining.RemoveAt(i);
                    failReasons.Remove(part);
                    fittedAny = true;
                }

                if (!fittedAny) break;
            }

            if (remaining.Count == 0) return;

            foreach (WeaponPartDef part in remaining)
            {
                string why = failReasons.TryGetValue(part, out var r) ? r : "unknown";
                WarnRequiredOnce(part, why);
            }
        }

        private static readonly HashSet<WeaponPartDef> WarnedRequired = new HashSet<WeaponPartDef>();

        private static void WarnRequiredOnce(WeaponPartDef part, string reason)
        {
            if (!WarnedRequired.Add(part)) return;
            Log.Warning("[LGModularWeapons] required part " + part.defName
                      + " could not be fitted on a generated weapon: " + reason
                      + ". Check that its prerequisites are also in requiredParts.");
        }

        private static void FitOptional(CompWeaponModular comp, PawnKindModularParts rules,
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config,
            HashSet<WeaponPartSlotDef> locked)
        {
            for (int pass = 0; pass < 8; pass++)
            {
                bool filledSomething = false;
                List<WeaponPartSlotDef> slots = comp.ComputeActiveSlots(config);

                foreach (WeaponPartSlotDef slot in slots)
                {
                    if (locked.Contains(slot)) continue;

                    WeaponPartDef current;
                    config.TryGetValue(slot, out current);

                    if (current != null)
                    {
                        if (!rules.replaceDefaults) continue;
                        if (comp.Props.defaultParts.NullOrEmpty()) continue;
                        if (!comp.Props.defaultParts.Contains(current)) continue;
                        if (!Rand.Chance(rules.ChanceForSlot(slot))) continue;

                        WeaponPartDef swap = PickFor(comp, rules, config, slots, slot, false);
                        if (swap == null || swap == current) continue;

                        config[slot] = swap;
                        filledSomething = true;
                        continue;
                    }

                    if (slot.defaultPart != null) continue;

                    bool must = slot.required;
                    if (!must && !Rand.Chance(rules.ChanceForSlot(slot))) continue;

                    WeaponPartDef pick = PickFor(comp, rules, config, slots, slot, must);
                    if (pick == null) continue;

                    config[slot] = pick;
                    filledSomething = true;
                }

                if (!filledSomething) break;
            }
        }

        // Sweeps required slots (slot.required=true) that are still empty after FitOptional.
        // Slots opened late (e.g. by a handguard fitted during optional) can be missed by the
        // optional loop; and the chance gate in the Postfix could skip FitOptional entirely,
        // leaving required child slots bare (bare buffer tube, etc.).
        private static void FillRequired(CompWeaponModular comp, PawnKindModularParts rules,
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config,
            HashSet<WeaponPartSlotDef> locked)
        {
            for (int pass = 0; pass < 8; pass++)
            {
                bool filled = false;
                List<WeaponPartSlotDef> slots = comp.ComputeActiveSlots(config);

                foreach (WeaponPartSlotDef slot in slots)
                {
                    if (locked.Contains(slot)) continue;
                    if (!slot.required || slot.defaultPart != null) continue;

                    WeaponPartDef current;
                    if (config.TryGetValue(slot, out current) && current != null) continue;

                    // Recompute slots inside PickFor so CanFitPart sees the latest config.
                    List<WeaponPartSlotDef> fresh = comp.ComputeActiveSlots(config);
                    WeaponPartDef pick = PickFor(comp, rules, config, fresh, slot, true);
                    if (pick == null) continue;

                    config[slot] = pick;
                    filled = true;
                }

                if (!filled) break;
            }
        }

        private static WeaponPartDef PickFor(CompWeaponModular comp, PawnKindModularParts rules,
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config,
            List<WeaponPartSlotDef> slots, WeaponPartSlotDef slot, bool relaxTagsIfEmpty)
        {
            List<WeaponPartDef> tagged = new List<WeaponPartDef>();
            List<WeaponPartDef> any = new List<WeaponPartDef>();

            foreach (WeaponPartDef part in DefDatabase<WeaponPartDef>.AllDefsListForReading)
            {
                if (part.slot != slot) continue;

                string reason;
                if (!comp.CanFitPart(part, config, slots, out reason)) continue;

                any.Add(part);
                if (rules.PartAllowed(part)) tagged.Add(part);
            }

            if (tagged.Count > 0) return tagged.RandomElement();
            if (relaxTagsIfEmpty && any.Count > 0) return any.RandomElement();
            return null;
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming))]
    public static class Patch_DrawEquipmentAiming
    {
        public static bool Prepare()
        {
            if (AccessTools.Method(typeof(PawnRenderUtility),
                    nameof(PawnRenderUtility.DrawEquipmentAiming)) != null) return true;
            Log.Warning("[LGModularWeapons] PawnRenderUtility.DrawEquipmentAiming not found - "
                      + "held-weapon overlays disabled.");
            return false;
        }

        public static void Postfix(Thing eq, Vector3 drawLoc, float aimAngle)
        {
            ModularWeaponRenderer.DrawPartOverlays(eq, drawLoc, aimAngle);
        }
    }

    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class Patch_Verb_TryCastNextBurstShot
    {
        public static bool Prepare()
        {
            if (AccessTools.Method(typeof(Verb), "TryCastNextBurstShot") != null) return true;
            Log.Warning("[LGModularWeapons] Verb.TryCastNextBurstShot not found - part sound "
                      + "overrides disabled, everything else still works.");
            return false;
        }

        private static readonly FieldInfo CastField =
            AccessTools.Field(typeof(VerbProperties), nameof(VerbProperties.soundCast));
        private static readonly FieldInfo TailField =
            AccessTools.Field(typeof(VerbProperties), nameof(VerbProperties.soundCastTail));

        private static readonly MethodInfo ResolveCastM =
            AccessTools.Method(typeof(Patch_Verb_TryCastNextBurstShot), nameof(ResolveCast));
        private static readonly MethodInfo ResolveTailM =
            AccessTools.Method(typeof(Patch_Verb_TryCastNextBurstShot), nameof(ResolveTail));

        public static SoundDef ResolveCast(SoundDef original, Verb verb)
        {
            CompWeaponModular comp = verb?.EquipmentSource?.GetComp<CompWeaponModular>();
            SoundDef over = comp?.SoundCastOverride;
            return over ?? original;
        }

        public static SoundDef ResolveTail(SoundDef original, Verb verb)
        {
            CompWeaponModular comp = verb?.EquipmentSource?.GetComp<CompWeaponModular>();
            SoundDef over = comp?.SoundCastTailOverride;
            return over ?? original;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int patched = 0;
            foreach (CodeInstruction ins in instructions)
            {
                yield return ins;

                if (ins.opcode == OpCodes.Ldfld && ins.operand is FieldInfo f)
                {
                    if (f == CastField)
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Call, ResolveCastM);
                        patched++;
                    }
                    else if (f == TailField)
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Call, ResolveTailM);
                        patched++;
                    }
                }
            }

            if (patched == 0)
                Log.Error("[LGModularWeapons] soundCast transpiler matched nothing - "
                        + "fire sounds will not respond to parts.");
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), nameof(Verb_LaunchProjectile.Projectile), MethodType.Getter)]
    public static class Patch_Verb_Projectile
    {
        public static void Postfix(Verb_LaunchProjectile __instance, ref ThingDef __result)
        {
            CompWeaponModular comp = __instance?.EquipmentSource?.GetComp<CompWeaponModular>();
            ThingDef over = comp?.ProjectileOverride;
            if (over != null) __result = over;
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ThingIcon),
        new Type[] { typeof(Rect), typeof(Thing), typeof(float), typeof(Rot4?), typeof(bool),
                     typeof(float), typeof(bool) })]
    public static class Patch_Widgets_ThingIcon
    {
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(Widgets), nameof(Widgets.ThingIcon),
                new Type[] { typeof(Rect), typeof(Thing), typeof(float), typeof(Rot4?),
                             typeof(bool), typeof(float), typeof(bool) }) != null;
        }

        public static void Prefix(Rect rect, Thing thing, float alpha, float scale)
        {
            DrawIconOverlays(rect, thing, alpha, scale, true);
        }

        public static void Postfix(Rect rect, Thing thing, float alpha, float scale)
        {
            DrawIconOverlays(rect, thing, alpha, scale, false);
        }

        private static void DrawIconOverlays(Rect rect, Thing thing, float alpha, float scale,
            bool under)
        {
            ThingWithComps twc = thing as ThingWithComps;
            if (twc == null) return;

            CompWeaponModular comp = twc.GetComp<CompWeaponModular>();
            if (comp == null || !comp.HasDrawableParts) return;

            var parts = comp.FittedPartsForDrawing();

            Color old = GUI.color;
            GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);

            Graphic gunGraphic = thing.Graphic;
            Vector2 gunSize = gunGraphic != null ? gunGraphic.drawSize : Vector2.one;
            float cells = Mathf.Max(gunSize.x, gunSize.y, 0.01f);

            float size = Mathf.Min(rect.width, rect.height) * scale * GenUI.IconDrawScale(thing.def);
            float px = size / cells;
            Vector2 center = rect.center;

            foreach (var kv in parts)
            {
                PartDrawData d = kv.Value;
                if ((d.layer < 0f) != under) continue;

                Texture tex = kv.Key.Graphic?.MatSingle?.mainTexture;
                if (tex == null) continue;

                Color partColor = kv.Key.Graphic.Color;
                GUI.color = new Color(partColor.r, partColor.g, partColor.b,
                    partColor.a * old.a * alpha);

                Vector2 sizePx = kv.Key.Graphic.drawSize * d.scale * px;
                Rect r = new Rect(
                    center.x - sizePx.x * 0.5f + d.offset.x * px,
                    center.y - sizePx.y * 0.5f - d.offset.z * px,
                    sizePx.x, sizePx.y);

                Matrix4x4 m = GUI.matrix;
                if (!Mathf.Approximately(d.angleOffset, 0f))
                    UI.RotateAroundPivot(-d.angleOffset, r.center);
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
                GUI.matrix = m;
            }

            GUI.color = old;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.BurstShotCount), MethodType.Getter)]
    public static class Patch_Verb_BurstShotCount
    {
        public static bool Prepare()
        {
            return AccessTools.PropertyGetter(typeof(Verb), nameof(Verb.BurstShotCount)) != null;
        }

        public static void Postfix(Verb __instance, ref int __result)
        {
            CompWeaponModular comp = __instance?.EquipmentSource?.GetComp<CompWeaponModular>();
            if (comp == null) return;

            float v = (__result + comp.BurstCountOffset) * comp.BurstCountFactor;
            __result = Mathf.Max(1, Mathf.CeilToInt(v));
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TicksBetweenBurstShots), MethodType.Getter)]
    public static class Patch_Verb_TicksBetweenBurstShots
    {
        public static bool Prepare()
        {
            return AccessTools.PropertyGetter(typeof(Verb),
                nameof(Verb.TicksBetweenBurstShots)) != null;
        }

        public static void Postfix(Verb __instance, ref int __result)
        {
            CompWeaponModular comp = __instance?.EquipmentSource?.GetComp<CompWeaponModular>();
            if (comp == null) return;

            float factor = comp.BurstSpeedFactor;
            if (factor <= 0f) return;

            __result = Mathf.Max(1, Mathf.RoundToInt(__result / factor));
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_Verb_MuzzleFlash
    {
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(Verb_LaunchProjectile), "TryCastShot") != null;
        }

        public static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (!__result) return;

            CompWeaponModular comp = __instance.EquipmentSource?.GetComp<CompWeaponModular>();
            EffecterDef effDef = comp?.MuzzleFlashEffecter;
            if (effDef == null) return;

            Thing caster = __instance.caster;
            if (caster == null || !caster.Spawned) return;

            Vector3 from = caster.DrawPos;
            Vector3 to = __instance.CurrentTarget.CenterVector3;

            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir = dir.normalized;

            float aimAngle = dir.AngleFlat();

            Pawn casterPawn = caster as Pawn;
            float drawDistanceFactor = casterPawn?.ageTracker?.CurLifeStage?.equipmentDrawDistanceFactor ?? 1f;

            Vector3 gunCentre = from
                + new Vector3(0f, 0f, 0.4f + __instance.EquipmentSource.def.equippedDistanceOffset)
                    .RotatedBy(aimAngle) * drawDistanceFactor;

            Vector3 muzzle = gunCentre + dir * comp.MuzzleFlashDistance;

            Vector3 offset = muzzle - caster.Position.ToVector3Shifted();
            offset.y = 0f;

            TargetInfo a = new TargetInfo(caster.Position, caster.Map);

            IntVec3 aimCell = (from + dir * 20f).ToIntVec3();
            if (!aimCell.InBounds(caster.Map))
                aimCell = __instance.CurrentTarget.Cell;

            TargetInfo b = new TargetInfo(aimCell, caster.Map);

            Effecter eff = new Effecter(effDef);
            eff.offset = offset;
            eff.scale = comp.MuzzleFlashScale;
            eff.Trigger(a, b);
            eff.Cleanup();
        }
    }

    [HarmonyPatch(typeof(CompEquippable), nameof(CompEquippable.Tools), MethodType.Getter)]
    public static class Patch_CompEquippable_Tools
    {
        public static bool Prepare()
        {
            return AccessTools.PropertyGetter(typeof(CompEquippable),
                nameof(CompEquippable.Tools)) != null;
        }

        public static void Postfix(CompEquippable __instance, ref List<Tool> __result)
        {
            CompWeaponModular comp = __instance.parent?.GetComp<CompWeaponModular>();
            List<Tool> combined = comp?.CombinedTools;
            if (combined != null) __result = combined;
        }
    }

    [HarmonyPatch(typeof(CompEquippableAbility), nameof(CompEquippableAbility.AbilityForReading),
        MethodType.Getter)]
    public static class Patch_CompEquippableAbility_AbilityForReading
    {
        public static bool Prepare()
        {
            return AccessTools.PropertyGetter(typeof(CompEquippableAbility),
                nameof(CompEquippableAbility.AbilityForReading)) != null;
        }

        public static void Postfix(CompEquippableAbility __instance, ref Ability __result)
        {
            if (__result == null) return;

            CompWeaponModular comp = __instance.parent?.GetComp<CompWeaponModular>();
            if (comp == null) return;

            if (!comp.AbilitiesResolved) return;

            if (!comp.GrantsAbility(__result.def)) __result = null;
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.EquipmentTrackerTick))]
    public static class Patch_EquipmentTrackerTick_Flashlight
    {
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(Pawn_EquipmentTracker),
                nameof(Pawn_EquipmentTracker.EquipmentTrackerTick)) != null;
        }

        public static void Postfix(Pawn_EquipmentTracker __instance)
        {
            Pawn pawn = __instance.pawn;
            if (pawn == null || !pawn.Spawned) return;

            ThingWithComps weapon = __instance.Primary;
            CompWeaponModular comp = weapon?.GetComp<CompWeaponModular>();
            if (comp == null) return;

            Stance_Busy stance = pawn.stances?.curStance as Stance_Busy;
            if (stance == null || stance.neverAimWeapon || !stance.focusTarg.HasThing) return;

            Pawn victim = stance.focusTarg.Thing as Pawn;
            if (victim == null || victim.Dead || victim.health == null) return;

            var lights = comp.FittedFlashlights();
            for (int i = 0; i < lights.Count; i++)
            {
                FlashlightProps light = lights[i].Key.flashlight;
                if (light == null) continue;

                if (!Applies(light, victim)) continue;
                if (victim.Position.DistanceTo(pawn.Position) > light.maxRange) continue;

                HediffDef def = light.hediff;
                float intensity = light.intensity;
                float perSecond = 0f;
                bool requireExisting = false;

                if (!light.targetOverrides.NullOrEmpty())
                {
                    foreach (FlashlightTargetOverride ov in light.targetOverrides)
                    {
                        if (!ov.Matches(victim)) continue;
                        if (ov.hediff != null) def = ov.hediff;
                        intensity = ov.intensity;
                        perSecond = ov.severityPerSecond;
                        requireExisting = ov.requireExisting;
                        break;
                    }
                }

                if (def == null) continue;

                Hediff hediff = victim.health.hediffSet.GetFirstHediffOfDef(def);
                if (hediff == null)
                {
                    if (requireExisting) continue;

                    hediff = HediffMaker.MakeHediff(def, victim);
                    hediff.Severity = 0.01f;
                    victim.health.AddHediff(hediff);
                }

                HediffComp_Dazzle dazzle = (hediff as HediffWithComps)?.TryGetComp<HediffComp_Dazzle>();
                if (dazzle != null)
                {
                    dazzle.Notify_Lit(intensity);
                    continue;
                }

                if (perSecond > 0f)
                    hediff.Severity = Mathf.Min(hediff.Severity + perSecond / 60f,
                        def.maxSeverity);
            }
        }

        private static bool Applies(FlashlightProps light, Pawn victim)
        {
            if (victim.RaceProps == null) return true;
            if (victim.RaceProps.IsMechanoid) return light.affectsMechanoids;
            if (victim.RaceProps.Animal) return light.affectsAnimals;
            return true;
        }
    }
}