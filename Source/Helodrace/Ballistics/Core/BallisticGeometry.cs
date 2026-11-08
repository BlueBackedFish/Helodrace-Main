using System;
using System.Collections.Generic;
using System.Numerics;

namespace Helodrace.Ballistics
{
    // Coordinate convention shared by every ballistic calculation:
    //   world: +X east, +Y up, +Z north (RimWorld x/z, altitude in Y), metres.
    //   yaw:   degrees, 0 = north, clockwise (same as CompHullHeading).
    //   local: +Z forward, +X right, +Y up.
    // No Unity/RimWorld types: this file also runs in the regression executable.

    /// <summary>A yaw-only rigid frame (pawn torso, head, vehicle hull, turret).</summary>
    public readonly struct BallisticFrame
    {
        public readonly Vector3 Origin;
        public readonly float YawDegrees;
        public readonly Vector3 Forward;
        public readonly Vector3 Right;

        public BallisticFrame(Vector3 origin, float yawDegrees)
        {
            Origin = origin;
            YawDegrees = BallisticGeometry.NormalizeDegrees(yawDegrees);
            float radians = YawDegrees * BallisticGeometry.DegToRad;
            float sin = MathF.Sin(radians);
            float cos = MathF.Cos(radians);
            Forward = new Vector3(sin, 0f, cos);
            Right = new Vector3(cos, 0f, -sin);
        }

        public static BallisticFrame World => new BallisticFrame(Vector3.Zero, 0f);

        public Vector3 ToWorldDirection(Vector3 local) =>
            local.X * Right + local.Y * Vector3.UnitY + local.Z * Forward;

        public Vector3 ToWorldPoint(Vector3 local) => Origin + ToWorldDirection(local);

        public Vector3 ToLocalDirection(Vector3 world) =>
            new Vector3(Vector3.Dot(world, Right), world.Y, Vector3.Dot(world, Forward));

        public Vector3 ToLocalPoint(Vector3 world) => ToLocalDirection(world - Origin);

        /// <summary>A child frame (turret on hull) given in this frame's local coordinates.</summary>
        public BallisticFrame Child(Vector3 localOrigin, float relativeYawDegrees) =>
            new BallisticFrame(ToWorldPoint(localOrigin), YawDegrees + relativeYawDegrees);
    }

    public readonly struct SurfaceImpact
    {
        public readonly Vector3 Point;
        public readonly Vector3 OutwardNormal;
        /// <summary>Angle between the reversed travel direction and the outward normal, radians [0, pi/2].</summary>
        public readonly float Obliquity;

        public SurfaceImpact(Vector3 point, Vector3 outwardNormal, float obliquity)
        {
            Point = point;
            OutwardNormal = outwardNormal;
            Obliquity = obliquity;
        }
    }

    public static class BallisticGeometry
    {
        public const float DegToRad = MathF.PI / 180f;
        public const float RadToDeg = 180f / MathF.PI;
        /// <summary>LOS growth is capped near grazing incidence (cos 89.5 deg).</summary>
        public const float MinimumCosine = 0.0087265f;
        private const float Epsilon = 1e-6f;

        public static float NormalizeDegrees(float degrees)
        {
            float value = degrees % 360f;
            return value < 0f ? value + 360f : value;
        }

        public static float DeltaDegrees(float from, float to) =>
            NormalizeDegrees(to - from + 180f) - 180f;

        /// <summary>Unit direction of travel from a map heading and a descent angle (positive = downward).</summary>
        public static Vector3 TravelDirection(float headingDegrees, float descentDegrees)
        {
            float heading = headingDegrees * DegToRad;
            float descent = descentDegrees * DegToRad;
            float horizontal = MathF.Cos(descent);
            return new Vector3(MathF.Sin(heading) * horizontal, -MathF.Sin(descent), MathF.Cos(heading) * horizontal);
        }

        /// <summary>Flat-fire descent angle from shooter muzzle height to impact height. [physical]</summary>
        public static float DescentDegrees(float muzzleHeight, float impactHeight, float flatDistance)
        {
            if (flatDistance <= Epsilon)
            {
                return muzzleHeight >= impactHeight ? 90f : -90f;
            }
            return MathF.Atan2(muzzleHeight - impactHeight, flatDistance) * RadToDeg;
        }

        /// <summary>
        /// Obliquity of a travel direction against an outward surface normal.
        /// False when the projectile moves away from (or along) the surface.
        /// </summary>
        public static bool TryObliquity(Vector3 travel, Vector3 outwardNormal, out float obliquity)
        {
            obliquity = 0f;
            float travelLength = travel.Length();
            float normalLength = outwardNormal.Length();
            if (travelLength <= Epsilon || normalLength <= Epsilon)
            {
                return false;
            }
            float cosine = -Vector3.Dot(travel, outwardNormal) / (travelLength * normalLength);
            if (cosine <= Epsilon)
            {
                return false;
            }
            obliquity = MathF.Acos(MathF.Min(1f, cosine));
            return true;
        }

        /// <summary>Geometric line-of-sight thickness t / cos(theta). [physical] Not the protective value.</summary>
        public static float LineOfSightThickness(float thickness, float obliquity) =>
            thickness / MathF.Max(MinimumCosine, MathF.Cos(obliquity));

        /// <summary>
        /// Outward normal of a plate from its local facing yaw (0 = forward) and its slope from vertical
        /// (0 = vertical wall, +90 = roof facing up, negative = belly facing down).
        /// </summary>
        public static Vector3 PlateNormal(float facingYawDegrees, float slopeFromVerticalDegrees)
        {
            float yaw = facingYawDegrees * DegToRad;
            float slope = slopeFromVerticalDegrees * DegToRad;
            float horizontal = MathF.Cos(slope);
            return new Vector3(MathF.Sin(yaw) * horizontal, MathF.Sin(slope), MathF.Cos(yaw) * horizontal);
        }

        /// <summary>Mirror reflection of a direction about a surface normal.</summary>
        public static Vector3 Reflect(Vector3 direction, Vector3 normal)
        {
            Vector3 n = Vector3.Normalize(normal);
            return direction - 2f * Vector3.Dot(direction, n) * n;
        }

        public static bool RayPlane(Vector3 origin, Vector3 direction, Vector3 planePoint, Vector3 planeNormal, out float distance)
        {
            distance = 0f;
            float denominator = Vector3.Dot(direction, planeNormal);
            if (MathF.Abs(denominator) <= Epsilon)
            {
                return false;
            }
            distance = Vector3.Dot(planePoint - origin, planeNormal) / denominator;
            return distance >= 0f;
        }

        /// <summary>Newell normal of a planar polygon; outward when vertices wind counter-clockwise seen from outside.</summary>
        public static Vector3 PolygonNormal(IReadOnlyList<Vector3> vertices)
        {
            Vector3 normal = Vector3.Zero;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 a = vertices[i];
                Vector3 b = vertices[(i + 1) % vertices.Count];
                normal.X += (a.Y - b.Y) * (a.Z + b.Z);
                normal.Y += (a.Z - b.Z) * (a.X + b.X);
                normal.Z += (a.X - b.X) * (a.Y + b.Y);
            }
            float length = normal.Length();
            return length <= Epsilon ? Vector3.Zero : normal / length;
        }

        /// <summary>Ray against a planar convex polygon. Hits from either side; caller checks facing with the normal.</summary>
        public static bool RayConvexPolygon(Vector3 origin, Vector3 direction, IReadOnlyList<Vector3> vertices,
            out float distance, out Vector3 normal)
        {
            distance = 0f;
            normal = Vector3.Zero;
            if (vertices == null || vertices.Count < 3)
            {
                return false;
            }
            normal = PolygonNormal(vertices);
            if (normal == Vector3.Zero || !RayPlane(origin, direction, vertices[0], normal, out distance))
            {
                return false;
            }
            Vector3 point = origin + direction * distance;
            float sign = 0f;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 a = vertices[i];
                Vector3 b = vertices[(i + 1) % vertices.Count];
                float side = Vector3.Dot(Vector3.Cross(b - a, point - a), normal);
                if (MathF.Abs(side) <= Epsilon)
                {
                    continue;
                }
                if (sign == 0f)
                {
                    sign = MathF.Sign(side);
                }
                else if (MathF.Sign(side) != sign)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Impact on an axis-aligned ellipsoid (in its own local frame) by a parallel beam, sampled uniformly over
        /// the beam cross-section. [physical] For a sphere this yields p(theta) = 2 sin(theta) cos(theta).
        /// The affine map to the unit sphere keeps rays parallel and flux uniform, so sampling the unit-sphere
        /// silhouette disk is exact for any ellipsoid.
        /// </summary>
        public static SurfaceImpact SampleEllipsoidImpact(Vector3 center, Vector3 radii, Vector3 travel, float u, float v)
        {
            Vector3 scaled = Vector3.Normalize(travel / radii);
            BuildBasis(scaled, out Vector3 tangent, out Vector3 bitangent);
            float radius = MathF.Sqrt(Math.Clamp(u, 0f, 1f));
            float angle = 2f * MathF.PI * v;
            Vector3 onDisk = radius * (MathF.Cos(angle) * tangent + MathF.Sin(angle) * bitangent);
            Vector3 unitHit = onDisk - MathF.Sqrt(MathF.Max(0f, 1f - radius * radius)) * scaled;
            Vector3 point = center + unitHit * radii;
            Vector3 normal = Vector3.Normalize(unitHit / radii);
            TryObliquity(travel, normal, out float obliquity);
            return new SurfaceImpact(point, normal, obliquity);
        }

        /// <summary>Orthonormal pair perpendicular to a unit vector, deterministic for a given input.</summary>
        public static void BuildBasis(Vector3 unit, out Vector3 tangent, out Vector3 bitangent)
        {
            Vector3 helper = MathF.Abs(unit.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            tangent = Vector3.Normalize(Vector3.Cross(helper, unit));
            bitangent = Vector3.Cross(unit, tangent);
        }
    }
}
