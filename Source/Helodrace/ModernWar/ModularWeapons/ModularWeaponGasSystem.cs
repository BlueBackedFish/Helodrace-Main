using System;
using System.Collections.Generic;
using UnityEngine;

namespace Helodrace.ModernWar
{
    /// <summary>
    /// Physical muzzle-signature values derived from the already-authored attachment
    /// geometry. No barrel- or muzzle-device-specific tuning value is required.
    /// </summary>
    public sealed class ModularWeaponMuzzleSignature
    {
        public float BarrelLengthInches { get; internal set; }
        public float BarrelLengthFactor { get; internal set; } = 1f;
        public float MuzzleDeviceFactor { get; internal set; } = 1f;
        public float GasFactor { get; internal set; } = 1f;
        public float Coefficient { get; internal set; } = 1f;
    }

    internal static class ModularWeaponGasSystemUtility
    {
        public const float MinimumSetting = 0.7f;
        public const float MaximumSetting = 1.3f;
        public const float DefaultSetting = 1f;

        // Existing AR-15 geometry averages approximately 0.445 local units at 20 in.
        private const float BarrelGeometryToInches = 45f;
        private const float ReferenceBarrelLengthInches = 16f;

        public static bool HasInstalledGasSystem(List<ModularRenderNode> nodes)
        {
            if (nodes == null) return false;
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                if (node == null) continue;
                if (ContainsToken(node.parentSocketId, "gas")) return true;

                ModularAttachmentMount mount = node.Props?.MountNamed(node.mountId);
                if (ContainsToken(mount?.id, "gas")
                    || ContainsToken(mount?.socketTags, "gas"))
                    return true;
            }
            return false;
        }

        public static ModularWeaponMuzzleSignature ResolveMuzzleSignature(
            List<ModularRenderNode> nodes,
            float gasSetting,
            bool suppressedByDefinition)
        {
            ModularWeaponMuzzleSignature result = new ModularWeaponMuzzleSignature();
            if (nodes == null || nodes.Count == 0)
            {
                result.Coefficient = 0f;
                return result;
            }

            float barrelLength = ResolveBarrelLengthInches(nodes);
            result.BarrelLengthInches = barrelLength;
            if (barrelLength <= 0.01f)
            {
                result.Coefficient = 0f;
                return result;
            }

            result.BarrelLengthFactor = Mathf.Clamp(
                Mathf.Pow(ReferenceBarrelLengthInches / barrelLength, 0.8f),
                0.65f,
                1.9f);
            result.MuzzleDeviceFactor = ResolveMuzzleDeviceFactor(nodes);
            result.GasFactor = Mathf.Clamp(
                1f + (Mathf.Clamp(gasSetting, MinimumSetting, MaximumSetting) - 1f)
                    * 0.12f,
                0.95f,
                1.05f);
            result.Coefficient = suppressedByDefinition
                ? 0f
                : Mathf.Clamp(
                    result.BarrelLengthFactor
                        * result.MuzzleDeviceFactor
                        * result.GasFactor,
                    0.05f,
                    2.5f);
            return result;
        }

        private static float ResolveBarrelLengthInches(List<ModularRenderNode> nodes)
        {
            float longest = 0f;
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                CompProperties_ModularWeaponNode props = node?.Props;
                ModularAttachmentSocket muzzle = props?.SocketNamed("muzzle");
                if (muzzle?.transform == null || props.mounts == null) continue;

                ModularAttachmentMount barrelMount = null;
                for (int j = 0; j < props.mounts.Count; j++)
                {
                    ModularAttachmentMount candidate = props.mounts[j];
                    if (ContainsToken(candidate?.id, "barrel")
                        || ContainsToken(candidate?.socketTags, "barrel"))
                    {
                        barrelMount = candidate;
                        break;
                    }
                }
                if (barrelMount?.transform == null) continue;

                float localLength = Mathf.Abs(
                    muzzle.transform.position.x - barrelMount.transform.position.x);
                localLength *= Mathf.Abs(node.transform.scale.x);
                longest = Mathf.Max(longest, localLength * BarrelGeometryToInches);
            }
            return longest;
        }

        private static float ResolveMuzzleDeviceFactor(List<ModularRenderNode> nodes)
        {
            float directDeviceFactor = 1.15f; // exposed barrel thread / bare muzzle
            bool directDeviceFound = false;
            bool suppressorAttached = false;
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                if (node == null) continue;

                if (string.Equals(node.parentSocketId, "muzzle",
                    StringComparison.OrdinalIgnoreCase))
                {
                    directDeviceFound = true;
                    directDeviceFactor = HasSuppressorInterface(node.Props)
                        ? 0.68f
                        : 0.82f;
                }
                else if (ContainsToken(node.parentSocketId, "suppressor"))
                {
                    suppressorAttached = true;
                }
            }
            float factor = directDeviceFound ? directDeviceFactor : 1.15f;
            // Keep the host flash-hider factor and add the attached can's effect.
            // An integral suppressor can occupy its own barrel socket without a host
            // muzzle device; it receives a stronger reduction so short MP5SD barrels
            // remain in the renderer's smoke-only range.
            return suppressorAttached
                ? (directDeviceFound ? factor * 0.25f : 0.14f)
                : factor;
        }

        private static bool HasSuppressorInterface(
            CompProperties_ModularWeaponNode props)
        {
            if (props?.sockets == null) return false;
            for (int i = 0; i < props.sockets.Count; i++)
            {
                ModularAttachmentSocket socket = props.sockets[i];
                if (ContainsToken(socket?.id, "suppressor")
                    || ContainsToken(socket?.tags, "suppressor"))
                    return true;
            }
            return false;
        }

        private static bool ContainsToken(string value, string token)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsToken(List<string> values, string token)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++)
                if (ContainsToken(values[i], token)) return true;
            return false;
        }
    }
}
