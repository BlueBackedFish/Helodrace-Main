using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Helodrace.ModernWar;
namespace Helodrace.Tests.FrozenRenderOffsets
{
public sealed class PawnRenderNodeWorker_HelodAppendage : PawnRenderNodeWorker
{
private static HelodAppendage AnatomicalKind(HelodAppendage kind, Rot4 facing)
        {
            if (facing != Rot4.West) return kind;
            if (kind == HelodAppendage.LeftEar) return HelodAppendage.RightEar;
            if (kind == HelodAppendage.RightEar) return HelodAppendage.LeftEar;
            return kind;
        }
public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 result = base.OffsetFor(node, parms, out pivot);
            var renderKind = ((PawnRenderNodeProperties_HelodAppendage)node.Props).appendage;
            Rot4 facing = parms.flipHead && renderKind != HelodAppendage.Tail
                ? parms.facing.Opposite : parms.facing;
            HelodAppendageOffset entry = ConfiguredOffsetFor(renderKind);
            if (entry == null) return result;
            Vector3 offset = DirectionalOffsetFor(entry, facing);
            HelodAppendage anatomicalKind = AnatomicalKind(renderKind, parms.facing);
            HelodAppendageOffset developmentalEntry = ConfiguredOffsetFor(anatomicalKind);
            if (developmentalEntry != null)
                offset += DevelopmentalOffsetFor(developmentalEntry,
                    parms.pawn.DevelopmentalStage, parms.facing);
            return result + offset;
        }
internal static bool TryGetConfiguredOffset(
            HelodAppendage kind, Rot4 facing, out Vector3 offset)
        {
            offset = Vector3.zero;
            HelodAppendageOffset entry = ConfiguredOffsetFor(kind);
            if (entry == null) return false;
            offset = DirectionalOffsetFor(entry, facing);
            return true;
        }
private static HelodAppendageOffset ConfiguredOffsetFor(HelodAppendage kind)
        {
            var entries = HelodRace.Settings?.appendageOffsets;
            if (entries == null) return null;
            for (int i = 0; i < entries.Count; i++)
            {
                HelodAppendageOffset entry = entries[i];
                if (entry == null || entry.appendage != kind) continue;
                return entry;
            }
            return null;
        }
private static Vector3 DirectionalOffsetFor(HelodAppendageOffset entry, Rot4 facing)
        {
            return facing == Rot4.North ? entry.north
                : facing == Rot4.South ? entry.south
                : facing == Rot4.East ? entry.east : entry.west;
        }
internal static Vector3 DevelopmentalOffsetFor(
            HelodAppendageOffset entry, DevelopmentalStage stage, Rot4 facing)
        {
            if (stage.Baby())
                return facing == Rot4.North ? entry.babyNorth
                    : facing == Rot4.South ? entry.babySouth
                    : facing == Rot4.East ? entry.babyEast : entry.babyWest;
            if (stage.Child())
                return facing == Rot4.North ? entry.childNorth
                    : facing == Rot4.South ? entry.childSouth
                    : facing == Rot4.East ? entry.childEast : entry.childWest;
            return Vector3.zero;
        }
}
public sealed class ArmorWorker : PawnRenderNodeWorker
{
public override Vector3 OffsetFor(
            PawnRenderNode node,
            PawnDrawParms parms,
            out Vector3 pivot)
        {
            Vector3 result = base.OffsetFor(node, parms, out pivot);
            PawnRenderNode_ModularArmorPart modularNode =
                (PawnRenderNode_ModularArmorPart)node;
            ModularArmorPartDef part = modularNode.Installed.part;
            ModularArmorPositionDef position = modularNode.Installed.EffectivePosition;

            if (modularNode.IsAuthoredPals)
            {
                return result;
            }

            if (position.drawOffsets != null)
            {
                result += position.drawOffsets.For(parms.facing);
            }

            if (part.drawOffsets != null)
            {
                result += part.drawOffsets.For(parms.facing);
            }

            if (modularNode.Installed.IsPalsMounted)
            {
                ModularArmorPalsPanelDef panel = modularNode.Installed.palsPanel;
                if (panel.drawOrigin != null)
                {
                    result += panel.drawOrigin.For(parms.facing);
                }

                float gridX = modularNode.Installed.palsX
                    + part.PalsWidthFor(panel) * 0.5f
                    - panel.columns * 0.5f;
                float gridY = panel.rows * 0.5f
                    - modularNode.Installed.palsY
                    - part.palsHeight * 0.5f;
                float sideCompression = parms.facing == Rot4.East
                    || parms.facing == Rot4.West
                    ? 0.35f
                    : 1f;
                result.x += gridX * panel.drawCellSize.x * sideCompression;
                result.z += gridY * panel.drawCellSize.y;
            }

            result += modularNode.NodeGraphicData.DrawOffsetForRot(parms.facing);
            return result;
        }
}
public class Thing_InstalledBreachCharge : ThingWithComps
    {
        public override Vector3 DrawPos
        {
            get
            {
                Vector3 drawPos = base.DrawPos;
                Vector3 outward = Rotation.FacingCell.ToVector3();
                drawPos += outward * 0.5f;
                return drawPos;
            }
        }
    }
}
