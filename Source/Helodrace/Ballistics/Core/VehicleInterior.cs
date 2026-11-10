using System;
using System.Collections.Generic;
using System.Numerics;

namespace Helodrace.Ballistics
{
    /// <summary>Interior equipment as an axis-aligned box in its frame, tied to a vehicle component.</summary>
    public sealed class InteriorBox
    {
        public string Id = string.Empty;
        public ArmorFrame Frame;
        public Vector3 Min, Max;
        /// <summary>Thin material the box presents along a shot line (index into the owner's layers), or -1.</summary>
        public int LayupIndex = -1;
        /// <summary>Vehicle component key that takes the damage.</summary>
        public string Component;
        /// <summary>Bar (slat) armour outside the shell (phase 11).</summary>
        public bool Slat;
    }

    /// <summary>A crew seat: a vertical capsule (cylinder) standing on its base point. [game geometry]</summary>
    public sealed class InteriorSeat
    {
        public string Role = string.Empty;
        public int Slot;
        public ArmorFrame Frame;
        public Vector3 Base;
        public float Radius = 0.25f;
        public float Height = 0.9f;
        /// <summary>Torso yaw relative to the frame, degrees (0 = facing the frame's forward).</summary>
        public float FacingYaw;
    }

    /// <summary>Crew/equipment space, for bookkeeping now and blast pressure later (design doc 7.3).</summary>
    public sealed class InteriorCompartment
    {
        public string Id = string.Empty;
        public ArmorFrame Frame;
        public Vector3 Min, Max;
        public float VolumeM3 => MathF.Max(0f, (Max.X - Min.X) * (Max.Y - Min.Y) * (Max.Z - Min.Z));
        public bool Contains(Vector3 local) =>
            local.X >= Min.X && local.X <= Max.X && local.Y >= Min.Y && local.Y <= Max.Y && local.Z >= Min.Z && local.Z <= Max.Z;
    }

    public enum InteriorHitKind
    {
        Equipment,
        Seat
    }

    public struct InteriorHit : IComparable<InteriorHit>
    {
        public InteriorHitKind Kind;
        public int Index;
        public float Distance;
        public float ExitDistance;
        /// <summary>Outward face normal at entry, in the hit object's frame (equipment only).</summary>
        public Vector3 LocalNormal;
        public Vector3 LocalTravel;

        public int CompareTo(InteriorHit other) => Distance.CompareTo(other.Distance);
    }

    /// <summary>Static interior of one vehicle (design doc 7.1/7.2). Same frames as the armour shell.</summary>
    public sealed class VehicleInterior
    {
        public readonly List<InteriorBox> Boxes = new List<InteriorBox>();
        public readonly List<InteriorSeat> Seats = new List<InteriorSeat>();
        public readonly List<InteriorCompartment> Compartments = new List<InteriorCompartment>();

        public bool IsEmpty => Boxes.Count == 0 && Seats.Count == 0;

        /// <summary>Every box and seat the world ray crosses within maxDistance, nearest first.</summary>
        public void Trace(Vector3 worldOrigin, Vector3 worldDirection, in BallisticFrame hull, in BallisticFrame turret,
            float maxDistance, List<InteriorHit> hits)
        {
            hits.Clear();
            Vector3 dir = Vector3.Normalize(worldDirection);
            for (int i = 0; i < Boxes.Count; i++)
            {
                InteriorBox box = Boxes[i];
                BallisticFrame frame = box.Frame == ArmorFrame.Turret ? turret : hull;
                Vector3 o = frame.ToLocalPoint(worldOrigin), d = frame.ToLocalDirection(dir);
                if (!InteriorGeometry.RayAabb(o, d, box.Min, box.Max, out float enter, out float exit, out Vector3 normal)) continue;
                if (exit < 0f || enter > maxDistance) continue;
                hits.Add(new InteriorHit
                {
                    Kind = InteriorHitKind.Equipment, Index = i, Distance = MathF.Max(0f, enter), ExitDistance = exit,
                    LocalNormal = normal, LocalTravel = d
                });
            }
            for (int i = 0; i < Seats.Count; i++)
            {
                InteriorSeat seat = Seats[i];
                BallisticFrame frame = seat.Frame == ArmorFrame.Turret ? turret : hull;
                Vector3 o = frame.ToLocalPoint(worldOrigin), d = frame.ToLocalDirection(dir);
                if (!InteriorGeometry.RayCylinder(o, d, seat.Base, seat.Radius, seat.Height, out float enter, out float exit)) continue;
                if (exit < 0f || enter > maxDistance) continue;
                hits.Add(new InteriorHit
                {
                    Kind = InteriorHitKind.Seat, Index = i, Distance = MathF.Max(0f, enter), ExitDistance = exit, LocalTravel = d
                });
            }
            hits.Sort();
        }

        public int CompartmentAt(Vector3 worldPoint, in BallisticFrame hull, in BallisticFrame turret)
        {
            for (int i = 0; i < Compartments.Count; i++)
            {
                InteriorCompartment c = Compartments[i];
                BallisticFrame frame = c.Frame == ArmorFrame.Turret ? turret : hull;
                if (c.Contains(frame.ToLocalPoint(worldPoint))) return i;
            }
            return -1;
        }
    }

    public static class InteriorGeometry
    {
        private const float Epsilon = 1e-7f;

        /// <summary>Slab test. normal is the outward face normal of the entry face. [physical]</summary>
        public static bool RayAabb(Vector3 origin, Vector3 dir, Vector3 min, Vector3 max, out float enter, out float exit, out Vector3 normal)
        {
            enter = float.NegativeInfinity;
            exit = float.PositiveInfinity;
            normal = Vector3.Zero;
            for (int axis = 0; axis < 3; axis++)
            {
                float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
                float d = axis == 0 ? dir.X : axis == 1 ? dir.Y : dir.Z;
                float lo = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
                float hi = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
                if (MathF.Abs(d) < Epsilon)
                {
                    if (o < lo || o > hi) return false;
                    continue;
                }
                float t1 = (lo - o) / d, t2 = (hi - o) / d;
                float sign = -1f;
                if (t1 > t2) { (t1, t2) = (t2, t1); sign = 1f; }
                if (t1 > enter)
                {
                    enter = t1;
                    normal = axis == 0 ? new Vector3(sign, 0, 0) : axis == 1 ? new Vector3(0, sign, 0) : new Vector3(0, 0, sign);
                }
                exit = MathF.Min(exit, t2);
                if (enter > exit) return false;
            }
            return exit >= 0f;
        }

        /// <summary>Ray against a vertical cylinder standing on basePoint (y up). [physical]</summary>
        public static bool RayCylinder(Vector3 origin, Vector3 dir, Vector3 basePoint, float radius, float height, out float enter, out float exit)
        {
            enter = float.NegativeInfinity;
            exit = float.PositiveInfinity;
            float ox = origin.X - basePoint.X, oz = origin.Z - basePoint.Z;
            float a = dir.X * dir.X + dir.Z * dir.Z;
            if (a < Epsilon)
            {
                if (ox * ox + oz * oz > radius * radius) return false;
            }
            else
            {
                float b = 2f * (ox * dir.X + oz * dir.Z);
                float c = ox * ox + oz * oz - radius * radius;
                float disc = b * b - 4f * a * c;
                if (disc < 0f) return false;
                float root = MathF.Sqrt(disc);
                enter = (-b - root) / (2f * a);
                exit = (-b + root) / (2f * a);
            }
            float oy = origin.Y - basePoint.Y;
            if (MathF.Abs(dir.Y) < Epsilon)
            {
                if (oy < 0f || oy > height) return false;
            }
            else
            {
                float t1 = -oy / dir.Y, t2 = (height - oy) / dir.Y;
                if (t1 > t2) (t1, t2) = (t2, t1);
                enter = MathF.Max(enter, t1);
                exit = MathF.Min(exit, t2);
            }
            return enter <= exit && exit >= 0f;
        }

        /// <summary>Side-on projected area of a seat cylinder for a ray. [physical approximation]</summary>
        public static float ProjectedArea(InteriorSeat seat, Vector3 localDir)
        {
            float horizontal = MathF.Sqrt(localDir.X * localDir.X + localDir.Z * localDir.Z);
            float side = 2f * seat.Radius * seat.Height * horizontal;
            float top = MathF.PI * seat.Radius * seat.Radius * MathF.Abs(localDir.Y);
            return side + top;
        }
    }

    /// <summary>
    /// Behind-armour spall as a few representative rays, each carrying a bundle of fragments (design doc 7.2).
    /// Counts, masses, speeds and cone angles are game values [game]; the per-ray expected hits on a crew member is
    /// the bundle count times the target's projected area over the ray's footprint at that range [physical approximation].
    /// </summary>
    public sealed class SpallProfile
    {
        public float FragmentsPerRhaMillimeter = 0.6f;
        public int MinFragments = 4;
        public int MaxFragments = 200;
        public float FragmentMassKg = 0.003f;
        public float SpeedShare = 0.5f;
        public float MaxSpeedMs = 1500f;
        public float ConeHalfAngleDegrees = 35f;
        public int Rays = 16;

        public static readonly SpallProfile Kinetic = new SpallProfile();
        public static readonly SpallProfile Jet = new SpallProfile
        {
            FragmentsPerRhaMillimeter = 0.4f, SpeedShare = 0f, MaxSpeedMs = 1200f, ConeHalfAngleDegrees = 25f
        };

        public int Count(float perforatedRhaMm) =>
            Math.Clamp((int)MathF.Round(FragmentsPerRhaMillimeter * perforatedRhaMm), MinFragments, MaxFragments);

        public float Speed(float residualSpeed) => SpeedShare <= 0f ? MaxSpeedMs : MathF.Min(MaxSpeedMs, SpeedShare * residualSpeed);

        /// <summary>Solid angle of one ray's share of the cone, steradians.</summary>
        public float RaySolidAngle => 2f * MathF.PI * (1f - MathF.Cos(ConeHalfAngleDegrees * BallisticGeometry.DegToRad)) / MathF.Max(1, Rays);

        /// <summary>Steel fragment presented diameter from its mass (sphere of steel). [physical]</summary>
        public float FragmentDiameterM => MathF.Cbrt(6f * FragmentMassKg / (MathF.PI * VehicleKineticModel.RhaDensity));

        /// <summary>Ray directions spread evenly over the cone (golden-angle spiral, rotated by the event seed).</summary>
        public void Directions(Vector3 axis, ulong seed, List<Vector3> into)
        {
            into.Clear();
            Vector3 a = Vector3.Normalize(axis);
            BallisticGeometry.BuildBasis(a, out Vector3 t, out Vector3 b);
            var rng = BallisticSeed.Stream(seed, "spall");
            float spin = rng.NextFloat() * 2f * MathF.PI;
            float cosMax = MathF.Cos(ConeHalfAngleDegrees * BallisticGeometry.DegToRad);
            for (int i = 0; i < Rays; i++)
            {
                float u = (i + 0.5f) / Rays;
                float cos = 1f - u * (1f - cosMax);
                float sin = MathF.Sqrt(MathF.Max(0f, 1f - cos * cos));
                float phi = spin + i * 2.39996323f;
                into.Add(Vector3.Normalize(a * cos + (t * MathF.Cos(phi) + b * MathF.Sin(phi)) * sin));
            }
        }

        /// <summary>Fragments of a bundle of n that strike a target of the given area at range r. [physical approximation + binomial]</summary>
        public int Hits(int bundle, float area, float range, ref BallisticRng rng)
        {
            if (bundle <= 0 || area <= 0f) return 0;
            float footprint = RaySolidAngle * MathF.Max(0.05f, range) * MathF.Max(0.05f, range);
            return rng.Binomial(bundle, MathF.Min(1f, area / footprint));
        }

        /// <summary>Wound value of one fragment, anchored to a rifle round (11 damage at 1,700 J). [game]</summary>
        public static float FragmentDamage(float massKg, float speed)
        {
            float energy = 0.5f * massKg * speed * speed;
            return Math.Clamp(11f * MathF.Sqrt(energy / 1700f), 1f, 30f);
        }
    }
}

namespace Helodrace.Ballistics
{
    public struct SpallSeatHit
    {
        public int Seat;
        public int Fragments;
        public float Speed;
        public Vector3 WorldDirection;
        public float Range;
    }

    public struct SpallBoxHit
    {
        public int Box;
        public int Fragments;
        public float Speed;
    }

    /// <summary>
    /// Traces a spall cone through an interior as bundle rays (design doc 7.2). Equipment shields what lies behind it:
    /// fragments meet the box's thin layers with the small-arms model and either stop or slow down. Pure: the
    /// vehicle adapter turns the result into component damage and per-crew wounds.
    /// </summary>
    public static class SpallTracer
    {
        public static void Trace(VehicleInterior interior, SpallProfile profile, Vector3 origin, Vector3 axis, int fragments,
            float speed, in BallisticFrame hull, in BallisticFrame turret, Func<int, IReadOnlyList<VehicleArmorLayer>> boxLayers,
            ulong seed, List<SpallSeatHit> seats, List<SpallBoxHit> boxes, float maxRange = 8f)
        {
            seats.Clear();
            boxes.Clear();
            if (interior == null || fragments <= 0) return;
            var directions = new List<Vector3>(profile.Rays);
            profile.Directions(axis, seed, directions);
            var hits = new List<InteriorHit>();
            var rng = BallisticSeed.Stream(seed, "spallHits");
            var round = new VehicleKineticRound { Mechanism = PenetrationMechanism.KineticBullet, SmallArmsClass = PenetratorClass.MildSteel };
            var scratch = new VehicleKineticResult();
            float mass = profile.FragmentMassKg, diameter = profile.FragmentDiameterM;
            for (int r = 0; r < directions.Count; r++)
            {
                int bundle = fragments / directions.Count + (r < fragments % directions.Count ? 1 : 0);
                float bundleSpeed = speed;
                interior.Trace(origin, directions[r], hull, turret, maxRange, hits);
                for (int i = 0; i < hits.Count && bundle > 0; i++)
                {
                    InteriorHit hit = hits[i];
                    if (hit.Kind == InteriorHitKind.Equipment)
                    {
                        boxes.Add(new SpallBoxHit { Box = hit.Index, Fragments = bundle, Speed = bundleSpeed });
                        var fragment = PenetratorState.Projectile(PenetrationMechanism.Fragment, mass,
                            Vector3.Normalize(hit.LocalTravel) * bundleSpeed, 0f, diameter);
                        VehicleKineticModel.Resolve(round, fragment, boxLayers(hit.Index), hit.LocalNormal,
                            BallisticSeed.ForChild(seed, 1000 + r * 64 + i, 0), scratch);
                        if (!scratch.Perforated) bundle = 0;
                        else bundleSpeed = scratch.Out.Speed;
                        continue;
                    }
                    InteriorSeat seat = interior.Seats[hit.Index];
                    int struck = profile.Hits(bundle, InteriorGeometry.ProjectedArea(seat, hit.LocalTravel), hit.Distance, ref rng);
                    if (struck <= 0) continue;
                    bundle -= struck;
                    seats.Add(new SpallSeatHit { Seat = hit.Index, Fragments = struck, Speed = bundleSpeed, WorldDirection = directions[r], Range = hit.Distance });
                }
            }
        }
    }
}
