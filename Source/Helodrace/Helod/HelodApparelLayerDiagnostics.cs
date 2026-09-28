using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    // Observe the matrix returned by the real renderer. LayerFor alone cannot
    // show subsequent subworker transforms, parent offsets or other patches.
    [HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.TryGetMatrix))]
    public static class HelodApparelLayerDiagnostics
    {
        private sealed class Row
        {
            public string label;
            public string worker;
            public float baseLayer;
            public float layer;
            public float altitude;
            public float finalY;
            public float transformY;
            public bool hair;
            public bool shell;
        }

        private sealed class Snapshot
        {
            public PawnRenderNode root;
            public readonly Dictionary<PawnRenderNode, Row> rows = new Dictionary<PawnRenderNode, Row>();
            public bool reported;
        }

        private static readonly ConditionalWeakTable<PawnRenderTree, Snapshot> Snapshots =
            new ConditionalWeakTable<PawnRenderTree, Snapshot>();

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnRenderTree __instance, PawnRenderNode node,
            PawnDrawParms parms, Matrix4x4 matrix, bool __result)
        {
            if (!__result || parms.Portrait || parms.facing != Rot4.North
                || parms.pawn?.def?.defName != "Helod") return;
            bool hair = node is PawnRenderNode_Hair;
            bool shell = node.apparel?.def?.apparel?.LastLayer == ApparelLayerDefOf.Shell
                && node.Worker is PawnRenderNodeWorker_Apparel_Body;
            bool belt = node.apparel?.def?.defName == "HD_Apparel_MOLLEBattleBelt";
            if (!hair && !shell && !belt) return;
            Snapshot snapshot = Snapshots.GetValue(__instance, _ => new Snapshot());
            if (snapshot.root != __instance.rootNode)
            {
                snapshot.root = __instance.rootNode;
                snapshot.rows.Clear();
            }
            float altitude = node.Worker.AltitudeFor(node, parms);
            snapshot.rows[node] = new Row
            {
                label = node.apparel?.def?.defName ?? node.Props.debugLabel,
                worker = node.Worker.GetType().FullName,
                baseLayer = node.Props.baseLayer,
                layer = node.Worker.LayerFor(node, parms),
                altitude = altitude,
                finalY = matrix.m13,
                transformY = matrix.m13 - parms.matrix.m13 - altitude,
                hair = hair,
                shell = shell
            };
            Row hairRow = snapshot.rows.Values.FirstOrDefault(row => row.hair);
            if (!snapshot.reported && hairRow != null
                && snapshot.rows.Values.Any(row => row.shell && row.finalY >= hairRow.finalY))
            {
                snapshot.reported = true;
                Log.Warning(Report(snapshot, parms.pawn));
            }
        }

        private static string Report(Snapshot snapshot, Pawn pawn)
        {
            var text = new StringBuilder("[Helodrace layer diagnostics] North-facing final render matrices\n");
            text.AppendLine("Race cache active: " + HelodRace.IsHelod(pawn));
            foreach (Row row in snapshot.rows.Values.OrderBy(row => row.finalY))
                text.AppendLine($"{row.label}: worker={row.worker}, base={row.baseLayer:F4}, "
                    + $"LayerFor={row.layer:F4}, altitude={row.altitude:F7}, "
                    + $"transformY={row.transformY:F7}, finalY={row.finalY:F7}");
            MethodInfo layerMethod = AccessTools.Method(typeof(PawnRenderNodeWorker_Apparel_Body), "LayerFor");
            Patches patches = Harmony.GetPatchInfo(layerMethod);
            text.AppendLine("Apparel LayerFor patch owners: "
                + (patches == null ? "none" : string.Join(", ", patches.Owners)));
            // These are the actual cached requests used by Draw, after
            // ParallelPreDraw and other mods have processed the matrices.
            PawnRenderTree tree = pawn.Drawer?.renderer?.renderTree;
            List<PawnGraphicDrawRequest> requests = tree == null ? null
                : AccessTools.Field(typeof(PawnRenderTree), "drawRequests")
                    .GetValue(tree) as List<PawnGraphicDrawRequest>;
            if (requests != null)
            {
                text.AppendLine("Actual draw requests (after other rendering patches):");
                for (int i = 0; i < requests.Count; i++)
                {
                    PawnGraphicDrawRequest request = requests[i];
                    PawnRenderNode node = request.node;
                    if (node == null || (!snapshot.rows.ContainsKey(node)
                        && !(node is PawnRenderNode_HelodAppendage))) continue;
                    Material material = request.material;
                    string label = node.apparel?.def?.defName ?? node.Props.debugLabel;
                    string shader = material?.shader?.name ?? "null";
                    string texture = material?.mainTexture?.name ?? "null";
                    string mask = material != null && material.HasProperty(ShaderPropertyIDs.MaskTex)
                        ? material.GetTexture(ShaderPropertyIDs.MaskTex)?.name ?? "none" : "unsupported";
                    Bounds bounds = request.mesh != null ? request.mesh.bounds : default(Bounds);
                    text.AppendLine($"draw[{i}] {label}: shader={shader}, queue={material?.renderQueue}, "
                        + $"texture={texture}, mask={mask}, cachedY={request.preDrawnComputedMatrix.m13:F7}, "
                        + $"meshY=[{bounds.min.y:F7},{bounds.max.y:F7}]");
                }
                Camera camera = Find.Camera;
                if (camera != null)
                    text.AppendLine($"Camera: orthographic={camera.orthographic}, "
                        + $"clip=[{camera.nearClipPlane},{camera.farClipPlane}], "
                        + $"forward={camera.transform.forward}, depthMode={camera.depthTextureMode}");
            }
            text.AppendLine("Expected: MOLLE finalY < coat finalY < hair finalY. "
                + "LayerFor mismatch indicates a bypassed/overridden layer rule; altitude mismatch "
                + "indicates a later layer transform; finalY mismatch indicates matrix/parent offsets.");
            return text.ToString();
        }

        [DebugAction("Helodrace", "Log selected pawn north layers", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void LogSelectedPawnLayers()
        {
            Pawn pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null || pawn.def?.defName != "Helod")
            {
                Messages.Message("Select a Helod pawn first.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            PawnRenderTree tree = pawn.Drawer.renderer.renderTree;
            if (Snapshots.TryGetValue(tree, out Snapshot snapshot) && snapshot.rows.Count > 0)
                Log.Warning(Report(snapshot, pawn));
            else
                Messages.Message("Render the selected pawn facing north, then run this action again.",
                    MessageTypeDefOf.RejectInput, false);
        }
    }
}
