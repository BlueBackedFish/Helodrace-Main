using System;
using System.Collections.Generic;
using System.Numerics;

namespace Helodrace.Ballistics
{
    public enum ArmorFrame
    {
        Hull,
        Turret
    }

    /// <summary>One planar convex armour plate in its frame's local coordinates (+Z forward, +X right, +Y up).</summary>
    public sealed class ArmorPlate
    {
        public readonly string Id;
        public readonly ArmorFrame Frame;
        public readonly Vector3[] Vertices;
        /// <summary>Outward normal from the vertex winding (counter-clockwise seen from outside). [physical]</summary>
        public readonly Vector3 Normal;
        public readonly int LayupIndex;
        /// <summary>Opening key (ramp, roof hatch ...). While open the plate is a hole: thickness 0.</summary>
        public readonly string Aperture;

        public ArmorPlate(string id, ArmorFrame frame, Vector3[] vertices, int layupIndex, string aperture = null)
        {
            Id = id ?? string.Empty;
            Frame = frame;
            Vertices = vertices ?? Array.Empty<Vector3>();
            Normal = Vertices.Length >= 3 ? BallisticGeometry.PolygonNormal(Vertices) : Vector3.Zero;
            LayupIndex = layupIndex;
            Aperture = string.IsNullOrEmpty(aperture) ? null : aperture;
        }

        public Vector3 Centroid
        {
            get
            {
                Vector3 sum = Vector3.Zero;
                foreach (Vector3 vertex in Vertices) sum += vertex;
                return Vertices.Length == 0 ? Vector3.Zero : sum / Vertices.Length;
            }
        }

        /// <summary>Plate slope from vertical in degrees: 0 = vertical wall, 90 = flat roof. [physical]</summary>
        public float SlopeFromVerticalDegrees => MathF.Asin(Math.Clamp(Normal.Y, -1f, 1f)) * BallisticGeometry.RadToDeg;
    }

    public struct ShellHit
    {
        public int PlateIndex;
        public ArmorPlate Plate;
        public float Distance;
        public Vector3 WorldPoint;
        /// <summary>Point, normal and travel in the struck plate's frame.</summary>
        public Vector3 LocalPoint;
        public Vector3 LocalNormal;
        public Vector3 LocalTravel;
        public float Obliquity;
        public bool Open;
    }

    /// <summary>
    /// Static low-poly armour shell of one vehicle (design doc 7.1). Geometry stays in local coordinates; an
    /// impact transforms the shot line into each frame instead of rotating the plates.
    /// </summary>
    public sealed class VehicleArmorShell
    {
        private const float Epsilon = 1e-5f;

        public readonly List<ArmorPlate> Plates = new List<ArmorPlate>();
        /// <summary>Turret ring centre in hull coordinates.</summary>
        public Vector3 TurretPivot;

        public bool HasTurret
        {
            get
            {
                foreach (ArmorPlate plate in Plates)
                    if (plate.Frame == ArmorFrame.Turret) return true;
                return false;
            }
        }

        public BallisticFrame TurretFrame(in BallisticFrame hull, float turretRelativeYaw) =>
            hull.Child(TurretPivot, turretRelativeYaw);

        /// <summary>Axis-aligned hull-frame bounds of every plate, turret plates at zero relative yaw.</summary>
        public void Bounds(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            foreach (ArmorPlate plate in Plates)
            {
                Vector3 offset = plate.Frame == ArmorFrame.Turret ? TurretPivot : Vector3.Zero;
                foreach (Vector3 vertex in plate.Vertices)
                {
                    min = Vector3.Min(min, vertex + offset);
                    max = Vector3.Max(max, vertex + offset);
                }
            }
            if (Plates.Count == 0) min = max = Vector3.Zero;
        }

        /// <summary>
        /// First outward-facing plate along a world ray. Turret plates are tested in the turret frame, so the turret
        /// can shadow hull plates and vice versa.
        /// </summary>
        public bool Raycast(Vector3 worldOrigin, Vector3 worldDirection, in BallisticFrame hull, in BallisticFrame turret,
            Func<string, bool> apertureOpen, out ShellHit hit)
        {
            hit = default;
            hit.PlateIndex = -1;
            hit.Distance = float.MaxValue;
            Vector3 direction = Vector3.Normalize(worldDirection);
            Vector3 hullOrigin = hull.ToLocalPoint(worldOrigin);
            Vector3 hullDirection = hull.ToLocalDirection(direction);
            Vector3 turretOrigin = turret.ToLocalPoint(worldOrigin);
            Vector3 turretDirection = turret.ToLocalDirection(direction);
            for (int i = 0; i < Plates.Count; i++)
            {
                ArmorPlate plate = Plates[i];
                bool onTurret = plate.Frame == ArmorFrame.Turret;
                Vector3 origin = onTurret ? turretOrigin : hullOrigin;
                Vector3 localDirection = onTurret ? turretDirection : hullDirection;
                if (Vector3.Dot(localDirection, plate.Normal) >= 0f) continue;
                if (!BallisticGeometry.RayConvexPolygon(origin, localDirection, plate.Vertices, out float distance, out _)) continue;
                if (distance >= hit.Distance) continue;
                hit.PlateIndex = i;
                hit.Plate = plate;
                hit.Distance = distance;
                hit.LocalPoint = origin + localDirection * distance;
                hit.LocalNormal = plate.Normal;
                hit.LocalTravel = localDirection;
            }
            if (hit.PlateIndex < 0) return false;
            hit.WorldPoint = worldOrigin + direction * hit.Distance;
            BallisticGeometry.TryObliquity(hit.LocalTravel, hit.LocalNormal, out hit.Obliquity);
            hit.Open = hit.Plate.Aperture != null && apertureOpen != null && apertureOpen(hit.Plate.Aperture);
            return true;
        }

        /// <summary>Authoring checks: at least three vertices, planar, convex, normal pointing away from the frame centre.</summary>
        public IEnumerable<string> Validate(string owner)
        {
            Vector3 hullCentre = Vector3.Zero, turretCentre = Vector3.Zero;
            int hullCount = 0, turretCount = 0;
            foreach (ArmorPlate plate in Plates)
            {
                if (plate.Frame == ArmorFrame.Turret) { turretCentre += plate.Centroid; turretCount++; }
                else { hullCentre += plate.Centroid; hullCount++; }
            }
            if (hullCount > 0) hullCentre /= hullCount;
            if (turretCount > 0) turretCentre /= turretCount;
            var ids = new HashSet<string>();
            foreach (ArmorPlate plate in Plates)
            {
                string name = owner + "/" + plate.Id;
                if (!ids.Add(plate.Id)) yield return name + ": duplicate plate id";
                if (plate.Vertices.Length < 3 || plate.Normal == Vector3.Zero)
                {
                    yield return name + ": needs three non-collinear vertices";
                    continue;
                }
                foreach (Vector3 vertex in plate.Vertices)
                {
                    if (MathF.Abs(Vector3.Dot(vertex - plate.Vertices[0], plate.Normal)) > 0.01f)
                    {
                        yield return name + ": vertices are not planar";
                        break;
                    }
                }
                if (!Convex(plate)) yield return name + ": polygon is not convex or winds clockwise";
                Vector3 centre = plate.Frame == ArmorFrame.Turret ? turretCentre : hullCentre;
                if (Vector3.Dot(plate.Centroid - centre, plate.Normal) <= 0f)
                    yield return name + ": normal points inward (wind vertices counter-clockwise seen from outside)";
                if (plate.LayupIndex < 0) yield return name + ": no layup";
            }
        }

        private static bool Convex(ArmorPlate plate)
        {
            Vector3[] v = plate.Vertices;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 a = v[i], b = v[(i + 1) % v.Length], c = v[(i + 2) % v.Length];
                if (Vector3.Dot(Vector3.Cross(b - a, c - b), plate.Normal) < -Epsilon) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Builds the shot line through a vehicle from a 2D game impact (design doc 7.1). The game knows only the
    /// map position and heading, so the strike point across the silhouette is sampled deterministically: lateral
    /// offset = the game point's offset across the line of fire plus scatter, height uniform over the shell.
    /// Descent comes from the round's flight data. [game sampling, physical geometry]
    /// </summary>
    public static class VehicleShotLine
    {
        /// <summary>Lateral aim scatter around the game impact point, metres. [game]</summary>
        public const float LateralScatter = 0.45f;
        public const int MaxSamples = 12;

        public static bool TryStrike(VehicleArmorShell shell, in BallisticFrame hull, float turretRelativeYaw,
            Vector3 gameImpact, float headingDegrees, float descentDegrees, ulong seed, Func<string, bool> apertureOpen,
            out ShellHit hit, out Vector3 travel)
        {
            travel = BallisticGeometry.TravelDirection(headingDegrees, descentDegrees);
            BallisticFrame turret = shell.TurretFrame(hull, turretRelativeYaw);
            shell.Bounds(out Vector3 min, out Vector3 max);
            float radius = MathF.Max(max.Length(), min.Length()) + 2f;
            Vector3 flat = new Vector3(travel.X, 0f, travel.Z);
            Vector3 across = flat.LengthSquared() > Epsilon2 ? Vector3.Normalize(new Vector3(flat.Z, 0f, -flat.X)) : hull.Right;
            Vector3 centre = hull.Origin;
            Vector3 offset = gameImpact - centre;
            float lateral = Vector3.Dot(new Vector3(offset.X, 0f, offset.Z), across);
            float halfWidth = MathF.Max(MathF.Abs(min.X), MathF.Abs(max.X)) + 0.25f;
            lateral = Math.Clamp(lateral, -halfWidth, halfWidth);
            var rng = BallisticSeed.Stream(seed, "shotLine");
            for (int attempt = 0; attempt < MaxSamples; attempt++)
            {
                float side = lateral + rng.NextGaussian() * LateralScatter;
                float height = rng.Range(MathF.Max(0f, min.Y), max.Y);
                // Aim point on the plane through the hull origin, perpendicular to the line of fire.
                Vector3 aim = centre + across * side + Vector3.UnitY * height;
                Vector3 origin = aim - travel * radius;
                if (shell.Raycast(origin, travel, hull, turret, apertureOpen, out hit)) return true;
            }
            hit = default;
            hit.PlateIndex = -1;
            return false;
        }

        private const float Epsilon2 = 1e-8f;
    }
}
