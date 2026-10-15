using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Verse;

namespace Helodrace
{
    internal sealed class ShotgunSurface
    {
        internal BodyPartRecord part;
        internal float area;
        internal ShotgunSurface parent;
        internal readonly List<BodyPartRecord> internals = new List<BodyPartRecord>();
        internal readonly Dictionary<ShotgunSurface, float> distances = new Dictionary<ShotgunSurface, float>();
    }

    internal sealed class ShotgunBodyProfile
    {
        private static readonly ConditionalWeakTable<BodyDef, ShotgunBodyProfile> cache = new ConditionalWeakTable<BodyDef, ShotgunBodyProfile>();
        internal readonly List<ShotgunSurface> surfaces = new List<ShotgunSurface>();
        private readonly Dictionary<BodyPartRecord, ShotgunSurface> byPart = new Dictionary<BodyPartRecord, ShotgunSurface>();
        internal static ShotgunBodyProfile For(BodyDef body) => cache.GetValue(body, value => new ShotgunBodyProfile(value));

        internal ShotgunBodyProfile(BodyDef body)
        {
            foreach (BodyPartRecord part in body.AllParts)
                if (part.depth == BodyPartDepth.Outside && !part.def.conceptual && part.coverageAbs > 0f && !float.IsInfinity(part.coverageAbs))
                {
                    var surface = new ShotgunSurface { part = part };
                    surfaces.Add(surface);
                    byPart.Add(part, surface);
                }
            // Return each internal residual area to its surface owner once.
            // Adding an organ must not enlarge the silhouette.
            foreach (BodyPartRecord part in body.AllParts)
            {
                if (!(part.coverageAbs > 0f) || float.IsInfinity(part.coverageAbs)) continue;
                ShotgunSurface owner = Owner(part);
                if (owner == null) continue;
                owner.area += part.coverageAbs;
                if (part.depth == BodyPartDepth.Inside && !part.def.conceptual) owner.internals.Add(part);
            }
            float total = surfaces.Sum(surface => surface.area);
            foreach (ShotgunSurface surface in surfaces)
            {
                if (total > 0f) surface.area /= total;
                surface.parent = Owner(surface.part.parent);
            }
        }

        internal ShotgunSurface Owner(BodyPartRecord part)
        {
            for (BodyPartRecord node = part; node != null; node = node.parent)
                if (byPart.TryGetValue(node, out ShotgunSurface owner)) return owner;
            return null;
        }

        internal ShotgunSurface Centre(Pawn pawn, DamageDef damage)
        {
            var available = surfaces.Where(surface => ShotgunBallistics.IsPresent(pawn, surface.part)).ToList();
            if (available.TryRandomElementByWeight(surface => surface.area * surface.part.def.GetHitChanceFactorFor(damage), out ShotgunSurface chosen)) return chosen;
            available.TryRandomElementByWeight(surface => surface.area, out chosen);
            return chosen;
        }

        internal static float Distance(ShotgunSurface a, ShotgunSurface b)
        {
            if (a == b) return 0f;
            if (a.distances.TryGetValue(b, out float cached)) return cached;
            float result = CalculateDistance(a, b);
            a.distances[b] = result;
            b.distances[a] = result;
            return result;
        }

        private static float CalculateDistance(ShotgunSurface a, ShotgunSurface b)
        {
            var ancestors = new Dictionary<ShotgunSurface, float>();
            float distance = 0f;
            for (ShotgunSurface node = a; node != null; node = node.parent)
            {
                ancestors[node] = distance;
                distance += Edge(node);
            }
            distance = 0f;
            for (ShotgunSurface node = b; node != null; node = node.parent)
            {
                if (ancestors.TryGetValue(node, out float other))
                    return distance + other + 0.15f * Math.Abs((int)a.part.height - (int)b.part.height);
                distance += Edge(node);
            }
            return 2f;
        }

        private static float Edge(ShotgunSurface surface) => 0.12f + 0.45f * (float)Math.Sqrt(surface.area);

        internal BodyPartRecord ChooseSurface(Pawn pawn, BodyPartRecord centrePart, DamageInfo damage, float relativeSpread)
        {
            ShotgunSurface centre = Owner(centrePart);
            if (centre == null) return null;
            float sigma = Math.Max(0.025f, relativeSpread * 0.45f);
            if (sigma <= 0.03f)
                return ShotgunBallistics.IsPresent(pawn, centre.part) ? centre.part : null;
            var available = surfaces.Where(surface => ShotgunBallistics.IsPresent(pawn, surface.part)
                && Distance(centre, surface) <= Math.Max(0.04f, relativeSpread * 1.2f)).ToList();
            float Weight(ShotgunSurface surface)
            {
                float distance = Distance(centre, surface);
                return surface.area * (float)Math.Exp(-distance * distance / (2f * sigma * sigma));
            }
            if (available.TryRandomElementByWeight(surface => Weight(surface) * surface.part.def.GetHitChanceFactorFor(damage.Def), out ShotgunSurface chosen)) return chosen.part;
            return available.TryRandomElementByWeight(Weight, out chosen) ? chosen.part : null;
        }

        internal BodyPartRecord ChooseInternal(Pawn pawn, BodyPartRecord entry, DamageDef damage)
        {
            ShotgunSurface owner = Owner(entry);
            if (owner == null) return null;
            owner.internals.Where(part => ShotgunBallistics.IsPresent(pawn, part)
                    && !pawn.health.hediffSet.AncestorHasDirectlyAddedParts(part))
                .TryRandomElementByWeight(part => part.coverageAbs * part.def.GetHitChanceFactorFor(damage), out BodyPartRecord chosen);
            return chosen;
        }

        internal static List<BodyPartRecord> InternalPath(BodyPartRecord entry, BodyPartRecord organ)
        {
            var path = new List<BodyPartRecord>();
            BodyPartRecord node = organ;
            for (; node != null && node != entry; node = node.parent)
            {
                if (node.depth == BodyPartDepth.Outside && node.coverageAbs > 0f && !node.def.conceptual)
                    return new List<BodyPartRecord>();
                if (node.depth == BodyPartDepth.Inside && node.coverageAbs > 0f && !node.def.conceptual) path.Add(node);
            }
            if (node != entry) return new List<BodyPartRecord>();
            path.Reverse();
            return path;
        }
    }
}
