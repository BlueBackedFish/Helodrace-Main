using System;
using System.Collections.Generic;
using System.Text;
using System.Numerics;
using Verse;
using Color = Waylith.Math.Color;
using Rect = Waylith.Math.Rect;

namespace Helodrace.Ballistics
{
    /// <summary>Optional presentation only. Never samples RNG or resolves another impact.</summary>
    public static class BallisticsDebugDisplay
    {
        private static RaidSettings Settings => HelodraceBase.Instance?.RaidOptions;
        public static bool ShowImpacts => Prefs.DevMode && (Settings?.ballisticImpactDebug ?? false);
        public static bool CaptureSpall => Prefs.DevMode && (Settings?.ballisticSpallDebug ?? false);
        public static bool Enabled => ShowImpacts || CaptureSpall;

        public static void Record(Thing target, ImpactTrace trace, Vector3 point, Vector3 incoming, Vector3 outgoing,
            float energyIn = float.NaN, float energyOut = float.NaN, List<SpallDebugRay> rays = null, Map map = null)
        {
            if (!Enabled || target == null || trace == null) return;
            if (!ShowImpacts && (rays == null || rays.Count == 0)) return;
            // A jet/interior detonation reports its full result first; its following exterior blast is the same shot.
            if (trace.Find("ExternalBlast") != null) return;
            map ??= target.MapHeld;
            if (map == null || map != Find.CurrentMap) return;
            map.GetComponent<MapComponent_BallisticDebug>().Add(target, trace, point, incoming, outgoing,
                energyIn, energyOut, rays, Math.Clamp(Settings.ballisticDebugSeconds, 1f, 20f));
        }

        internal static string Describe(Thing target, ImpactTrace trace, float energyIn, float energyOut,
            IReadOnlyList<SpallDebugRay> rays)
        {
            string result = ("HD_Ballistics_Result_" + trace.Result).Translate();
            string color = trace.Result == ImpactResult.Perforation || trace.Result == ImpactResult.Overmatch ? "FF7777"
                : trace.Result == ImpactResult.Ricochet ? "77CCFF" : "FFDD77";
            string projectile = DefDatabase<ThingDef>.GetNamedSilentFail(trace.ProjectileDefName)?.LabelCap
                ?? trace.ProjectileDefName;
            var text = new StringBuilder("HD_Ballistics_DebugHit".Translate(target.LabelShort, projectile,
                "<color=#" + color + ">" + result + "</color>"));
            string zone = trace.Find(ImpactStageIds.HitLocation)?.Note;
            if (!string.IsNullOrEmpty(zone))
                text.Append('\n').Append("HD_Ballistics_DebugZone".Translate(
                    DefDatabase<BodyPartDef>.GetNamedSilentFail(zone)?.label ?? zone));
            double? angle = null, surfaceAngle = null, fragments = null, jetIn = null, jetOut = null;
            bool hesh = false, blast = false;
            foreach (TraceStage stage in trace.Stages)
            {
                if (stage.StageId == ImpactStageIds.SecondaryGenerate)
                {
                    hesh |= stage.Note?.Contains("HESH scab", StringComparison.Ordinal) == true;
                    blast |= stage.Note?.Contains("interior detonation", StringComparison.Ordinal) == true;
                }
                foreach (TraceValue value in stage.Values)
                {
                    if (!angle.HasValue && (value.Key == "obliquityDeg" || value.Key.EndsWith(".obliquityDeg", StringComparison.Ordinal)))
                        angle = value.Value;
                    if (value.Key == "surfaceObliquityDeg") surfaceAngle = value.Value;
                    if (value.Key == "spallFragments" || value.Key == "fragments") fragments = value.Value;
                    if (value.Key.EndsWith(".potentialRhaMm", StringComparison.Ordinal)) jetIn = value.Value;
                    if (value.Key.EndsWith(".residualRhaMm", StringComparison.Ordinal)) jetOut = value.Value;
                }
            }
            angle ??= surfaceAngle;
            if (angle.HasValue) text.Append('\n').Append("HD_Ballistics_DebugAngle".Translate(angle.Value.ToString("0.0")));
            if (float.IsFinite(energyIn) && float.IsFinite(energyOut))
                text.Append('\n').Append("HD_Ballistics_DebugEnergy".Translate(energyIn.ToString("0"), energyOut.ToString("0")));
            if (jetIn.HasValue)
                text.Append('\n').Append("HD_Ballistics_DebugJet".Translate(jetIn.Value.ToString("0"), (jetOut ?? 0).ToString("0")));
            if (fragments.HasValue)
                text.Append('\n').Append((hesh ? "HD_Ballistics_DebugScabCount" : blast ? "HD_Ballistics_DebugCaseCount"
                    : "HD_Ballistics_DebugSpallCount").Translate(fragments.Value.ToString("0")));
            if (rays?.Count > 0)
                text.Append('\n').Append("HD_Ballistics_DebugRays".Translate(rays.Count, rays[0].Speed.ToString("0")));
            return text.ToString();
        }
    }

    /// <summary>Short-lived, bounded, map-local snapshots. Pausing the simulation does not freeze their lifetime.</summary>
    public sealed class MapComponent_BallisticDebug : MapComponent
    {
        private sealed class Hit
        {
            internal int Target;
            internal Vector3 Point, Incoming, Outgoing;
            internal ImpactResult Result;
            internal string Text;
            internal double Expires;
            internal readonly List<SpallDebugRay> Rays = new List<SpallDebugRay>();
        }

        private const int MaxHits = 32, MaxRaysPerHit = 32;
        private readonly List<Hit> hits = new List<Hit>();
        internal int Count => hits.Count;
        internal int RayCount { get { int count = 0; foreach (Hit hit in hits) count += hit.Rays.Count; return count; } }
        public MapComponent_BallisticDebug(Map map) : base(map) { }

        internal void Add(Thing target, ImpactTrace trace, Vector3 point, Vector3 incoming, Vector3 outgoing,
            float energyIn, float energyOut, List<SpallDebugRay> rays, float seconds)
        {
            Prune();
            if (hits.Count == MaxHits) hits.RemoveAt(0);
            var hit = new Hit { Target = target.thingIDNumber, Point = point, Incoming = incoming, Outgoing = outgoing,
                Result = trace.Result, Expires = Waylith.Time.RealtimeSinceStartupAsDouble + seconds,
                Text = BallisticsDebugDisplay.Describe(target, trace, energyIn, energyOut, rays) };
            if (BallisticsDebugDisplay.CaptureSpall && rays != null)
                for (int i = 0; i < Math.Min(MaxRaysPerHit, rays.Count); i++) hit.Rays.Add(rays[i]);
            hits.Add(hit);
        }

        private void Prune()
        {
            if (!BallisticsDebugDisplay.Enabled) { hits.Clear(); return; }
            double now = Waylith.Time.RealtimeSinceStartupAsDouble;
            hits.RemoveAll(hit => hit.Expires <= now);
        }

        public override void MapComponentUpdate() => Prune();

        public override void MapComponentOnGUI()
        {
            if (!BallisticsDebugDisplay.Enabled || Find.CurrentMap != map || Find.Camera == null) return;
            Prune();
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
                // Newest label per target; every retained shot still contributes its ray geometry.
                var labelled = new HashSet<int>();
                var labels = new List<Rect>();
                for (int i = hits.Count - 1; i >= 0; i--)
                {
                    Hit hit = hits[i];
                    if (BallisticsDebugDisplay.CaptureSpall)
                        foreach (SpallDebugRay ray in hit.Rays)
                        {
                            Color color = ray.CrewHit ? new Color(1f, 0.25f, 0.9f) : ray.Stopped
                                ? new Color(0.35f, 0.8f, 1f) : new Color(1f, 0.65f, 0.15f);
                            Line(ray.Start, ray.End, color, 1.5f);
                            if (ray.Stopped) Cross(ray.End, new Color(0.35f, 0.8f, 1f));
                            if (ray.CrewHit) Cross(ray.CrewPoint, new Color(1f, 0.25f, 0.9f));
                        }
                    if (!BallisticsDebugDisplay.ShowImpacts || !labelled.Add(hit.Target)) continue;
                    Vector2 anchor = Project(hit.Point);
                    if (anchor.X < 0 || anchor.X > UI.screenWidth || anchor.Y < 0 || anchor.Y > UI.screenHeight) continue;
                    Color impactColor = hit.Result == ImpactResult.Perforation || hit.Result == ImpactResult.Overmatch
                        ? new Color(1f, 0.35f, 0.35f) : new Color(1f, 0.85f, 0.25f);
                    Vector3 direction = hit.Incoming.LengthSquared() > 0f ? Vector3.Normalize(hit.Incoming) : Vector3.UnitZ;
                    Line(hit.Point - direction * 1.5f, hit.Point, new Color(0.9f, 0.9f, 0.9f), 2f);
                    if (hit.Outgoing.LengthSquared() > 0f && (hit.Result == ImpactResult.Perforation
                        || hit.Result == ImpactResult.Overmatch || hit.Result == ImpactResult.Ricochet))
                        Line(hit.Point, hit.Point + Vector3.Normalize(hit.Outgoing) * 1.5f, impactColor, 2f);
                    Cross(hit.Point, impactColor);
                    float width = Math.Min(310f, UI.screenWidth);
                    float height = Text.CalcHeight(hit.Text, width - 12f) + 8f;
                    var rect = new Rect(Math.Clamp(anchor.X + 12f, 0f, UI.screenWidth - width),
                        Math.Clamp(anchor.Y - height - 12f, 0f, Math.Max(0f, UI.screenHeight - height)), width, height);
                    // Keep neighbouring targets' summaries separate when there is room above them.
                    foreach (Rect occupied in labels)
                        if (rect.Overlaps(occupied)) rect.y = Math.Max(0f, occupied.y - rect.height - 4f);
                    labels.Add(rect);
                    Widgets.DrawBoxSolid(rect.ToGame(), new Color(0.04f, 0.04f, 0.04f, 0.9f).ToGame());
                    Widgets.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, rect.height - 8f).ToGame(), hit.Text);
                }
                if (hits.Count > 0 && BallisticsDebugDisplay.CaptureSpall)
                    Widgets.Label(new Rect(12f, UI.screenHeight - 100f, Math.Min(620f, UI.screenWidth - 24f), 60f).ToGame(),
                        "HD_Ballistics_DebugLegend".Translate());
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; }
        }

        // Verse UI projection/drawing contracts require game vectors/rects/colors. Geometry stays native above.
        private static Vector2 Project(Vector3 world) => UI.MapToUIPosition(world.ToGame()).ToNative();
        private static void Line(Vector3 start, Vector3 end, Color color, float width) =>
            Widgets.DrawLine(Project(start).ToGame(), Project(end).ToGame(), color.ToGame(), width);
        private static void Cross(Vector3 point, Color color)
        {
            Vector2 p = Project(point);
            Widgets.DrawLine((p - new Vector2(3f, 3f)).ToGame(), (p + new Vector2(3f, 3f)).ToGame(), color.ToGame(), 2f);
            Widgets.DrawLine((p + new Vector2(-3f, 3f)).ToGame(), (p + new Vector2(3f, -3f)).ToGame(), color.ToGame(), 2f);
        }
    }
}
