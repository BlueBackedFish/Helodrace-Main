using System;
using System.Collections.Generic;
using System.Numerics;

namespace Helodrace.Ballistics
{
    /// <summary>Evidence grade of a value (design doc section 0). Mixed grades are never silently combined.</summary>
    public enum EvidenceGrade
    {
        Physical,
        Experimental,
        Game
    }

    /// <summary>How a round defeats armor. Chosen from data only, never guessed from a def name.</summary>
    public enum PenetrationMechanism
    {
        KineticBullet,
        KineticHardCore,
        KineticLongRod,
        ShapedCharge,
        Tandem,
        HighExplosive,
        HighExplosiveSquashHead,
        HighExplosiveFragmentation,
        Shot,
        Fragment
    }

    public enum PenetratorKind
    {
        Kinetic,
        Jet,
        Blast,
        Fragment,
        Shot
    }

    public enum PenetratorCondition
    {
        Intact,
        Deformed,
        Eroded,
        Fragmented,
        Detonated,
        Spent
    }

    public enum ImpactResult
    {
        None,
        Ricochet,
        Shatter,
        Stopped,
        PartialPenetration,
        Perforation,
        Overmatch
    }

    public enum ArmorLayerRole
    {
        Plate,
        Spaced,
        Reactive,
        Backing,
        Liner
    }

    public enum SecondaryEventKind
    {
        Spall,
        ProjectileFragment,
        WarheadFragment,
        Blast,
        Trauma
    }

    public static class PenetrationMechanisms
    {
        public static PenetratorKind InitialKind(PenetrationMechanism mechanism)
        {
            switch (mechanism)
            {
                case PenetrationMechanism.ShapedCharge:
                case PenetrationMechanism.Tandem:
                    return PenetratorKind.Jet;
                case PenetrationMechanism.HighExplosive:
                case PenetrationMechanism.HighExplosiveSquashHead:
                case PenetrationMechanism.HighExplosiveFragmentation:
                    return PenetratorKind.Blast;
                case PenetrationMechanism.Shot:
                    return PenetratorKind.Shot;
                case PenetrationMechanism.Fragment:
                    return PenetratorKind.Fragment;
                default:
                    return PenetratorKind.Kinetic;
            }
        }
    }

    /// <summary>The attacking body as it changes layer by layer. SI units.</summary>
    public struct PenetratorState
    {
        public PenetratorKind Kind;
        public PenetratorCondition Condition;
        public float MassKg;
        /// <summary>Velocity in the frame the state is currently expressed in (world, or target local).</summary>
        public Vector3 Velocity;
        public float LengthM;
        public float DiameterM;
        /// <summary>Angle between body axis and velocity, radians (long-rod yaw).</summary>
        public float YawRadians;

        public float Speed => Velocity.Length();

        public Vector3 Direction
        {
            get
            {
                float speed = Speed;
                return speed > 0f ? Velocity / speed : Vector3.Zero;
            }
        }

        /// <summary>0.5 m v^2, joules. [physical]</summary>
        public float KineticEnergyJ => 0.5f * MassKg * Velocity.LengthSquared();

        public bool IsSpent => Condition == PenetratorCondition.Spent || MassKg <= 0f || Speed <= 0f;

        public static PenetratorState Projectile(PenetrationMechanism mechanism, float massKg, Vector3 velocity,
            float lengthM, float diameterM) =>
            new PenetratorState
            {
                Kind = PenetrationMechanisms.InitialKind(mechanism),
                Condition = PenetratorCondition.Intact,
                MassKg = MathF.Max(0f, massKg),
                Velocity = velocity,
                LengthM = MathF.Max(0f, lengthM),
                DiameterM = MathF.Max(0f, diameterM)
            };

        public PenetratorState InFrame(BallisticFrame frame)
        {
            PenetratorState local = this;
            local.Velocity = frame.ToLocalDirection(Velocity);
            return local;
        }

        public PenetratorState ToWorld(BallisticFrame frame)
        {
            PenetratorState world = this;
            world.Velocity = frame.ToWorldDirection(Velocity);
            return world;
        }
    }

    /// <summary>Immutable description of one hit. Created by an adapter from the game projectile.</summary>
    public readonly struct ImpactEvent
    {
        public readonly ulong Seed;
        public readonly int Tick;
        public readonly int AttackerId;
        public readonly int TargetId;
        public readonly string ProjectileDefName;
        public readonly PenetrationMechanism Mechanism;
        public readonly Vector3 WorldPoint;
        public readonly PenetratorState Penetrator;
        /// <summary>0 for the primary impact; each secondary generation adds one.</summary>
        public readonly int Depth;

        public ImpactEvent(ulong seed, int tick, int attackerId, int targetId, string projectileDefName,
            PenetrationMechanism mechanism, Vector3 worldPoint, PenetratorState penetrator, int depth = 0)
        {
            Seed = seed;
            Tick = tick;
            AttackerId = attackerId;
            TargetId = targetId;
            ProjectileDefName = projectileDefName ?? string.Empty;
            Mechanism = mechanism;
            WorldPoint = worldPoint;
            Penetrator = penetrator;
            Depth = depth;
        }

        /// <summary>Secondary impact (ricochet, fragment, crew hit) with a derived seed and depth + 1.</summary>
        public ImpactEvent Child(int childIndex, int targetId, PenetrationMechanism mechanism, Vector3 worldPoint,
            PenetratorState penetrator) =>
            new ImpactEvent(BallisticSeed.ForChild(Seed, childIndex, targetId), Tick, AttackerId, targetId,
                ProjectileDefName, mechanism, worldPoint, penetrator, Depth + 1);
    }

    /// <summary>Impact geometry in the target's local frame.</summary>
    public struct ImpactGeometry
    {
        public bool Valid;
        public Vector3 LocalPoint;
        public Vector3 LocalNormal;
        public Vector3 LocalTravel;
        public float Obliquity;

        public static ImpactGeometry From(Vector3 localPoint, Vector3 localNormal, Vector3 localTravel)
        {
            bool valid = BallisticGeometry.TryObliquity(localTravel, localNormal, out float obliquity);
            return new ImpactGeometry
            {
                Valid = valid,
                LocalPoint = localPoint,
                LocalNormal = localNormal,
                LocalTravel = localTravel,
                Obliquity = obliquity
            };
        }
    }

    /// <summary>One armor layer met along the shot line. Static layup data plus a link to dynamic damage.</summary>
    public struct ArmorLayer
    {
        public string MaterialId;
        public ArmorLayerRole Role;
        public float ThicknessM;
        /// <summary>Outward normal of this layer at the impact point, target local frame.</summary>
        public Vector3 Normal;
        /// <summary>Air gap to the next layer (spaced armor), metres.</summary>
        public float GapAfterM;
        /// <summary>Index into the owner's dynamic damage state, or -1.</summary>
        public int DamageStateIndex;
    }

    /// <summary>Ordered layers at an impact point, outermost first. Reused between impacts.</summary>
    public sealed class ArmorStack
    {
        public readonly List<ArmorLayer> Layers = new List<ArmorLayer>(8);
        public string OwnerId = string.Empty;

        public bool IsEmpty => Layers.Count == 0;

        public void Clear()
        {
            Layers.Clear();
            OwnerId = string.Empty;
        }
    }

    public struct SecondaryEvent
    {
        public SecondaryEventKind Kind;
        public Vector3 Origin;
        public Vector3 Direction;
        public float ConeHalfAngleRadians;
        public int Count;
        public float MassKgEach;
        public float SpeedMs;
        /// <summary>Blast: TNT-equivalent kg. Trauma: transferred energy J.</summary>
        public float Magnitude;
        public int GenerationDepth;
    }

    public struct LayerDamage
    {
        public int LayerIndex;
        public Vector3 LocalPoint;
        public float RadiusM;
        public float Severity;
        public bool Perforated;
    }

    public sealed class ImpactOutcome
    {
        public ImpactResult Result;
        public bool HasResidual;
        public PenetratorState Residual;
        public readonly List<SecondaryEvent> Secondaries = new List<SecondaryEvent>(4);
        public readonly List<LayerDamage> LayerDamage = new List<LayerDamage>(4);

        public void Clear()
        {
            Result = ImpactResult.None;
            HasResidual = false;
            Residual = default;
            Secondaries.Clear();
            LayerDamage.Clear();
        }
    }
}
