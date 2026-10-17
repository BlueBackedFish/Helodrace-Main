using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class GameComponent_ModularPlacementWorkspace : GameComponent
    {
        // Like live Def editing itself, the workspace survives closing editor
        // windows, not restarting/loading the game. Export XML before leaving.
        public readonly ModularPlacementWorkspace Workspace = new ModularPlacementWorkspace();
        public GameComponent_ModularPlacementWorkspace(Game game) { }
    }

    public sealed class ModularPlacementWorkspace
    {
        public readonly ModularPlacementBatch Batch = new ModularPlacementBatch();
        public readonly List<CompModularWeaponNode> Roots = new List<CompModularWeaponNode>();
        private readonly Action<CompModularWeaponNode> invalidate;

        public ModularPlacementWorkspace(Action<CompModularWeaponNode> invalidateWeapon = null)
        {
            invalidate = invalidateWeapon ?? (root => root.InvalidateTree());
        }

        public void Visit(CompModularWeaponNode root)
        {
            if (!IsLive(root)) return;
            if (!Roots.Contains(root))
            {
                Roots.Add(root);
                // This weapon may have cached a shared Def before it was edited
                // through another weapon, even before joining this workspace.
                invalidate(root);
            }
            Refresh();
        }

        public void Refresh()
        {
            if (Batch.RefreshParts())
                foreach (CompModularWeaponNode root in Roots)
                    if (IsLive(root)) invalidate(root);
            foreach (CompModularWeaponNode root in Roots)
            {
                if (!IsLive(root)) continue; // Keep the last capture if a weapon was destroyed.
                List<ModularRenderNode> nodes = root.RenderSnapshot();
                foreach (ModularRenderNode node in nodes)
                {
                    ModularRenderNode captured = node;
                    Batch.ObservePart(node.thing.def.defName, () => Dialog_ModularAttachmentEditor.BuildXml(
                        new List<ModularRenderNode> {captured}));
                }
                Batch.ObserveAssembly(root.parent.thingIDNumber.ToString(CultureInfo.InvariantCulture), AssemblyXml(root, nodes));
            }
        }

        internal static bool IsLive(CompModularWeaponNode root) => root?.parent != null && !root.parent.Destroyed;

        internal static string AssemblyXml(CompModularWeaponNode root, List<ModularRenderNode> nodes)
        {
            var parts = new XElement("parts");
            foreach (ModularRenderNode node in nodes)
            {
                var part = new XElement("li", new XElement("path", node.path), new XElement("part", node.thing.def.defName));
                if (node.parentComp != null)
                {
                    part.Add(new XElement("parentPath", nodes.Find(n => n.comp == node.parentComp)?.path ?? "root"),
                        new XElement("socketId", node.parentSocketId), new XElement("mountId", node.mountId));
                    if (node.attachedToRail) part.Add(new XElement("railOffset", node.railOffset.ToString("0.######", CultureInfo.InvariantCulture)));
                }
                parts.Add(part);
            }
            return new XElement("li", new XElement("weaponDef", root.parent.def.defName),
                new XElement("thingId", root.parent.thingIDNumber), new XElement("label", root.parent.Label), parts).ToString();
        }
    }
}
