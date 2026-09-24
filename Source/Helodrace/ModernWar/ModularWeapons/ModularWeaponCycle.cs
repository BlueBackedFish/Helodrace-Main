using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public static class ModularWeaponCycleUtility
    {
        private sealed class CycleState
        {
            public int shotTick;
            public float cycleTicks;
        }

        private sealed class PendingCasing
        {
            public int dueTick;
            public Verb verb;
            public CompModularWeaponNode root;
        }

        private sealed class ReloadState
        {
            public int startTick;
            public int durationTicks;
            public bool startedEmpty;
            public bool releasesBoltCatch;
        }

        private static readonly Dictionary<int, CycleState> states =
            new Dictionary<int, CycleState>();
        private static readonly List<PendingCasing> pendingCasings =
            new List<PendingCasing>();
        private static readonly Dictionary<int, ReloadState> reloadStates =
            new Dictionary<int, ReloadState>();
        private static readonly Dictionary<int, CompModularWeaponNode> caughtBolts =
            new Dictionary<int, CompModularWeaponNode>();
        private static readonly List<int> cleanupIds = new List<int>();
        private static int nextCleanupTick;

        private sealed class GripState
        {
            public CompModularWeaponNode root;
            public float amount;
        }

        private static readonly Dictionary<int, GripState> grips = new Dictionary<int, GripState>();

        private static bool GripPressed(CompModularWeaponNode root)
        {
            Pawn pawn = (root?.parent?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            return pawn?.Drafted == true && !pawn.Dead && !pawn.Downed;
        }

        /// <summary>Sets the visual state of an AR-pattern last-round bolt catch.</summary>
        public static void NotifyBoltCatch(
            CompModularWeaponNode root,
            bool caught)
        {
            if (root?.Props.isAssemblyRoot != true || root.parent == null) return;
            if (!SupportsAr15BoltCatch(root)) return;
            int id = root.parent.thingIDNumber;
            if (caught)
                caughtBolts[id] = root;
            else
                caughtBolts.Remove(id);
        }

        /// <summary>
        /// Compatibility layers call this when their real reload job starts. Keeping
        /// the state in the base renderer lets every modular part share one animation
        /// without taking a compile-time dependency on an ammunition mod.
        /// </summary>
        public static void NotifyReload(
            CompModularWeaponNode root,
            int durationTicks,
            bool startedEmpty)
        {
            if (root?.Props.isAssemblyRoot != true || root.parent == null) return;
            int id = root.parent.thingIDNumber;
            bool releasesBoltCatch = startedEmpty && caughtBolts.Remove(id);
            reloadStates[id] = new ReloadState
            {
                startTick = Find.TickManager?.TicksGame ?? 0,
                durationTicks = Mathf.Max(12, durationTicks),
                startedEmpty = startedEmpty,
                releasesBoltCatch = releasesBoltCatch
            };
        }

        public static void NotifyShot(
            Verb verb,
            CompModularWeaponNode root)
        {
            if (verb == null || root?.Props.isAssemblyRoot != true) return;
            ModularWeaponMuzzleEffectUtility.NotifyShot(verb, root);
            int now = Find.TickManager?.TicksGame ?? 0;
            float cycleTicks = Mathf.Max(1f, root.EffectiveBurstIntervalTicks / Mathf.Max(0.01f, root.Props.animationSpeed));
            states[root.parent.thingIDNumber] = new CycleState
            {
                shotTick = now,
                cycleTicks = cycleTicks
            };

            float fraction = ModularWeaponCasingUtility.EjectionCycleFraction(root);
            pendingCasings.Add(new PendingCasing
            {
                dueTick = now + Mathf.Max(
                    1,
                    Mathf.RoundToInt(cycleTicks * fraction)),
                verb = verb,
                root = root
            });
        }

        public static bool HasCustomCasingEjection(
            CompModularWeaponNode root)
        {
            if (root?.Props.isAssemblyRoot != true) return false;

            bool hasPort = false;
            bool hasCasingMote = false;
            List<ModularRenderNode> nodes = root.RenderSnapshot();
            for (int i = 0; i < nodes.Count; i++)
            {
                CompProperties_ModularWeaponNode props = nodes[i].Props;
                hasPort |= props.casingPort != null;
                hasCasingMote |= props.casingMoteDef != null;
                if (hasPort && hasCasingMote) return true;
            }
            return false;
        }

        public static bool TryGetProgress(
            CompModularWeaponNode root,
            out float progress)
        {
            progress = 1f;
            if (root?.parent == null) return false;
            CycleState state;
            if (!states.TryGetValue(root.parent.thingIDNumber, out state))
                return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            int elapsed = now - state.shotTick;
            if (elapsed < 0 || elapsed > state.cycleTicks) return false;
            progress = Mathf.Clamp01(elapsed / (float)state.cycleTicks);
            return true;
        }

        public static float AnimationAmount(
            CompModularWeaponNode root,
            ModularWeaponAnimatedPartKind kind)
        {
            float progress;
            if (kind == ModularWeaponAnimatedPartKind.None) return 0f;
            if (kind == ModularWeaponAnimatedPartKind.GripSafety)
            {
                if (root?.parent == null) return 0f;
                GripState grip;
                if (!grips.TryGetValue(root.parent.thingIDNumber, out grip))
                {
                    grip = new GripState { root = root, amount = 0f };
                    grips[root.parent.thingIDNumber] = grip;
                }
                return root.parent.ParentHolder is Pawn_EquipmentTracker ? grip.amount : 0f;
            }
            if (kind == ModularWeaponAnimatedPartKind.Slide)
                return AnimationAmount(root, ModularWeaponAnimatedPartKind.Bolt);
            if (kind == ModularWeaponAnimatedPartKind.TiltingBarrel)
            {
                float slide = AnimationAmount(root, ModularWeaponAnimatedPartKind.Slide);
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.65f, slide));
            }
            if (kind == ModularWeaponAnimatedPartKind.Hammer)
            {
                // Authored pose is decocked; rotate around the animation pivot,
                // not the attachment mount, when held ready like the grip safety.
                float cocked = AnimationAmount(root, ModularWeaponAnimatedPartKind.GripSafety);
                return TryGetProgress(root, out progress)
                    ? cocked * Mathf.SmoothStep(0f, 1f, progress / 0.42f)
                    : cocked;
            }

            float firingAmount = 0f;
            bool firing = TryGetProgress(root, out progress);
            bool boltCaught = kind == ModularWeaponAnimatedPartKind.Bolt
                && IsBoltCaught(root);
            if (firing)
            {
                if (kind == ModularWeaponAnimatedPartKind.Trigger)
                    firingAmount = 1f - Mathf.SmoothStep(
                        0f, 1f, progress / 0.32f);

                if (kind == ModularWeaponAnimatedPartKind.Bolt)
                {
                    const float rearwardEnd = 0.42f;
                    if (boltCaught)
                    {
                        firingAmount = progress <= rearwardEnd
                            ? Mathf.SmoothStep(0f, 1f, progress / rearwardEnd)
                            : 1f;
                    }
                    else
                    {
                        firingAmount = progress <= rearwardEnd
                            ? Mathf.SmoothStep(0f, 1f, progress / rearwardEnd)
                            : 1f - Mathf.SmoothStep(
                                0f,
                                1f,
                                (progress - rearwardEnd) / (1f - rearwardEnd));
                    }
                }
            }

            float reloadAmount = kind == ModularWeaponAnimatedPartKind.Bolt
                ? ReloadBoltAmount(root)
                : 0f;
            float heldAmount = boltCaught && !firing ? 1f : 0f;
            return Mathf.Max(heldAmount, Mathf.Max(firingAmount, reloadAmount));
        }

        public static Vector2 ReloadMagazineOffset(
            CompModularWeaponNode root,
            ModularRenderNode node)
        {
            if (!BelongsToMagazine(node)) return Vector2.zero;

            float progress;
            bool startedEmpty;
            if (!TryGetReloadProgress(root, out progress, out startedEmpty))
                return Vector2.zero;

            // Empty reloads reserve the last part of the same total duration for the
            // charging handle, so their magazine swap completes sooner.
            float removeEnd = startedEmpty ? 0.12f : 0.18f;
            float hiddenEnd = startedEmpty ? 0.42f : 0.58f;
            float seatEnd = startedEmpty ? 0.78f : 0.94f;
            float amount;
            if (progress < removeEnd)
                amount = Mathf.SmoothStep(0f, 1f, progress / removeEnd);
            else if (progress < hiddenEnd)
                amount = 1f;
            else if (progress < seatEnd)
                amount = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(hiddenEnd, seatEnd, progress));
            else
                amount = 0f;

            return new Vector2(0.015f * amount, -0.18f * amount);
        }

        public static bool HideMagazineForReload(
            CompModularWeaponNode root,
            ModularRenderNode node)
        {
            if (!BelongsToMagazine(node)) return false;

            float progress;
            bool startedEmpty;
            if (!TryGetReloadProgress(root, out progress, out startedEmpty))
                return false;
            float removeEnd = startedEmpty ? 0.12f : 0.18f;
            float hiddenEnd = startedEmpty ? 0.42f : 0.58f;
            return progress >= removeEnd && progress < hiddenEnd;
        }

        private static float ReloadBoltAmount(CompModularWeaponNode root)
        {
            float progress;
            bool startedEmpty;
            if (!TryGetReloadProgress(root, out progress, out startedEmpty)
                || !startedEmpty)
                return 0f;

            // Pulling a charging handle is deliberately slower than the powered
            // firing cycle. Return speed stays tied to the normal bolt return phase.
            float normalCycleTicks = Mathf.Max(1f, root.EffectiveBurstIntervalTicks / Mathf.Max(0.01f, root.Props.animationSpeed));
            int normalReturnTicks = Mathf.Max(
                2,
                Mathf.RoundToInt(normalCycleTicks * 0.58f));
            ReloadState state = reloadStates[root.parent.thingIDNumber];
            int elapsedTicks = Mathf.Clamp(
                Mathf.RoundToInt(progress * state.durationTicks),
                0,
                state.durationTicks);
            int returnStartTick = state.durationTicks - normalReturnTicks;

            // A caught AR-15 bolt is already fully rearward. The empty reload only
            // presses the catch after seating the magazine, so hold it open until the
            // normal-speed return phase instead of pulling the charging handle again.
            if (state.releasesBoltCatch)
            {
                if (elapsedTicks <= returnStartTick) return 1f;
                return 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        returnStartTick,
                        state.durationTicks,
                        elapsedTicks));
            }

            int desiredPullTicks = Mathf.RoundToInt(state.durationTicks * 0.20f);
            // Even on an unusually fast stat-scaled reload, reserve at least one
            // normal return-time for rearward travel. Two extra ticks keep the pull
            // perceptibly slower than the powered return.
            int pullTicks = Mathf.Max(normalReturnTicks + 2, desiredPullTicks);
            pullTicks = Mathf.Min(
                pullTicks,
                Mathf.Max(normalReturnTicks, state.durationTicks - normalReturnTicks));
            int pullStartTick = Mathf.Max(0, returnStartTick - pullTicks);
            if (elapsedTicks < pullStartTick) return 0f;

            return elapsedTicks <= returnStartTick
                ? Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        pullStartTick,
                        returnStartTick,
                        elapsedTicks))
                : 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        returnStartTick,
                        state.durationTicks,
                        elapsedTicks));
        }

        private static bool IsBoltCaught(CompModularWeaponNode root)
        {
            return root?.parent != null
                && caughtBolts.ContainsKey(root.parent.thingIDNumber);
        }

        private static bool SupportsAr15BoltCatch(CompModularWeaponNode root)
        {
            List<ModularRenderNode> nodes = root?.RenderSnapshot();
            if (nodes == null) return false;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i]?.thing?.def?.defName == "HD_ModularPart_Bolt_AR15"
                    || nodes[i]?.Props.animatedPart == ModularWeaponAnimatedPartKind.Slide)
                    return true;
            return false;
        }

        private static bool TryGetReloadProgress(
            CompModularWeaponNode root,
            out float progress,
            out bool startedEmpty)
        {
            progress = 1f;
            startedEmpty = false;
            if (root?.parent == null) return false;

            ReloadState state;
            if (!reloadStates.TryGetValue(root.parent.thingIDNumber, out state))
                return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            int elapsed = now - state.startTick;
            if (elapsed < 0 || elapsed > state.durationTicks) return false;
            progress = Mathf.Clamp01(elapsed / (float)state.durationTicks);
            startedEmpty = state.startedEmpty;
            return true;
        }

        private static bool BelongsToMagazine(ModularRenderNode node)
        {
            CompModularWeaponNode current = node?.comp;
            HashSet<CompModularWeaponNode> visited =
                new HashSet<CompModularWeaponNode>();
            while (current != null && visited.Add(current))
            {
                if (current.Props.magazineCapacity > 0) return true;
                current = current.parent?.ParentHolder as CompModularWeaponNode;
            }
            return false;
        }

        public static bool TryGetFeedingRound(
            CompModularWeaponNode root,
            out ModularRenderNode portNode,
            out ModularWeaponCasingPortProperties port,
            out string texturePath,
            out float feedProgress)
        {
            portNode = null;
            port = null;
            texturePath = null;
            feedProgress = 0f;
            float cycleProgress;
            if (!TryGetProgress(root, out cycleProgress)) return false;

            List<ModularRenderNode> nodes = root.RenderSnapshot();
            int texturePriority = int.MinValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                CompProperties_ModularWeaponNode props = nodes[i].Props;
                if (portNode == null && props.casingPort != null)
                {
                    portNode = nodes[i];
                    port = props.casingPort;
                }
                if (!props.feedingRoundTexPath.NullOrEmpty()
                    && props.overridePriority >= texturePriority)
                {
                    texturePath = props.feedingRoundTexPath;
                    texturePriority = props.overridePriority;
                }
            }
            if (portNode == null || port == null || texturePath.NullOrEmpty())
                return false;
            if (cycleProgress < port.feedStartCycleFraction
                || cycleProgress > port.feedEndCycleFraction)
                return false;
            feedProgress = Mathf.InverseLerp(
                port.feedStartCycleFraction,
                port.feedEndCycleFraction,
                cycleProgress);
            feedProgress = Mathf.SmoothStep(0f, 1f, feedProgress);
            return true;
        }

        public static void TickPending()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (grips.Count == 0 && pendingCasings.Count == 0
                && states.Count == 0 && reloadStates.Count == 0
                && caughtBolts.Count == 0)
            {
                return;
            }

            cleanupIds.Clear();
            foreach (KeyValuePair<int, GripState> pair in grips)
            {
                GripState grip = pair.Value;
                grip.amount = Mathf.MoveTowards(grip.amount, GripPressed(grip.root) ? 1f : 0f, 1f / 6f);
                if (grip.root?.parent == null || grip.root.parent.Destroyed
                    || (grip.amount == 0f && !(grip.root.parent.ParentHolder is Pawn_EquipmentTracker)))
                    cleanupIds.Add(pair.Key);
            }
            for (int i = 0; i < cleanupIds.Count; i++) grips.Remove(cleanupIds[i]);
            for (int i = pendingCasings.Count - 1; i >= 0; i--)
            {
                PendingCasing pending = pendingCasings[i];
                if (pending.dueTick > now) continue;
                pendingCasings.RemoveAt(i);
                ModularWeaponCasingUtility.TryEject(pending.verb, pending.root);
            }

            if (now < nextCleanupTick)
            {
                return;
            }

            nextCleanupTick = now + 120;
            CleanupExpired(now);
        }

        internal static void CleanupExpired(int now)
        {
            cleanupIds.Clear();
            foreach (KeyValuePair<int, CycleState> pair in states)
            {
                if (now - pair.Value.shotTick > pair.Value.cycleTicks + 120)
                    cleanupIds.Add(pair.Key);
            }
            for (int i = 0; i < cleanupIds.Count; i++) states.Remove(cleanupIds[i]);

            cleanupIds.Clear();
            foreach (KeyValuePair<int, ReloadState> pair in reloadStates)
            {
                if (now - pair.Value.startTick > pair.Value.durationTicks + 120)
                    cleanupIds.Add(pair.Key);
            }
            for (int i = 0; i < cleanupIds.Count; i++) reloadStates.Remove(cleanupIds[i]);

            cleanupIds.Clear();
            foreach (KeyValuePair<int, CompModularWeaponNode> pair in caughtBolts)
            {
                if (pair.Value?.parent == null || pair.Value.parent.Destroyed)
                    cleanupIds.Add(pair.Key);
            }
            for (int i = 0; i < cleanupIds.Count; i++) caughtBolts.Remove(cleanupIds[i]);
        }
    }
}
