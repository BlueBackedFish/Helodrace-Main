using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Helodrace.ModernWar
{
    // Expand only visual consumers. Gameplay snapshots retain one node per actual part.
    internal static class ModularWeaponVisualLayers
    {
        private static readonly ConditionalWeakTable<List<ModularRenderNode>, List<ModularRenderNode>> cache =
            new ConditionalWeakTable<List<ModularRenderNode>, List<ModularRenderNode>>();

        public static List<ModularRenderNode> Expand(List<ModularRenderNode> nodes)
        {
            return cache.GetValue(nodes, Build);
        }

        private static List<ModularRenderNode> Build(List<ModularRenderNode> nodes)
        {
            var result = new List<ModularRenderNode>();
            foreach (var node in nodes)
            {
                result.Add(node);
                if (node.Props.additionalGraphics == null) continue;
                foreach (var graphic in node.Props.additionalGraphics)
                    if (graphic?.graphicData != null) result.Add(node.WithGraphic(graphic));
            }
            // Stable ordering for equal layers.
            for (int i = 1; i < result.Count; i++)
            {
                var value = result[i];
                int j = i - 1;
                while (j >= 0 && result[j].GraphicLayer > value.GraphicLayer)
                {
                    result[j + 1] = result[j];
                    j--;
                }
                result[j + 1] = value;
            }
            return result;
        }
    }
}
