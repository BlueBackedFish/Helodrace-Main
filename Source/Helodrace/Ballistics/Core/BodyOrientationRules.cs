using System;

namespace Helodrace.Ballistics
{
    /// <summary>Weapon family that selects a stance profile. Data overrides the heuristic (StanceProfileDef).</summary>
    public enum StanceFamily
    {
        Unarmed,
        Pistol,
        Shotgun,
        Rifle,
        Sniper,
        MachineGun,
        Launcher
    }

    /// <summary>Weapon facts used by the stance heuristic, read once per weapon def.</summary>
    public readonly struct WeaponStanceFacts
    {
        public readonly bool Ranged;
        public readonly float MassKg;
        public readonly float Range;
        public readonly int Burst;
        public readonly bool Shotgun;
        public readonly bool Explosive;

        public WeaponStanceFacts(bool ranged, float massKg, float range, int burst, bool shotgun, bool explosive)
        {
            Ranged = ranged;
            MassKg = massKg;
            Range = range;
            Burst = burst;
            Shotgun = shotgun;
            Explosive = explosive;
        }
    }

    /// <summary>Turn rates and offsets of one stance (degrees, degrees per tick). [game]</summary>
    public readonly struct StanceParameters
    {
        public readonly float TorsoOffset;
        public readonly float HeadOffset;
        public readonly float TorsoRate;
        public readonly float HeadRate;
        public readonly float MoveFactor;
        public readonly int UpdateInterval;

        public StanceParameters(float torsoOffset, float headOffset, float torsoRate, float headRate,
            float moveFactor, int updateInterval)
        {
            TorsoOffset = torsoOffset;
            HeadOffset = headOffset;
            TorsoRate = torsoRate;
            HeadRate = headRate;
            MoveFactor = moveFactor;
            UpdateInterval = Math.Max(1, updateInterval);
        }
    }

    /// <summary>
    /// Continuous torso and head yaw (design doc section 6.1). Same convention as the hull heading: degrees,
    /// 0 = north, clockwise. No Unity/RimWorld types: the rules run in the regression executable.
    /// </summary>
    public static class BodyOrientationRules
    {
        /// <summary>Head may turn this far from the torso. [game]</summary>
        public const float MaxHeadTurn = 70f;
        /// <summary>A sprite keeps its side until the torso is this far past the 45-degree boundary.</summary>
        public const float SpriteHysteresis = 5f;

        /// <summary>Heuristic family of a weapon. [game] Thresholds match the Helod arsenal (docs).</summary>
        public static StanceFamily Classify(in WeaponStanceFacts weapon)
        {
            if (!weapon.Ranged) return StanceFamily.Unarmed;
            if (weapon.Shotgun) return StanceFamily.Shotgun;
            if (weapon.Explosive) return StanceFamily.Launcher;
            if (weapon.MassKg > 0f && weapon.MassKg <= 1.6f) return StanceFamily.Pistol;
            if (weapon.MassKg >= 7f && weapon.Burst >= 3) return StanceFamily.MachineGun;
            if (weapon.Range >= 39f && weapon.Burst <= 1) return StanceFamily.Sniper;
            return StanceFamily.Rifle;
        }

        /// <summary>Rotate from one yaw toward another by at most <paramref name="maxStep"/> degrees.</summary>
        public static float Step(float from, float to, float maxStep)
        {
            float delta = BallisticGeometry.DeltaDegrees(from, to);
            float step = Math.Max(0f, maxStep);
            return BallisticGeometry.NormalizeDegrees(Math.Abs(delta) <= step ? to : from + Math.Sign(delta) * step);
        }

        /// <summary>Head target clamped to the torso's reach.</summary>
        public static float ClampHead(float torsoYaw, float headTarget)
        {
            float delta = Math.Clamp(BallisticGeometry.DeltaDegrees(torsoYaw, headTarget), -MaxHeadTurn, MaxHeadTurn);
            return BallisticGeometry.NormalizeDegrees(torsoYaw + delta);
        }

        /// <summary>
        /// Cardinal sprite angle (0/90/180/270) showing a torso yaw. Keeps the current side inside the hysteresis
        /// band, so a torso at 45 degrees does not flicker between two sprites.
        /// </summary>
        public static float SpriteYaw(float torsoYaw, float currentSpriteYaw)
        {
            float current = BallisticGeometry.NormalizeDegrees((float)Math.Round(currentSpriteYaw / 90f) * 90f);
            if (Math.Abs(BallisticGeometry.DeltaDegrees(current, torsoYaw)) <= 45f + SpriteHysteresis)
                return current % 360f;
            return BallisticGeometry.NormalizeDegrees((float)Math.Round(BallisticGeometry.NormalizeDegrees(torsoYaw) / 90f) * 90f) % 360f;
        }

        /// <summary>Torso target: aim yaw plus the stance offset while aiming, otherwise the intended facing.</summary>
        public static float TorsoTarget(bool aiming, float aimYaw, float intentYaw, in StanceParameters stance) =>
            aiming ? BallisticGeometry.NormalizeDegrees(aimYaw + stance.TorsoOffset) : BallisticGeometry.NormalizeDegrees(intentYaw);

        public static float HeadTarget(bool aiming, float aimYaw, float torsoTarget, in StanceParameters stance) =>
            aiming ? BallisticGeometry.NormalizeDegrees(aimYaw + stance.HeadOffset) : torsoTarget;
    }

    /// <summary>
    /// Mutable orientation of one body. <see cref="Advance"/> integrates the turn analytically from the last
    /// update tick, so reading the yaw at an impact does not depend on how often the body was ticked.
    /// </summary>
    public sealed class BodyOrientationState
    {
        public float TorsoYaw;
        public float HeadYaw;
        public float TorsoTarget;
        public float HeadTarget;
        public int LastTick = int.MinValue;
        public bool Moving;
        public StanceParameters Stance = new StanceParameters(0f, 0f, 12f, 18f, 1f, 1);

        public bool Initialized => LastTick != int.MinValue;

        public void Reset(float yaw, int tick)
        {
            TorsoYaw = HeadYaw = TorsoTarget = HeadTarget = BallisticGeometry.NormalizeDegrees(yaw);
            LastTick = tick;
        }

        public void SetTargets(float torsoTarget, float headTarget)
        {
            TorsoTarget = BallisticGeometry.NormalizeDegrees(torsoTarget);
            HeadTarget = BallisticGeometry.NormalizeDegrees(headTarget);
        }

        public void Advance(int tick)
        {
            if (!Initialized)
            {
                Reset(TorsoTarget, tick);
                return;
            }
            int elapsed = tick - LastTick;
            if (elapsed <= 0) return;
            float factor = Moving ? Stance.MoveFactor : 1f;
            TorsoYaw = BodyOrientationRules.Step(TorsoYaw, TorsoTarget, Stance.TorsoRate * factor * elapsed);
            // The head moves on its own rate but never further than the torso allows at this instant.
            float head = BodyOrientationRules.Step(HeadYaw, HeadTarget, Stance.HeadRate * factor * elapsed);
            HeadYaw = BodyOrientationRules.ClampHead(TorsoYaw, head);
            LastTick = tick;
        }
    }
}
