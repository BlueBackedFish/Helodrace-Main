using System;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Helodrace.ModernWar;
namespace Helodrace.Tests.FrozenWireSniper
{
public sealed class Probe : Projectile
{
private const float WireWidth=.022f;
private static Material WireMaterial=>(Material)typeof(Projectile_ZaperX26Probe).GetProperty("WireMaterial",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map impactMap = Map;
            Pawn firingPawn = Launcher as Pawn;
            Pawn hitPawn = !blockedByShield ? hitThing as Pawn : null;
            CompZaperX26 zaperComp = equipment?.TryGetComp<CompZaperX26>();
            float impactAngle = ExactRotation.eulerAngles.y;
            Thing intendedThing = intendedTarget.Thing;
            ThingDef sourceWeaponDef = equipmentDef;

            base.Impact(hitThing, blockedByShield);

            if (hitPawn != null && firingPawn != null && zaperComp != null)
            {
                bool instigatorGuilty = !firingPawn.Drafted;
                DamageInfo probeDamage = new DamageInfo(
                    DamageDefOf.Bullet,
                    DamageAmount,
                    ArmorPenetration,
                    impactAngle,
                    firingPawn,
                    weapon: sourceWeaponDef,
                    intendedTarget: intendedThing,
                    instigatorGuilty: instigatorGuilty);
                DamageWorker.DamageResult result = hitPawn.TakeDamage(probeDamage);

                // Armor blocks the electrical circuit when no gunshot wound is
                // produced. Such a probe does not count toward the required pair.
                if (result.totalDamageDealt > 0f && result.wounded)
                {
                    zaperComp.RegisterProbeHit(firingPawn, hitPawn, impactMap);
                }
            }
        }
private void DrawFlightWire(Vector3 probePosition)
        {
            if (!(Launcher is Pawn pawn) || !pawn.Spawned || pawn.Map != Map)
            {
                return;
            }

            DrawSaggingWire(pawn.DrawPos, probePosition, 0f, WireWidth);
        }
internal static void DrawSaggingWire(Vector3 start, Vector3 end, float lateralOffset, float width)
        {
            Vector3 direction = end - start;
            Vector3 lateral = direction.sqrMagnitude > 0.001f
                ? new Vector3(-direction.z, 0f, direction.x).normalized * lateralOffset
                : Vector3.zero;
            start += lateral;
            end += lateral;

            float altitude = AltitudeLayer.MoteOverhead.AltitudeFor();
            start.y = altitude;
            end.y = altitude;
            Vector3 midpoint = Vector3.Lerp(start, end, 0.5f);
            midpoint.z -= Mathf.Min(0.12f, Vector3.Distance(start, end) * 0.014f);

            GenDraw.DrawLineBetween(start, midpoint, WireMaterial, width);
            GenDraw.DrawLineBetween(midpoint, end, WireMaterial, width);
        }
}
internal static class Sniper
{
private static void DrawTrail(SniperSupportTrail trail)
        {
            if (trail == null || !trail.Valid)
            {
                return;
            }

            Vector3 start = trail.Source.ToVector3Shifted();
            Vector3 end = trail.Impact.ToVector3Shifted();
            start.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            end.y = start.y;
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.01f)
            {
                return;
            }

            Vector3 center = (start + end) * 0.5f;
            float angle = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            DrawTrailLayer(center, angle, length, Mathf.Lerp(0.18f, 0.07f, trail.Progress), HelodSniperSupportUtility.TrailGlowMaterial);
            DrawTrailLayer(center, angle, length, Mathf.Lerp(0.07f, 0.025f, trail.Progress), HelodSniperSupportUtility.TrailCoreMaterial);
        }
private static void DrawTrailLayer(Vector3 center, float angle, float length, float width, Material material)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(center, Quaternion.AngleAxis(angle, Vector3.up), new Vector3(width, 1f, length));
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }
private static void DrawAimIcon(SniperSupportEffect effect)
        {
            Thing target = effect.Target;
            Vector3 pos = target.DrawPos;
            pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            float progress = effect.Progress;
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            float angle = Mathf.Lerp(0f, 450f, eased);
            float scale = Mathf.Lerp(1.15f, 0.85f, Mathf.SmoothStep(0f, 1f, progress));
            Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.AngleAxis(angle, Vector3.up), Vector3.one * scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix, HelodSniperSupportUtility.AimMaterial, 0);
        }
}
internal static class Tether
{
private const float ActiveWireWidth=.026f;
internal static void Draw(ZaperX26TetherEffect effect)
{
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
if(!(bool)typeof(ZaperX26TetherEffect).GetMethod("ConnectionIsValid",flags).Invoke(effect,null))return;
var launcher=(Pawn)typeof(ZaperX26TetherEffect).GetField("launcher",flags).GetValue(effect);
var target=(Pawn)typeof(ZaperX26TetherEffect).GetField("target",flags).GetValue(effect);
Vector3 start=launcher.DrawPos;Vector3 end=target.DrawPos;Probe.DrawSaggingWire(start,end,-.045f,ActiveWireWidth);Probe.DrawSaggingWire(start,end,.045f,ActiveWireWidth);
}
}
}
