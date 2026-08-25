using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class ModularAttachmentTransform
    {
        public Vector3 position = Vector3.zero;
        public float angle;
        public Vector2 scale = Vector2.one;
        public float layer;

        public ModularAttachmentTransform Clone()
        {
            return new ModularAttachmentTransform
            {
                position = position,
                angle = angle,
                scale = scale,
                layer = layer
            };
        }
    }

    public sealed class ModularAttachmentSocket
    {
        public string id;
        public string label;
        public List<string> tags = new List<string>();
        public ModularAttachmentTransform transform = new ModularAttachmentTransform();
        public bool required;
        public bool isRail;
        public float railLength;
        public int maxAttachments;

        public string Label => label.NullOrEmpty() ? id : label;
    }

    public sealed class ModularAttachmentMount
    {
        public string id = "mount";
        public string label;
        public List<string> socketTags = new List<string>();
        public ModularAttachmentTransform transform = new ModularAttachmentTransform();
        public float railOccupancy;

        public string Label => label.NullOrEmpty() ? id : label;

        public bool Accepts(ModularAttachmentSocket socket)
        {
            if (socket == null) return false;
            if (socketTags.NullOrEmpty() || socket.tags.NullOrEmpty()) return true;

            for (int i = 0; i < socketTags.Count; i++)
            {
                if (socket.tags.Contains(socketTags[i])) return true;
            }

            return false;
        }
    }

    public sealed class ModularDefaultAttachment
    {
        public string socketId;
        public string mountId;
        public ThingDef part;
        public float railOffset;
    }

    public sealed class CompProperties_ModularWeaponNode : CompProperties
    {
        public bool isAssemblyRoot;
        public bool allowPlayerConfiguration = true;
        public float realisticRoundsPerMinute;
        public float baseFireDelayFactor = 1.2f;
        public float fireDelayMultiplier = 1f;
        public Vector3 graphicOffset = Vector3.zero;
        public float graphicAngle;
        public Vector2 graphicScale = Vector2.one;
        public float graphicLayer;
        public List<ModularAttachmentMount> mounts = new List<ModularAttachmentMount>();
        public List<ModularAttachmentSocket> sockets = new List<ModularAttachmentSocket>();
        public List<ModularDefaultAttachment> defaultAttachments =
            new List<ModularDefaultAttachment>();

        public CompProperties_ModularWeaponNode()
        {
            compClass = typeof(CompModularWeaponNode);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;

            if (isAssemblyRoot && realisticRoundsPerMinute <= 0f)
                yield return parentDef.defName
                    + " is a modular weapon root without realisticRoundsPerMinute.";
            if (isAssemblyRoot && baseFireDelayFactor < 1f)
                yield return parentDef.defName
                    + " has baseFireDelayFactor below its mechanical RPM floor.";
            if (fireDelayMultiplier <= 0f)
                yield return parentDef.defName + " has a non-positive fireDelayMultiplier.";

            HashSet<string> ids = new HashSet<string>();
            for (int i = 0; i < sockets.Count; i++)
            {
                ModularAttachmentSocket socket = sockets[i];
                if (socket == null || socket.id.NullOrEmpty())
                {
                    yield return parentDef.defName + " has a modular socket without an id.";
                    continue;
                }

                if (!ids.Add(socket.id))
                    yield return parentDef.defName + " has duplicate modular socket " + socket.id + ".";

                if (socket.isRail)
                {
                    if (socket.railLength <= 0f)
                        yield return parentDef.defName + " rail socket " + socket.id
                            + " must have a positive railLength.";
                    if (socket.transform != null
                        && !Mathf.Approximately(socket.transform.angle, 0f))
                        yield return parentDef.defName + " rail socket " + socket.id
                            + " must be horizontal (transform angle 0).";
                    if (socket.maxAttachments < 0)
                        yield return parentDef.defName + " rail socket " + socket.id
                            + " has a negative maxAttachments.";
                }
            }

            ids.Clear();
            for (int i = 0; i < mounts.Count; i++)
            {
                ModularAttachmentMount mount = mounts[i];
                if (mount == null || mount.id.NullOrEmpty())
                {
                    yield return parentDef.defName + " has a modular mount without an id.";
                    continue;
                }

                if (!ids.Add(mount.id))
                    yield return parentDef.defName + " has duplicate modular mount " + mount.id + ".";

                if (mount.railOccupancy < 0f)
                    yield return parentDef.defName + " mount " + mount.id
                        + " has a negative railOccupancy.";
                if (mount.socketTags != null
                    && mount.socketTags.Contains("picatinny")
                    && mount.railOccupancy <= 0f)
                    yield return parentDef.defName + " Picatinny mount " + mount.id
                        + " must have a positive railOccupancy.";
            }

            for (int i = 0; i < defaultAttachments.Count; i++)
            {
                ModularDefaultAttachment attachment = defaultAttachments[i];
                if (attachment == null || attachment.part == null)
                {
                    yield return parentDef.defName + " has an invalid default modular attachment.";
                    continue;
                }

                if (SocketNamed(attachment.socketId) == null)
                    yield return parentDef.defName + " default attachment uses missing socket "
                        + attachment.socketId + ".";
            }
        }

        public ModularAttachmentSocket SocketNamed(string id)
        {
            if (id.NullOrEmpty()) return null;
            for (int i = 0; i < sockets.Count; i++)
            {
                if (sockets[i]?.id == id) return sockets[i];
            }

            return null;
        }

        public ModularAttachmentMount MountNamed(string id)
        {
            if (!id.NullOrEmpty())
            {
                for (int i = 0; i < mounts.Count; i++)
                {
                    if (mounts[i]?.id == id) return mounts[i];
                }
            }

            return mounts.Count > 0 ? mounts[0] : null;
        }
    }

    public struct ModularTransform2D
    {
        public Vector2 position;
        public float angle;
        public Vector2 scale;
        public float layer;

        public static ModularTransform2D Identity => new ModularTransform2D
        {
            position = Vector2.zero,
            angle = 0f,
            scale = Vector2.one,
            layer = 0f
        };

        public Vector2 TransformPoint(Vector3 local)
        {
            Vector2 scaled = new Vector2(local.x * scale.x, local.z * scale.y);
            return position + Rotate(scaled, angle);
        }

        public ModularTransform2D Attach(
            ModularAttachmentTransform socket,
            ModularAttachmentTransform mount,
            float childAngleOffset = 0f)
        {
            socket = socket ?? new ModularAttachmentTransform();
            mount = mount ?? new ModularAttachmentTransform();

            Vector2 socketPosition = TransformPoint(socket.position);
            float socketAngle = angle + socket.angle;
            Vector2 socketScale = Vector2.Scale(scale, SafeScale(socket.scale));
            Vector2 mountScale = SafeScale(mount.scale);
            Vector2 childScale = new Vector2(
                socketScale.x / mountScale.x,
                socketScale.y / mountScale.y);
            // The authored part angle belongs to the complete child node, not just its
            // texture. Folding it into the attachment transform keeps the mount pinned
            // to the parent socket while the graphic centre and all descendant sockets
            // orbit around that mount.
            float childAngle = socketAngle - mount.angle + childAngleOffset;
            Vector2 mountedOffset = new Vector2(
                mount.position.x * childScale.x,
                mount.position.z * childScale.y);

            return new ModularTransform2D
            {
                position = socketPosition - Rotate(mountedOffset, childAngle),
                angle = childAngle,
                scale = childScale,
                layer = layer + socket.layer - mount.layer
            };
        }

        private static Vector2 SafeScale(Vector2 value)
        {
            return new Vector2(
                Mathf.Abs(value.x) < 0.0001f ? 1f : value.x,
                Mathf.Abs(value.y) < 0.0001f ? 1f : value.y);
        }

        public static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(
                value.x * cos - value.y * sin,
                value.x * sin + value.y * cos);
        }
    }

    public sealed class ModularRenderNode
    {
        public string path;
        public int depth;
        public Thing thing;
        public CompModularWeaponNode comp;
        public ModularTransform2D transform;
        public string parentSocketId;
        public string mountId;
        public CompModularWeaponNode parentComp;
        public int parentChildIndex = -1;
        public float railOffset;
        public bool attachedToRail;
        public Vector2 railStart;
        public Vector2 railEnd;
        public Vector2 occupiedRailStart;
        public Vector2 occupiedRailEnd;

        public CompProperties_ModularWeaponNode Props => comp.Props;
        public Vector2 GraphicCenter => transform.TransformPoint(Props.graphicOffset);
        public float GraphicAngle => transform.angle;
        public Vector2 GraphicScale => Vector2.Scale(transform.scale, Props.graphicScale);
        public float GraphicLayer => transform.layer + Props.graphicLayer;
    }
}
