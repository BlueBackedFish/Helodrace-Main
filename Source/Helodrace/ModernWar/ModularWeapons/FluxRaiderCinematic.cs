using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

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
            public float fromStock;
            public bool stockBeforeAim;
            public bool vanillaAim;
            public IntVec3 aimCell = IntVec3.Invalid;
            public Map aimMap;
            public int transitionTick;
            public bool emittersOn;
            public int reloadTick;
            public int reloadShotTick = -1000;
            public int reloadShotSequence;
            public bool reloadPlayed;
            public float reloadPreview = -1f;
            public int shotSequence;
            public int lastVisualShotTick = -1000;
            public int pendingShotTick = -1;
            public bool pendingReload;
            public int lastDustTick = -1000;
            public bool directionLocked;
            public Rot4 lockedDirection;
            public Vector2 lowReadyOffset = new Vector2(0f, -0.26f);
            public float lowReadyAngle = 28f;
            public ThingWithComps visualSpare;
        }

        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        private const int RaiseTicks = 14;
        private const int StockLeadTicks = 10;
        private const int ShotToReloadTicks = 8;

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
            if (!Prefs.DevMode || pawn == null
                || !states.TryGetValue(pawn.thingIDNumber, out State state))
                return null;
            if (pawn.equipment?.Primary?.thingIDNumber != state.weaponId
                || !IsFluxRaider(pawn))
            {
                states.Remove(pawn.thingIDNumber);
                return null;
            }
            return state;
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
            {
                if (state.vanillaAim && state.aimMap == pawn.Map
                    && state.aimCell.IsValid)
                    SetVanillaAiming(pawn, state.stockBeforeAim,
                        new LocalTargetInfo(state.aimCell));
                else
                    SetAiming(pawn, state.stockBeforeAim);
            }
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
            int delay = target > state.fromRaise && state.stockBeforeAim
                && state.fromStock < 0.99f
                ? StockLeadTicks : 0;
            float t = Ease((Now - state.transitionTick - delay) / (float)RaiseTicks);
            return Mathf.Lerp(state.fromRaise, target, t);
        }

        public static float StockExtension(Pawn pawn)
        {
            State state = For(pawn);
            if (state == null) return 1f;
            float target = state.pose == FluxCinematicPose.LowReady ? 0f : 1f;
            int delay = target < state.fromStock && state.stockBeforeAim
                ? RaiseTicks : 0;
            int duration = state.stockBeforeAim ? StockLeadTicks : RaiseTicks;
            float t = Ease((Now - state.transitionTick - delay) / (float)duration);
            return Mathf.Lerp(state.fromStock, target, t);
        }

        private static float Ease(float progress)
        {
            float t = Mathf.Clamp01(progress);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        public static void SetAiming(Pawn pawn, bool stockBeforeAim)
        {
            State state = For(pawn);
            if (state == null) return;
            float currentRaise = Raise(pawn);
            float currentStock = StockExtension(pawn);
            state.stockBeforeAim = stockBeforeAim;
            state.vanillaAim = false;
            state.aimCell = IntVec3.Invalid;
            state.pose = FluxCinematicPose.Aiming;
            state.fromRaise = currentRaise;
            state.fromStock = currentStock;
            state.transitionTick = Now;
            state.pendingShotTick = -1;
        }

        public static void SetVanillaAiming(Pawn pawn, bool stockBeforeAim,
            LocalTargetInfo target)
        {
            if (!target.IsValid || pawn?.Map == null) return;
            SetAiming(pawn, stockBeforeAim);
            State state = For(pawn);
            if (state == null) return;
            state.vanillaAim = true;
            state.aimCell = target.Cell;
            state.aimMap = pawn.Map;
            Vector3 direction = target.CenterVector3 - pawn.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                FaceForPose(pawn, Rot4.FromAngleFlat(direction.AngleFlat()));
        }

        public static void BeginVanillaAiming(Pawn pawn, bool stockBeforeAim)
        {
            if (For(pawn) == null || pawn.Map == null) return;
            Map map = pawn.Map;
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetLocations = true,
                canTargetPawns = true,
                canTargetBuildings = true,
                canTargetItems = false,
                validator = target => target.IsValid
                    && target.Cell.InBounds(map)
                    && target.Cell != pawn.Position
            }, target => SetVanillaAiming(pawn, stockBeforeAim, target));
        }

        public static LocalTargetInfo VisualFocus(Pawn pawn, Rot4 facing)
        {
            State state = For(pawn);
            if (state?.vanillaAim == true && state.aimMap == pawn.Map
                && state.aimCell.IsValid)
                return new LocalTargetInfo(state.aimCell);
            return new LocalTargetInfo(pawn.Position + facing.FacingCell * 6);
        }

        public static void SetPose(Pawn pawn, FluxCinematicPose pose)
        {
            State state = For(pawn);
            if (state == null) return;
            float current = Raise(pawn);
            float currentStock = StockExtension(pawn);
            state.pose = pose;
            state.fromRaise = current;
            state.fromStock = currentStock;
            state.transitionTick = Now;
            state.pendingShotTick = -1;
            if (pose == FluxCinematicPose.Lean || pose == FluxCinematicPose.Prone)
                ThrowDust(pawn, 4);
        }

        public static void StartReload(Pawn pawn)
        {
            QueueVisualShot(pawn, true);
        }

        public static void FireVisual(Pawn pawn)
        {
            QueueVisualShot(pawn, false);
        }

        private static void QueueVisualShot(Pawn pawn, bool reload)
        {
            State state = For(pawn);
            if (state == null) return;
            BeginAimingForShot(pawn);
            int raiseDelay = state.stockBeforeAim && state.fromStock < 0.99f
                ? StockLeadTicks : 0;
            int raiseEnd = state.transitionTick + RaiseTicks + raiseDelay;
            int stockEnd = state.transitionTick
                + (state.stockBeforeAim ? StockLeadTicks : RaiseTicks);
            if (Raise(pawn) >= 0.995f && StockExtension(pawn) >= 0.995f)
            {
                PlayVisualShot(pawn, reload);
                return;
            }
            state.pendingShotTick = Mathf.Max(Now, Mathf.Max(raiseEnd, stockEnd));
            state.pendingReload = reload;
        }

        public static void Tick(Pawn pawn)
        {
            if (pawn == null || !states.TryGetValue(pawn.thingIDNumber, out State state)
                || state.pendingShotTick < 0 || Now < state.pendingShotTick)
                return;
            bool reload = state.pendingReload;
            state.pendingShotTick = -1;
            PlayVisualShot(pawn, reload);
        }

        private static void PlayVisualShot(Pawn pawn, bool reload)
        {
            State state = For(pawn);
            ThingWithComps weapon = pawn?.equipment?.Primary;
            CompModularWeaponNode root = weapon?.GetComp<CompModularWeaponNode>();
            Verb verb = weapon?.GetComp<CompEquippable>()?.PrimaryVerb;
            if (state == null || root?.Props.isAssemblyRoot != true
                || pawn.Spawned != true || pawn.Dead || pawn.Downed
                || pawn.Map == null) return;

            LocalTargetInfo focus = VisualFocus(pawn, pawn.Rotation);
            ModularWeaponCycleUtility.NotifyCinematicShot(root, pawn, focus);
            state.lastVisualShotTick = Now;
            TargetInfo soundTarget = new TargetInfo(pawn.Position, pawn.Map);
            (root.SoundCastOverride ?? verb?.verbProps?.soundCast)
                ?.PlayOneShot(soundTarget);
            (root.SoundCastTailOverride ?? verb?.verbProps?.soundCastTail)
                ?.PlayOneShot(soundTarget);
            if (reload)
            {
                state.reloadShotTick = Now;
                state.reloadShotSequence = state.shotSequence;
                state.reloadTick = Now + ShotToReloadTicks;
                state.reloadPlayed = true;
                state.reloadPreview = -1f;
            }
        }

        public static bool HasReloadPlayed(Pawn pawn) => For(pawn)?.reloadPlayed == true;

        public static void ResetReload(Pawn pawn)
        {
            State state = For(pawn);
            if (state == null) return;
            state.reloadPlayed = false;
            state.reloadPreview = -1f;
            state.pendingShotTick = -1;
        }

        public static void SetReloadPreview(Pawn pawn, float progress)
        {
            State state = For(pawn);
            if (state != null) state.reloadPreview = Mathf.Clamp01(progress);
        }

        public static void ClearReloadPreview(Pawn pawn)
        {
            State state = For(pawn);
            if (state != null) state.reloadPreview = -1f;
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
            if (state.reloadPreview >= 0f)
            {
                progress = state.reloadPreview;
                return true;
            }
            if (!state.reloadPlayed) return false;
            int duration = FluxRaiderReloadTimeline.Settings.reloadDurationTicks;
            progress = Mathf.Clamp01((Now - state.reloadTick)
                / (float)Mathf.Max(12, duration));
            return true;
        }

        public static bool TrySlideAmount(CompModularWeaponNode root,
            out float amount)
        {
            amount = 0f;
            Pawn pawn = (root?.parent?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            State state = For(pawn);
            if (state == null) return false;
            if (state.reloadPreview >= 0f)
            {
                ReloadProgress(root, out float progress);
                amount = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.94f, 1f, progress));
                return true;
            }
            if (!state.reloadPlayed || state.shotSequence > state.reloadShotSequence)
                return false;
            if (Now < state.reloadTick)
            {
                amount = Ease((Now - state.reloadShotTick) / 6f);
                return true;
            }
            ReloadProgress(root, out float reloadProgress);
            amount = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.94f, 1f, reloadProgress));
            return true;
        }

        public static bool TryMagazinePose(CompModularWeaponNode root,
            ModularRenderNode node, out Vector2 offset, out float angle,
            out float layer)
        {
            offset = Vector2.zero;
            angle = 0f;
            layer = node?.GraphicLayer ?? 0f;
            if (node == null || !ReloadProgress(root, out float progress))
                return false;
            bool spare = IsFrontMagazine(node);
            if (!spare && !IsMagazineNode(node)) return false;
            FluxMagazineKeyframe frame = FluxRaiderReloadTimeline.Sample(
                spare ? FluxRaiderReloadTimeline.Settings.Spare
                    : FluxRaiderReloadTimeline.Settings.Dropped,
                progress);
            offset = new Vector2(frame.x, frame.z)
                * Mathf.Max(0.01f, root.Props.assemblyScale);
            angle = frame.angle;
            if (spare)
            {
                ModularRenderNode magazine = root.RenderSnapshot()
                    .FirstOrDefault(n => n.Props.magazineCapacity > 0);
                if (magazine != null)
                {
                    offset += (magazine.GraphicCenter - node.GraphicCenter)
                        * frame.travel;
                    layer = Mathf.Lerp(node.GraphicLayer, magazine.GraphicLayer,
                        frame.travel);
                }
            }
            return true;
        }

        public static List<ModularRenderNode> WithVisualSpare(
            CompModularWeaponNode root, List<ModularRenderNode> visibleNodes)
        {
            Pawn pawn = (root?.parent?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            State state = For(pawn);
            if (state == null || visibleNodes == null
                || visibleNodes.Any(IsFrontMagazine)) return visibleNodes;

            List<ModularRenderNode> assembly = root.RenderSnapshot();
            ModularRenderNode receiver = assembly.FirstOrDefault(n =>
                n.thing?.def?.defName == "HD_ModularPart_Receiver_FluxRaiderKit"
                || n.thing?.def?.defName == "HD_ModularPart_Receiver_FluxRaiderKitTan");
            ModularAttachmentSocket socket = receiver?.Props.SocketNamed("front_magazine");
            if (socket == null) return visibleNodes;

            if (state.visualSpare == null)
            {
                int capacity = assembly.FirstOrDefault(n => n.Props.magazineCapacity > 0)
                    ?.Props.magazineCapacity ?? 21;
                if (capacity != 17 && capacity != 21 && capacity != 30)
                    capacity = 21;
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(
                    "HD_ModularPart_FrontMagazine_P320" + capacity);
                state.visualSpare = def == null ? null
                    : ThingMaker.MakeThing(def) as ThingWithComps;
            }
            CompModularWeaponNode spareComp = state.visualSpare
                ?.GetComp<CompModularWeaponNode>();
            ModularAttachmentMount mount = spareComp?.Props.MountNamed(
                "front_magazine_mount");
            if (mount == null) return visibleNodes;

            float paletteRatio = ModularWeaponPaletteScaleUtility.Relative(
                state.visualSpare.def, receiver.thing.def);
            ModularTransform2D transform = receiver.transform.Attach(
                socket.transform, mount.EffectiveTransform(socket),
                spareComp.Props.graphicAngle, paletteRatio);
            ModularRenderNode visual = new ModularRenderNode
            {
                path = receiver.path + "/front_magazine[cinematic]",
                depth = receiver.depth + 1,
                thing = state.visualSpare,
                comp = spareComp,
                transform = transform,
                parentSocketId = "front_magazine",
                mountId = "front_magazine_mount",
                parentComp = receiver.comp
            };
            List<ModularRenderNode> result = new List<ModularRenderNode>(visibleNodes);
            int insertAt = result.FindIndex(n => n.GraphicLayer > visual.GraphicLayer);
            if (insertAt < 0) result.Add(visual);
            else result.Insert(insertAt, visual);
            return result;
        }

        private static bool IsFrontMagazine(ModularRenderNode node) =>
            node?.thing?.def?.defName.StartsWith("HD_ModularPart_FrontMagazine_P320") == true;

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

        public static void NotifyShot(Pawn pawn)
        {
            State state = For(pawn);
            if (state != null)
            {
                state.shotSequence++;
            }
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
            float remaining = 1f - raise;
            Vector2 control = lowReadyOffset + new Vector2(side * 0.12f, 0.16f);
            Vector2 curved = For(pawn)?.vanillaAim == true
                ? lowReadyOffset * remaining
                : lowReadyOffset * (remaining * remaining)
                    + control * (2f * remaining * raise);
            drawLoc += new Vector3(curved.x, 0f, curved.y);
            aimAngle += side * lowReadyAngle * (1f - raise);
            State state = For(pawn);
            float shotProgress = (Now - (state?.lastVisualShotTick ?? -1000)) / 9f;
            if (shotProgress >= 0f && shotProgress < 1f)
            {
                float kick = 1f - Mathf.SmoothStep(0f, 1f, shotProgress);
                drawLoc.z += 0.045f * kick;
                aimAngle += side * 4f * kick;
            }
            if (Pose(pawn) == FluxCinematicPose.Prone)
                drawLoc += new Vector3(0f, 0f, -0.17f);
            else if (Pose(pawn) == FluxCinematicPose.Lean)
                drawLoc += new Vector3(side * 0.1f, 0f, 0f);
        }
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    internal static class Patch_FluxRaiderCinematicQueuedShot
    {
        [HarmonyPostfix]
        private static void Postfix(Pawn __instance)
        {
            FluxRaiderCinematic.Tick(__instance);
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
            yield return new Command_Action
            {
                defaultLabel = "CINE: controls",
                defaultDesc = "Open the movable Flux Raider filming controls.",
                icon = __instance.equipment.Primary.def.uiIcon,
                action = () => Dialog_FluxRaiderCinematicControls.Open(__instance)
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
            Focus.SetValue(visual, FluxRaiderCinematic.VisualFocus(pawn, __2));
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
