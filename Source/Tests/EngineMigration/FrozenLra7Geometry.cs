// Frozen feedback geometry from d5945ed38a10f366ec0eec5f0e1cf1e06ec9d6f5.
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Helodrace.Tests.Frozen
{
    public static class LanceLra7FeedbackUtility
    {
        public static IEnumerable<IntVec3> AffectedCells(
            Map map,
            IntVec3 originCell,
            Vector3 forward,
            float radius,
            float coneAngle)
        {
            if (map == null || !originCell.InBounds(map))
            {
                yield break;
            }

            forward.y = 0f;
            forward.Normalize();
            float halfAngle = Mathf.Clamp(coneAngle, 1f, 180f) * 0.5f;
            Building originEdifice = originCell.GetEdifice(map);
            bool embeddedInWall = originEdifice?.def.Fillage == FillCategory.Full;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(originCell, radius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                Vector3 offset = cell.ToVector3Shifted() - originCell.ToVector3Shifted();
                offset.y = 0f;
                if (offset.sqrMagnitude > 0.01f && Vector3.Angle(forward, offset) > halfAngle)
                {
                    continue;
                }

                if (cell != originCell && (embeddedInWall || !GenSight.LineOfSight(originCell, cell, map, true)))
                {
                    continue;
                }

                yield return cell;
            }
        }
    }
}
