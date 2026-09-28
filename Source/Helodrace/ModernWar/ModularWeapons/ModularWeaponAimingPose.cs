using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    /// <summary>
    /// Shares the exact transform received by DrawEquipmentAiming with effects drawn or
    /// spawned outside that callback. Reconstructing drawLoc independently misses
    /// direction-specific pawn/equipment offsets, which is most visible while aiming east.
    /// </summary>
    internal static class ModularWeaponAimingPoseUtility
    {
        private sealed class Pose
        {
            public int tick;
            public float aimAngle;
            public Vector3 drawLoc;
            public float bodyAngle;
            public bool flipped;
            public bool recoilActive;
        }

        private static readonly Dictionary<int, Pose> poses =
            new Dictionary<int, Pose>();

        public static void Record(
            Thing equipment,
            Vector3 drawLoc,
            float aimAngle,
            float bodyAngle,
            bool flipped,
            bool recoilActive)
        {
            if (equipment == null) return;
            poses[equipment.thingIDNumber] = new Pose
            {
                tick = Find.TickManager?.TicksGame ?? 0,
                aimAngle = aimAngle,
                drawLoc = drawLoc,
                bodyAngle = bodyAngle,
                flipped = flipped,
                recoilActive = recoilActive
            };
            if (poses.Count > 512) RemoveStalePoses();
        }

        public static void Resolve(
            Thing equipment,
            Pawn wielder,
            float aimAngle,
            out Vector3 drawLoc,
            out float bodyAngle,
            out bool flipped)
        {
            bool recoilActive;
            Resolve(
                equipment,
                wielder,
                aimAngle,
                out drawLoc,
                out bodyAngle,
                out flipped,
                out recoilActive);
        }

        public static void Resolve(
            Thing equipment,
            Pawn wielder,
            float aimAngle,
            out Vector3 drawLoc,
            out float bodyAngle,
            out bool flipped,
            out bool recoilActive)
        {
            Pose pose;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (equipment != null
                && poses.TryGetValue(equipment.thingIDNumber, out pose)
                && now - pose.tick <= 3
                && Mathf.Abs(Mathf.DeltaAngle(pose.aimAngle, aimAngle)) <= 3f)
            {
                drawLoc = pose.drawLoc;
                bodyAngle = pose.bodyAngle;
                flipped = pose.flipped;
                recoilActive = pose.recoilActive;
                return;
            }

            CalculateFallback(
                equipment,
                wielder,
                aimAngle,
                out drawLoc,
                out bodyAngle,
                out flipped,
                out recoilActive);
        }

        private static void CalculateFallback(
            Thing equipment,
            Pawn wielder,
            float aimAngle,
            out Vector3 drawLoc,
            out float bodyAngle,
            out bool flipped,
            out bool recoilActive)
        {
            float distanceFactor = wielder?.ageTracker?.CurLifeStage
                ?.equipmentDrawDistanceFactor ?? 1f;
            drawLoc = (wielder?.DrawPos ?? Vector3.zero)
                + new Vector3(
                    0f,
                    0f,
                    0.4f + (equipment?.def.equippedDistanceOffset ?? 0f))
                    .RotatedBy(aimAngle) * distanceFactor;

            bodyAngle = aimAngle - 90f;
            flipped = aimAngle > 200f && aimAngle < 340f;
            if (flipped)
            {
                bodyAngle -= 180f;
                bodyAngle -= equipment?.def.equippedAngleOffset ?? 0f;
            }
            else
            {
                bodyAngle += equipment?.def.equippedAngleOffset ?? 0f;
            }

            Vector3 recoilOffset;
            float recoilAngle;
            ModularWeaponRecoilUtility.Resolve(
                equipment,
                aimAngle,
                out recoilOffset,
                out recoilAngle);
            recoilActive = recoilOffset.sqrMagnitude > 0.000001f
                || Mathf.Abs(recoilAngle) > 0.001f;
            drawLoc += recoilOffset;
            bodyAngle += flipped ? -recoilAngle : recoilAngle;
            bodyAngle %= 360f;
        }

        private static void RemoveStalePoses()
        {
            int threshold = (Find.TickManager?.TicksGame ?? 0) - 120;
            List<int> stale = new List<int>();
            foreach (KeyValuePair<int, Pose> pair in poses)
                if (pair.Value.tick < threshold) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++) poses.Remove(stale[i]);
        }
    }
}
