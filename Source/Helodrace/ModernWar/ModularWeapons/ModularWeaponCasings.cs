using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    internal static class ModularWeaponCasingUtility
    {
        public static float EjectionCycleFraction(CompModularWeaponNode root)
        {
            List<ModularRenderNode> nodes = root?.RenderSnapshot();
            if (nodes == null) return 0.28f;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].Props.casingPort != null)
                    return Mathf.Clamp01(
                        nodes[i].Props.casingPort.ejectionCycleFraction);
            return 0.28f;
        }

        public static void TryEject(
            Verb verb,
            CompModularWeaponNode root)
        {
            Pawn shooter = verb?.caster as Pawn;
            Thing equipment = verb?.EquipmentSource;
            if (root?.Props.isAssemblyRoot != true
                || shooter?.Map == null
                || !shooter.Spawned
                || equipment == null)
                return;

            List<ModularRenderNode> nodes = root.RenderSnapshot();
            ModularRenderNode portNode = null;
            ThingDef moteDef = null;
            int motePriority = int.MinValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                CompProperties_ModularWeaponNode props = nodes[i].Props;
                if (portNode == null && props.casingPort != null)
                    portNode = nodes[i];
                if (props.casingMoteDef != null
                    && props.overridePriority >= motePriority)
                {
                    moteDef = props.casingMoteDef;
                    motePriority = props.overridePriority;
                }
            }

            ModularWeaponCasingPortProperties port = portNode?.Props.casingPort;
            if (port == null || moteDef == null) return;

            MoteThrown casing = ThingMaker.MakeThing(moteDef) as MoteThrown;
            if (casing == null) return;

            Vector3 shotDirection = verb.CurrentTarget.CenterVector3 - shooter.DrawPos;
            shotDirection.y = 0f;
            if (shotDirection.sqrMagnitude < 0.0001f)
                shotDirection = shooter.Rotation.FacingCell.ToVector3();
            shotDirection.Normalize();
            float aimAngle = shotDirection.AngleFlat();

            Vector3 gunCenter;
            float bodyAngle;
            bool flipped;
            ModularWeaponAimingPoseUtility.Resolve(
                equipment,
                shooter,
                aimAngle,
                out gunCenter,
                out bodyAngle,
                out flipped);

            Vector3 portLocal = portNode.Props.graphicOffset + port.offset;
            Vector2 transformed = portNode.transform.TransformPoint(portLocal);
            if (flipped) transformed.x = -transformed.x;
            Vector3 worldOffset = Quaternion.AngleAxis(bodyAngle, Vector3.up)
                * new Vector3(transformed.x, 0f, transformed.y);
            Vector3 origin = gunCenter + worldOffset;

            GenSpawn.Spawn(casing, origin.ToIntVec3(), shooter.Map);
            // The west-facing weapon graphic is mirrored. Mirror its authored
            // local ejection angle as well, otherwise +90 degrees sends western
            // casings north/up instead of south/down with the visible port.
            float ejectionOffset = flipped
                ? -port.ejectionAngleOffset
                : port.ejectionAngleOffset;
            float ejectionAngle = aimAngle + ejectionOffset
                + Rand.Range(-16f, 16f);
            casing.exactPosition = origin;
            // The texture's authored north/up axis starts 90 degrees from the
            // travel direction. Reverse that facing for the mirrored west pose
            // with rotation, rather than a negative scale that culls the mote.
            casing.exactRotation = ejectionAngle + (flipped ? 90f : -90f);
            casing.rotationRate = Rand.Range(-900f, 900f);
            casing.Scale = port.scale;
            // Do not mirror a MoteThrown with a negative X scale. That reverses
            // the quad winding and makes the west-only casing get back-face culled.
            // Its port position and ejection direction are already mirrored above.
            casing.SetVelocity(
                ejectionAngle,
                Rand.Range(port.speedMin, port.speedMax));
        }
    }
}
