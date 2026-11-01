using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Helodrace.Tactical;
using Helodrace.Tactics;
namespace Helodrace.Tests.FrozenPresentation
{
internal static class TakedownGeometry
{
private const float BackArcHalfWidth=60;
private static bool IsBehindTarget(Pawn attacker, Pawn target)
        {
            Vector3 direction = attacker.DrawPos - target.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return false;
            }

            float toAttackerAngle = direction.AngleFlat();
            float facingDelta = Mathf.Abs(Mathf.DeltaAngle(
                target.Rotation.AsAngle,
                toAttackerAngle));
            return facingDelta >= 180f - BackArcHalfWidth;
        }
private static void BeginAnimation(Pawn attacker, Pawn target)
        {
            Vector3 direction = target.DrawPos - attacker.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            attacker.Rotation = Rot4.FromAngleFlat(direction.AngleFlat());
        }
}
internal sealed class RopeGeometry
{
private static readonly Material RopeOutline=SolidColorMaterials.SimpleSolidColorMaterial(new Color(.10f,.08f,.05f));
private static readonly Material RopeCore=SolidColorMaterials.SimpleSolidColorMaterial(new Color(.85f,.70f,.36f));
private List<TcccDragLink> links=new();
private readonly Map map;
internal RopeGeometry(Map map){this.map=map;}
public void MapComponentDraw()
        {
            foreach (TcccDragLink link in links)
                if (link.carrier?.Map == map && link.patient?.Map == map)
                {
                    Vector3 start = link.carrier.DrawPos + new Vector3(.24f, 0, .08f);
                    Vector3 end = link.patient.DrawPos + new Vector3(.15f, 0, .08f);
                    start.y = end.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                    // Route a short slack bend outside the sprites: a center-to-center line
                    // disappears underneath adjacent standing/downed pawn bodies.
                    Vector3 direction = end - start;
                    Vector3 side = new Vector3(-direction.z, 0, direction.x).normalized;
                    Vector3 bend = (start + end) * .5f + side * .38f;
                    DrawRopeSegment(start, bend); DrawRopeSegment(bend, end);
                }
        }
private static void DrawRopeSegment(Vector3 start, Vector3 end)
        {
            GenDraw.DrawLineBetween(start, end, RopeOutline, .09f);
            start.y += .01f; end.y += .01f;
            GenDraw.DrawLineBetween(start, end, RopeCore, .045f);
        }
}
internal static class GrenadeGeometry
{
public static void Postfix(Pawn pawn, Vector3 drawPos, Rot4 facing, PawnRenderFlags flags)
        {
            var driver = pawn.jobs?.curDriver as JobDriver_TacticalThrow;
            Thing grenade = driver?.Prepared == true && !driver.Released ? driver.Grenade : null;
            if (pawn.jobs?.curDriver is JobDriver_ThrowInventoryGrenade inventory && !inventory.Released) grenade = inventory.Grenade;
            if (grenade?.Graphic == null || flags.HasFlag(PawnRenderFlags.Portrait) || pawn.Dead || pawn.Downed) return;
            Vector3 hand = drawPos + new Vector3(.23f, .045f, .25f).RotatedBy(facing.AsAngle);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(hand, facing.AsQuat, new Vector3(.42f, 1f, .42f)),
                grenade.Graphic.MatAt(facing, grenade), 0);
        }
}
}
