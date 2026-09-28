using System.Collections.Generic;
using UnityEngine;

namespace Helodrace.ModernWar
{
    internal static class ModularWeaponPartPerformance
    {
        internal static bool IsForegrip(ModularRenderNode node)
        {
            return node?.comp?.Props.performanceRole
                == ModularWeaponPerformanceRole.Foregrip;
        }

        // Follow non-rail adapters (such as an optic mount) to the rail that
        // determines how the complete accessory is actually oriented.
        internal static ModularRailSurface MountSurface(
            ModularRenderNode node,
            List<ModularRenderNode> nodes)
        {
            for (int depth = 0; node != null && depth < 32; depth++)
            {
                if (node.attachedToRail)
                {
                    ModularAttachmentSocket socket = node.parentComp
                        ?.SocketNamed(node.parentSocketId);
                    if (socket?.isRail == true) return socket.railSurface;
                }

                CompModularWeaponNode parent = node.parentComp;
                node = null;
                if (parent == null || nodes == null) break;
                for (int i = 0; i < nodes.Count; i++)
                    if (nodes[i].comp == parent)
                    {
                        node = nodes[i];
                        break;
                    }
            }
            return ModularRailSurface.Unspecified;
        }

        internal static Dictionary<int, float> ResolveWeights(
            List<ModularRenderNode> nodes,
            Dictionary<int, float> sightWeights)
        {
            Dictionary<int, float> result = new Dictionary<int, float>();
            if (nodes == null) return result;

            int bestGripId = 0;
            float bestGripWeight = 0f;
            int bestLaserId = 0;
            float bestLaserWeight = 0f;
            List<int> panelIds = new List<int>();
            Dictionary<int, float> panelBenefits = new Dictionary<int, float>();
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                if (node?.thing == null || node.Props == null) continue;
                ModularRailSurface surface = MountSurface(node, nodes);
                float weight = 1f;
                if (node.Props.sight != null)
                {
                    if (sightWeights == null || !sightWeights.TryGetValue(
                        node.thing.thingIDNumber, out weight)) weight = 0f;
                    if (surface == ModularRailSurface.Bottom) weight = 0f;
                    else if (surface == ModularRailSurface.Side) weight *= 0.5f;
                }
                else if (IsForegrip(node))
                {
                    if (surface == ModularRailSurface.Top) weight = 0f;
                    else if (surface == ModularRailSurface.Side) weight = 0.45f;
                    if (weight > bestGripWeight)
                    {
                        bestGripWeight = weight;
                        bestGripId = node.thing.thingIDNumber;
                    }
                }
                else if (node.Props.laser != null)
                {
                    if (surface == ModularRailSurface.Top) weight = 0.85f;
                    else if (surface == ModularRailSurface.Bottom) weight = 0.65f;
                    if (weight > bestLaserWeight)
                    {
                        bestLaserWeight = weight;
                        bestLaserId = node.thing.thingIDNumber;
                    }
                }
                else if (node.Props.performanceRole
                    == ModularWeaponPerformanceRole.RailPanel)
                {
                    panelIds.Add(node.thing.thingIDNumber);
                    panelBenefits[node.thing.thingIDNumber] =
                        Mathf.Max(0f, node.Props.internalStats?.ergonomics ?? 0f);
                }
                result[node.thing.thingIDNumber] = Mathf.Clamp01(weight);
            }

            // Two foregrips cannot provide two independent support hands.
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                if (IsForegrip(node)
                    && node.thing.thingIDNumber != bestGripId)
                    result[node.thing.thingIDNumber] = 0f;
                if (node?.comp?.Props.laser != null
                    && node.thing.thingIDNumber != bestLaserId)
                    result[node.thing.thingIDNumber] = 0f;
            }

            panelIds.Sort((a, b) =>
            {
                int byPlacement = panelBenefits[b].CompareTo(panelBenefits[a]);
                return byPlacement != 0 ? byPlacement : a.CompareTo(b);
            });
            for (int i = 0; i < panelIds.Count; i++)
            {
                float diminishing = i == 0 ? 1f
                    : i == 1 ? 0.5f
                    : i == 2 ? 0.25f : 0f;
                result[panelIds[i]] *= diminishing;
            }
            return result;
        }
    }
}
