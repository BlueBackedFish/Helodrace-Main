using RimWorld;
using UnityEngine;
using Verse;
namespace Helodrace.Tests.FrozenColors
{
public static class Patch_HelodHairPalette
    {
        private static readonly Color32[] Colors =
        {
            new Color32(92,92,92,255), new Color32(181,132,102,255),
            new Color32(153,69,0,255), new Color32(221,150,0,255),
            new Color32(242,214,153,255), new Color32(148,138,138,255),
            new Color32(181,135,89,255), new Color32(171,40,46,255),
            new Color32(229,193,255,255), new Color32(0,206,255,255),
            new Color32(230,230,230,255)
        };
        private static readonly int[] Weights = { 70,150,150,100,100,50,40,30,40,40,20 };
        private static readonly int[] ColorIndices = { 0,1,2,3,4,5,6,7,8,9,10 };

        public static bool Prefix(Pawn pawn, int ageYears, ref Color __result)
        {
            if (!HelodRace.IsHelod(pawn)) return true;
            // Gene hair overrides still take precedence in the vanilla gene tracker.
            __result = PawnHairColors.HasGreyHair(pawn, ageYears)
                ? PawnHairColors.RandomGreyHairColor()
                : (Color)Colors[ColorIndices.RandomElementByWeight(i => Weights[i])];
            return false;
        }
    }
internal sealed class GasCell
{private readonly Map Map;internal GasCell(Map map){Map=map;}
private void AddCell(IntVec3 cell, HelodGasDef gas, byte density, LayerSubMesh subMesh, float altitude)
        {
            int mapIndex = Map.cellIndices.CellToIndex(cell);
            uint hash = (uint)(mapIndex * 747796405) + (uint)(gas.index * 2891336453u);
            hash ^= hash >> 16;
            float scaleOffset = 0.4f + (hash & 255u) / 1275f;
            float xOffset = ((hash >> 8) & 255u) / 637.5f - 0.2f;
            float zOffset = ((hash >> 16) & 255u) / 637.5f - 0.2f;
            float alpha = Mathf.Lerp(0.16f, gas.color.a, density / (float)byte.MaxValue);
            Color vertexColor = new Color(1f, 1f, 1f, alpha);

            float x1 = cell.x - scaleOffset + xOffset;
            float x2 = cell.x + 1f + scaleOffset + xOffset;
            float z1 = cell.z - scaleOffset + zOffset;
            float z2 = cell.z + 1f + scaleOffset + zOffset;
            int start = subMesh.verts.Count;
            subMesh.verts.Add(new Vector3(x1, altitude, z1));
            subMesh.verts.Add(new Vector3(x1, altitude, z2));
            subMesh.verts.Add(new Vector3(x2, altitude, z2));
            subMesh.verts.Add(new Vector3(x2, altitude, z1));
            subMesh.uvs.Add(new Vector3(0f, 0f, mapIndex));
            subMesh.uvs.Add(new Vector3(0f, 1f, mapIndex));
            subMesh.uvs.Add(new Vector3(1f, 1f, mapIndex));
            subMesh.uvs.Add(new Vector3(1f, 0f, mapIndex));
            for (int i = 0; i < 4; i++)
            {
                subMesh.colors.Add(vertexColor);
            }
            subMesh.tris.Add(start);
            subMesh.tris.Add(start + 1);
            subMesh.tris.Add(start + 2);
            subMesh.tris.Add(start);
            subMesh.tris.Add(start + 2);
            subMesh.tris.Add(start + 3);
        }
}
}
