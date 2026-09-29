using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    // Developer-only, visual states for recording the Flux Raider trailer.
    internal enum FluxCinematicPose { LowReady, Aiming, Lean, Prone }

    internal static class FluxRaiderCinematic
    {
        private sealed class State
        {
            public int weaponId;
            public FluxCinematicPose pose = FluxCinematicPose.LowReady;
            public float fromRaise;
            public int transitionTick;
            public bool emittersOn;
            public int reloadTick = -1000;
            public bool frontMagazineConsumed;
            public int lastDustTick = -1000;
            public bool directionLocked;
            public Rot4 lockedDirection;
            public Vector2 lowReadyOffset = new Vector2(0f, -0.26f);
            public float lowReadyAngle = 28f;
        }

        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        private const int RaiseTicks = 14;
        private const int ReloadTicks = 72;

        public static bool IsFluxRaider(Pawn pawn)
        {
            CompModularWeaponNode root = pawn?.equipment?.Primary?.GetComp<CompModularWeaponNode>();
            if (root?.Props.isAssemblyRoot != true || pawn.equipment.Primary.def.defName != "HD_Gun_P320_Weapon")
                return false;
            List<ModularRenderNode> nodes = root.RenderSnapshot();
            return nodes.Any(n => n.thing?.def?.defName == "HD_ModularPart_Receiver_FluxRaiderKit"
                || n.thing?.def?.defName == "HD_ModularPart_Receiver_FluxRaiderKitTan");
        }

        private static State For(Pawn pawn)
        {
            if (!Prefs.DevMode || !IsFluxRaider(pawn)) return null;
            int weaponId = pawn.equipment.Primary.thingIDNumber;
            return states.TryGetValue(pawn.thingIDNumber, out State state)
                && state.weaponId == weaponId ? state : null;
        }

        private static int Now => Find.TickManager?.TicksGame ?? 0;

        public static FluxCinematicPose Pose(Pawn pawn) => For(pawn)?.pose ?? FluxCinematicPose.Aiming;
        public static bool Active(Pawn pawn) => For(pawn) != null;
        public static bool EmittersOn(Pawn pawn) => For(pawn)?.emittersOn ?? true;

        public static bool TryLockedDirection(Pawn pawn, out Rot4 direction)
        {
            State state = For(pawn);
            direction = state?.lockedDirection ?? Rot4.Invalid;
            return state?.directionLocked == true;
        }

        public static void LockDirection(Pawn pawn, Rot4 direction)
        {
            State state = For(pawn);
            if (state == null) return;
            state.lockedDirection = direction;
            state.directionLocked = true;
            pawn.Rotation = direction;
        }

        public static void UnlockDirection(Pawn pawn)
        {
            State state = For(pawn);
            if (state != null) state.directionLocked = false;
        }

        public static void FaceForPose(Pawn pawn, Rot4 direction)
        {
            if (TryLockedDirection(pawn, out _)) LockDirection(pawn, direction);
            else pawn.Rotation = direction;
        }

        public static void ApplyLockedDirection(Pawn pawn)
        {
            if (TryLockedDirection(pawn, out Rot4 direction) && pawn.Rotation != direction)
                pawn.Rotation = direction;
        }

        public static void GetLowReady(Pawn pawn, out Vector2 offset, out float angle)
        {
            State state = For(pawn);
            offset = state?.lowReadyOffset ?? new Vector2(0f, -0.26f);
            angle = state?.lowReadyAngle ?? 28f;
        }

        public static void SetLowReady(Pawn pawn, Vector2 offset, float angle)
        {
            State state = For(pawn);
            if (state == null) return;
            state.lowReadyOffset = new Vector2(
                Mathf.Clamp(offset.x, -0.6f, 0.6f),
                Mathf.Clamp(offset.y, -0.6f, 0.6f));
            state.lowReadyAngle = Mathf.Clamp(angle, -90f, 90f);
        }

        public static void BeginAimingForShot(Pawn pawn)
        {
            State state = For(pawn);
            if (state?.pose == FluxCinematicPose.LowReady)
                SetPose(pawn, FluxCinematicPose.Aiming);
        }

        public static void ToggleSession(Pawn pawn)
        {
            if (!Prefs.DevMode || !IsFluxRaider(pawn)) return;
            if (For(pawn) != null)
                states.Remove(pawn.thingIDNumber);
            else
                states[pawn.thingIDNumber] = new State
                {
                    weaponId = pawn.equipment.Primary.thingIDNumber,
                    transitionTick = Now - RaiseTicks
                };
        }

        public static float Raise(Pawn pawn)
        {
            State state = For(pawn);
            if (state == null) return 1f;
            float target = state.pose == FluxCinematicPose.LowReady ? 0f : 1f;
            float t = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((Now - state.transitionTick) / (float)RaiseTicks));
            return Mathf.Lerp(state.fromRaise, target, t);
        }

        public static void SetPose(Pawn pawn, FluxCinematicPose pose)
        {
            State state = For(pawn);
            if (state == null) return;
            float current = Raise(pawn);
            state.pose = pose;
            state.fromRaise = current;
            state.transitionTick = Now;
            if (pose == FluxCinematicPose.Lean || pose == FluxCinematicPose.Prone)
                ThrowDust(pawn, 4);
        }

        public static void StartReload(Pawn pawn)
        {
            State state = For(pawn);
            if (state == null) return;
            state.reloadTick = Now;
            state.frontMagazineConsumed = false;
        }

        public static void ToggleEmitters(Pawn pawn)
        {
            State state = For(pawn);
            if (state != null) state.emittersOn = !state.emittersOn;
        }

        public static bool ReloadProgress(CompModularWeaponNode root, out float progress)
        {
            progress = 0f;
            Pawn pawn = (root?.parent?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            State state = For(pawn);
            if (state == null || state.weaponId != root.parent.thingIDNumber) return false;
            int elapsed = Now - state.reloadTick;
            if (elapsed >= Mathf.RoundToInt(ReloadTicks * 0.88f))
                state.frontMagazineConsumed = true;
            if (elapsed < 0 || elapsed >= ReloadTicks) return false;
            progress = elapsed / (float)ReloadTicks;
            return true;
        }

        public static bool ReloadMagazineOffset(CompModularWeaponNode root,
            ModularRenderNode node, out Vector2 offset)
        {
            offset = Vector2.zero;
            if (node == null || !IsMagazineNode(node) || !ReloadProgress(root, out float progress))
                return false;
            // The original magazine falls freely; the replacement comes up from the
            // support hand and seats after the old one has cleared the frame.
            if (progress < 0.12f) return true;
            if (progress < 0.53f)
            {
                float fall = Mathf.InverseLerp(0.12f, 0.53f, progress);
                offset = new Vector2(0.04f * fall, -0.68f * fall * fall);
                return true;
            }
            float seat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.53f, 0.88f, progress));
            offset = new Vector2(-0.14f * (1f - seat), -0.42f * (1f - seat));
            return true;
        }

        private static bool IsMagazineNode(ModularRenderNode node)
        {
            CompModularWeaponNode comp = node.comp;
            for (int depth = 0; comp != null && depth < 16; depth++)
            {
                if (comp.Props.magazineCapacity > 0) return true;
                comp = comp.parent?.ParentHolder as CompModularWeaponNode;
            }
            return false;
        }

        public static bool HideReloadNode(CompModularWeaponNode root, ModularRenderNode node)
        {
            Pawn pawn = (root?.parent?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            State state = For(pawn);
            if (state == null || node?.thing?.def == null) return false;
            bool front = node.thing.def.defName.StartsWith("HD_ModularPart_FrontMagazine_P320");
            if (!ReloadProgress(root, out float progress))
                return front && state.frontMagazineConsumed;
            if (front) return progress >= 0.88f;
            return IsMagazineNode(node) && progress >= 0.53f && progress < 0.88f;
        }

        public static Vector2 FrontMagazineOffset(CompModularWeaponNode root,
            ModularRenderNode node)
        {
            if (node?.thing?.def?.defName.StartsWith("HD_ModularPart_FrontMagazine_P320") != true
                || !ReloadProgress(root, out float progress) || progress < 0.35f)
                return Vector2.zero;
            List<ModularRenderNode> nodes = root.RenderSnapshot();
            ModularRenderNode magazine = nodes.FirstOrDefault(n => n.Props.magazineCapacity > 0);
            if (magazine == null) return Vector2.zero;
            float travel = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.35f, 0.88f, progress));
            Vector2 delta = magazine.GraphicCenter - node.GraphicCenter;
            return delta * travel + new Vector2(0.04f, -0.13f) * Mathf.Sin(travel * Mathf.PI);
        }

        public static void NotifyShot(Pawn pawn)
        {
            State state = For(pawn);
            if (state == null || (state.pose != FluxCinematicPose.Lean
                && state.pose != FluxCinematicPose.Prone) || Now - state.lastDustTick < 6) return;
            state.lastDustTick = Now;
            ThrowDust(pawn, 3);
        }

        private static void ThrowDust(Pawn pawn, int count)
        {
            if (pawn?.Spawned != true || pawn.Map == null
                || pawn.Position.GetTerrain(pawn.Map)?.IsWater == true) return;
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = new Vector3(Rand.Range(-0.35f, 0.35f), 0f,
                    Rand.Range(-0.25f, 0.2f));
                FleckMaker.ThrowDustPuff(pawn.DrawPos + offset, pawn.Map,
                    Rand.Range(0.35f, 0.65f));
            }
        }

        public static void AdjustWeapon(Pawn pawn, ref Vector3 drawLoc, ref float aimAngle)
        {
            if (!Active(pawn)) return;
            float raise = Raise(pawn);
            Rot4 facing = TryLockedDirection(pawn, out Rot4 locked) ? locked : pawn.Rotation;
            float side = facing == Rot4.West ? -1f : 1f;
            GetLowReady(pawn, out Vector2 lowReadyOffset, out float lowReadyAngle);
            drawLoc += new Vector3(lowReadyOffset.x, 0f, lowReadyOffset.y)
                * (1f - raise);
            aimAngle += side * lowReadyAngle * (1f - raise);
            if (Pose(pawn) == FluxCinematicPose.Prone)
                drawLoc += new Vector3(0f, 0f, -0.17f);
            else if (Pose(pawn) == FluxCinematicPose.Lean)
                drawLoc += new Vector3(side * 0.1f, 0f, 0f);
        }
    }

    [HarmonyPatch]
    internal static class Patch_FluxRaiderCinematicDirectionLock
    {
        private static IEnumerable<MethodBase> TargetMethods()
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
                if (method != null) yield return method;
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(-100)]
        private static void Postfix(Pawn ___pawn)
        {
            FluxRaiderCinematic.ApplyLockedDirection(___pawn);
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "RotationForcedByJob")]
    internal static class Patch_FluxRaiderCinematicForcedRenderDirection
    {
        [HarmonyPostfix]
        [HarmonyPriority(-100)]
        private static void Postfix(Pawn ___pawn, ref Rot4 __result)
        {
            if (FluxRaiderCinematic.TryLockedDirection(___pawn, out Rot4 direction))
                __result = direction;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class Patch_FluxRaiderCinematicGizmos
    {
        [HarmonyPostfix]
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            if (values != null)
                foreach (Gizmo gizmo in values) yield return gizmo;
            if (__instance?.Faction != Faction.OfPlayer || !Prefs.DevMode
                || !FluxRaiderCinematic.IsFluxRaider(__instance))
                yield break;
            bool active = FluxRaiderCinematic.Active(__instance);
            yield return new Command_Action
            {
                defaultLabel = active ? "CINE: stop" : "CINE: start",
                defaultDesc = "Toggle Flux Raider recording controls for this pawn.",
                icon = __instance.equipment.Primary.def.uiIcon,
                action = () => FluxRaiderCinematic.ToggleSession(__instance)
            };
            if (!active) yield break;
            foreach (FluxCinematicPose pose in Enum.GetValues(typeof(FluxCinematicPose)))
            {
                if (pose == FluxCinematicPose.Lean || pose == FluxCinematicPose.Prone)
                    continue;
                FluxCinematicPose choice = pose;
                yield return new Command_Action
                {
                    defaultLabel = "CINE: " + choice,
                    defaultDesc = "Set the Flux Raider recording pose.",
                    icon = __instance.equipment.Primary.def.uiIcon,
                    action = () => FluxRaiderCinematic.SetPose(__instance, choice)
                };
            }
            yield return new Command_Action
            {
                defaultLabel = "CINE: tune low ready",
                defaultDesc = "Adjust the lowered weapon's screen X/Z position and angle.",
                icon = __instance.equipment.Primary.def.uiIcon,
                action = () =>
                {
                    FluxRaiderCinematic.SetPose(__instance, FluxCinematicPose.LowReady);
                    Find.WindowStack.Add(new Dialog_FluxRaiderLowReady(__instance));
                }
            };
            foreach (FluxCinematicPose pose in new[]
                { FluxCinematicPose.Lean, FluxCinematicPose.Prone })
            {
                foreach (Rot4 direction in new[] { Rot4.East, Rot4.West })
                {
                    FluxCinematicPose choice = pose;
                    Rot4 facing = direction;
                    yield return new Command_Action
                    {
                        defaultLabel = "CINE: " + choice + " " + facing,
                        defaultDesc = "Face " + facing + " and set the recording pose.",
                        icon = __instance.equipment.Primary.def.uiIcon,
                        action = () =>
                        {
                            FluxRaiderCinematic.FaceForPose(__instance, facing);
                            FluxRaiderCinematic.SetPose(__instance, choice);
                        }
                    };
                }
            }
            if (FluxRaiderCinematic.TryLockedDirection(__instance, out Rot4 lockedDirection))
            {
                yield return new Command_Action
                {
                    defaultLabel = "CINE: unlock facing (" + lockedDirection + ")",
                    defaultDesc = "Allow normal facing changes again.",
                    icon = TexCommand.DesirePower,
                    action = () => FluxRaiderCinematic.UnlockDirection(__instance)
                };
            }
            else
            {
                yield return new Command_Action
                {
                    defaultLabel = "CINE: lock current facing",
                    defaultDesc = "Keep the pawn facing its current direction while filming.",
                    icon = TexCommand.DesirePower,
                    action = () => FluxRaiderCinematic.LockDirection(__instance, __instance.Rotation)
                };
            }
            foreach (Rot4 direction in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                Rot4 facing = direction;
                yield return new Command_Action
                {
                    defaultLabel = "CINE: lock " + facing,
                    defaultDesc = "Face " + facing + " and hold that direction.",
                    icon = TexCommand.DesirePower,
                    action = () => FluxRaiderCinematic.LockDirection(__instance, facing)
                };
            }
            yield return new Command_Action
            {
                defaultLabel = "CINE: reload",
                defaultDesc = "Drop the current magazine and seat a replacement visually.",
                icon = __instance.equipment.Primary.def.uiIcon,
                action = () => FluxRaiderCinematic.StartReload(__instance)
            };
            yield return new Command_Action
            {
                defaultLabel = FluxRaiderCinematic.EmittersOn(__instance)
                    ? "CINE: lights off" : "CINE: lights on",
                defaultDesc = "Toggle attached laser and flashlight beams for filming.",
                icon = TexCommand.DesirePower,
                action = () => FluxRaiderCinematic.ToggleEmitters(__instance)
            };
        }
    }

    internal sealed class Dialog_FluxRaiderLowReady : Window
    {
        private readonly Pawn pawn;

        public Dialog_FluxRaiderLowReady(Pawn pawn)
        {
            this.pawn = pawn;
            doCloseX = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(430f, 220f);

        public override void DoWindowContents(Rect inRect)
        {
            if (!FluxRaiderCinematic.Active(pawn))
            {
                Close();
                return;
            }

            FluxRaiderCinematic.GetLowReady(pawn, out Vector2 offset, out float angle);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f),
                "Flux Raider low ready");
            Text.Font = GameFont.Small;
            float y = inRect.y + 39f;
            offset.x = SliderRow(inRect, ref y, "Screen X", offset.x, -0.6f, 0.6f);
            offset.y = SliderRow(inRect, ref y, "Screen Z", offset.y, -0.6f, 0.6f);
            angle = SliderRow(inRect, ref y, "Angle", angle, -90f, 90f);
            FluxRaiderCinematic.SetLowReady(pawn, offset, angle);

            if (Widgets.ButtonText(new Rect(inRect.x, y + 5f, 90f, 27f), "Reset"))
                FluxRaiderCinematic.SetLowReady(pawn, new Vector2(0f, -0.26f), 28f);
            if (Widgets.ButtonText(new Rect(inRect.xMax - 90f, y + 5f, 90f, 27f), "Close"))
                Close();
        }

        private static float SliderRow(Rect bounds, ref float y, string label,
            float value, float minimum, float maximum)
        {
            Widgets.Label(new Rect(bounds.x, y, 95f, 24f), label);
            value = Widgets.HorizontalSlider(
                new Rect(bounds.x + 99f, y + 2f, bounds.width - 170f, 18f),
                value, minimum, maximum, true);
            value = Mathf.Round(value * 100f) / 100f;
            Widgets.Label(new Rect(bounds.xMax - 66f, y, 66f, 24f),
                value.ToString("0.00"));
            y += 31f;
            return value;
        }
    }

    [HarmonyPatch(typeof(SelectionDrawer), nameof(SelectionDrawer.DrawSelectionBracketFor))]
    internal static class Patch_FluxRaiderCinematicSelectionBracket
    {
        [HarmonyPrefix]
        private static bool Prefix(object obj)
        {
            return !(obj is Pawn pawn && FluxRaiderCinematic.Active(pawn));
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class Patch_FluxRaiderCinematicAttackOrder
    {
        [HarmonyPrefix]
        private static void Prefix(Pawn ___pawn, Job newJob)
        {
            if (!FluxRaiderCinematic.Active(___pawn) || newJob == null) return;
            Verb verb = newJob.verbToUse
                ?? ___pawn.equipment?.Primary?.GetComp<CompEquippable>()?.PrimaryVerb;
            Thing weapon = ___pawn.equipment?.Primary;
            if (verb == null || weapon == null
                || !Helodrace.Tactical.TacticalAimUtility.IsRangedVerb(verb))
                return;
            bool usesWeapon = verb.EquipmentSource == weapon
                || verb == weapon.TryGetComp<CompEquippable>()?.PrimaryVerb;
            if (usesWeapon && (newJob.def == JobDefOf.AttackStatic
                || newJob.verbToUse == verb))
                FluxRaiderCinematic.BeginAimingForShot(___pawn);
        }
    }

    [HarmonyPatch]
    internal static class Patch_FluxRaiderCinematicCastStart
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in typeof(Verb).GetMethods())
                if (method.Name == nameof(Verb.TryStartCastOn)) yield return method;
        }

        [HarmonyPostfix]
        private static void Postfix(Verb __instance, bool __result)
        {
            if (!__result || !Helodrace.Tactical.TacticalAimUtility.IsRangedVerb(__instance))
                return;
            Pawn pawn = __instance.CasterPawn;
            Thing weapon = pawn?.equipment?.Primary;
            if (weapon != null && (__instance.EquipmentSource == weapon
                || __instance == weapon.TryGetComp<CompEquippable>()?.PrimaryVerb))
                FluxRaiderCinematic.BeginAimingForShot(pawn);
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    internal static class Patch_FluxRaiderCinematicRenderStance
    {
        private sealed class VisualStance : Stance_Busy
        {
            public VisualStance() : base(2) { }
        }

        private static readonly FieldInfo CurrentStance =
            AccessTools.Field(typeof(Pawn_StanceTracker), "curStance");
        private static readonly FieldInfo Focus = AccessTools.Field(typeof(Stance_Busy), "focusTarg");
        private static readonly FieldInfo VerbField = AccessTools.Field(typeof(Stance_Busy), "verb");
        private static readonly FieldInfo NeverAim =
            AccessTools.Field(typeof(Stance_Busy), "neverAimWeapon");

        private sealed class RestoreState
        {
            public Pawn_StanceTracker tracker;
            public Stance oldStance;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Pawn pawn, ref Rot4 __2, ref RestoreState __state)
        {
            if (!FluxRaiderCinematic.Active(pawn) || pawn.stances == null
                || CurrentStance == null || Focus == null) return;
            // Real combat stances must retain their actual target and timing.
            if (pawn.stances.curStance is Stance_Busy busy
                && !busy.neverAimWeapon && busy.focusTarg.IsValid) return;
            __2 = FluxRaiderCinematic.TryLockedDirection(pawn, out Rot4 locked)
                ? locked : pawn.Rotation;
            Stance_Busy visual = new VisualStance();
            IntVec3 facing = __2.FacingCell;
            Focus.SetValue(visual, new LocalTargetInfo(pawn.Position + facing * 6));
            VerbField?.SetValue(visual,
                pawn.equipment.Primary.GetComp<CompEquippable>()?.PrimaryVerb);
            NeverAim?.SetValue(visual, false);
            __state = new RestoreState { tracker = pawn.stances, oldStance = pawn.stances.curStance };
            CurrentStance.SetValue(pawn.stances, visual);
        }

        [HarmonyPostfix]
        private static void Postfix(RestoreState __state) => Restore(__state);

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, RestoreState __state)
        {
            Restore(__state);
            return __exception;
        }

        private static void Restore(RestoreState state)
        {
            if (state?.tracker != null) CurrentStance?.SetValue(state.tracker, state.oldStance);
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming))]
    internal static class Patch_FluxRaiderCinematicEquipmentPose
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Thing eq, ref Vector3 drawLoc, ref float aimAngle)
        {
            Pawn pawn = (eq?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            if (pawn != null) FluxRaiderCinematic.AdjustWeapon(pawn, ref drawLoc, ref aimAngle);
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    internal static class Patch_FluxRaiderCinematicBody
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Pawn ___pawn, ref float angle, ref Rot4 bodyFacing,
            PawnRenderFlags flags)
        {
            if (flags.FlagSet(PawnRenderFlags.Portrait) || !FluxRaiderCinematic.Active(___pawn))
                return;
            FluxCinematicPose pose = FluxRaiderCinematic.Pose(___pawn);
            Rot4 facing = FluxRaiderCinematic.TryLockedDirection(___pawn, out Rot4 locked)
                ? locked : ___pawn.Rotation;
            if (pose == FluxCinematicPose.Prone)
            {
                bodyFacing = Rot4.East;
                angle = facing == Rot4.West ? 270f : 90f;
            }
            else if (pose == FluxCinematicPose.Lean)
                angle += facing == Rot4.West ? -11f : 11f;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Pawn ___pawn, ref PawnDrawParms __result)
        {
            if (__result.flags.FlagSet(PawnRenderFlags.Portrait)
                || !FluxRaiderCinematic.Active(___pawn)
                || FluxRaiderCinematic.Pose(___pawn) != FluxCinematicPose.Prone)
                return;
            __result.posture = PawnPosture.LayingOnGroundNormal;
            __result.crawling = false;
            __result.facing = Rot4.East;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "GetBodyPos")]
    internal static class Patch_FluxRaiderCinematicBodyPosition
    {
        [HarmonyPrefix]
        private static void Prefix(Pawn ___pawn, ref PawnPosture posture)
        {
            if (FluxRaiderCinematic.Active(___pawn)
                && FluxRaiderCinematic.Pose(___pawn) == FluxCinematicPose.Prone)
                posture = PawnPosture.LayingOnGroundNormal;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "RenderPawnAt")]
    internal static class Patch_FluxRaiderCinematicWater
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Pawn ___pawn, ref Rot4? rotOverride)
        {
            if (FluxRaiderCinematic.TryLockedDirection(___pawn, out Rot4 locked))
                rotOverride = locked;
            else if (FluxRaiderCinematic.Active(___pawn)
                && FluxRaiderCinematic.Pose(___pawn) == FluxCinematicPose.Prone)
                rotOverride = ___pawn.Rotation;
        }

        [HarmonyPostfix]
        private static void Postfix(Pawn ___pawn)
        {
            if (!FluxRaiderCinematic.Active(___pawn) || ___pawn?.Spawned != true)
                return;
            TerrainDef terrain = ___pawn.Position.GetTerrain(___pawn.Map);
            if (terrain?.IsWater != true || terrain.DrawMatSingle == null) return;

            // Lay the same water material over the near half of the pawn and weapon.
            // It is drawn above equipment so a camera sees them break the surface.
            Vector3 center = ___pawn.DrawPos + new Vector3(0f, 0f, -0.24f);
            center.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.02f;
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(center, Quaternion.identity, new Vector3(1.3f, 1f, 0.55f)),
                terrain.DrawMatSingle, 0);
        }
    }
}
