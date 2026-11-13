using System.Collections.Generic;
using Helodrace.Tactics;
using UnityEngine;
using Verse;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace Helodrace.SquadAI
{
    // Developer view (View -> raid tactical plan nodes): enemy focus, each
    // pawn's post (green hold, yellow move, cyan flank) and the squad stage.
    public sealed partial class MapComponent_SquadAI
    {
        private bool OverlayEnabled => Prefs.DevMode && TacticalPlanOverlaySettings.drawTacticalPlanNodes && Find.CurrentMap == map;

        public override void MapComponentUpdate()
        {
            if (!OverlayEnabled) return;
            float altitude = AltitudeLayer.MetaOverlays.AltitudeFor();
            Vector3 At(IntVec3 cell) => new Vector3(cell.x + .5f, altitude, cell.z + .5f);
            foreach (Squad squad in squads)
            {
                if (squad.Stage == SquadStage.Vanilla) continue;
                if (squad.Focus.IsValid)
                {
                    Vector3 focus = At(squad.Focus);
                    GenDraw.DrawLineBetween((focus + new Vector3(-.5f, 0, -.5f)).ToGame(), (focus + new Vector3(.5f, 0, .5f)).ToGame(), SimpleColor.Red, .1f);
                    GenDraw.DrawLineBetween((focus + new Vector3(-.5f, 0, .5f)).ToGame(), (focus + new Vector3(.5f, 0, -.5f)).ToGame(), SimpleColor.Red, .1f);
                }
                foreach (SquadTeam team in squad.Teams)
                {
                    SimpleColor color = team.Task == TeamTask.Move ? SimpleColor.Yellow : team.Flank ? SimpleColor.Cyan : SimpleColor.Green;
                    foreach (KeyValuePair<Pawn, IntVec3> pair in team.Posts)
                        if (pair.Key.Spawned && pair.Key.Map == map)
                            GenDraw.DrawLineBetween(pair.Key.DrawPos, At(pair.Value).ToGame(), color, .05f);
                }
            }
        }

        public override void MapComponentOnGUI()
        {
            if (!OverlayEnabled || Event.current.type != EventType.Repaint) return;
            foreach (Squad squad in squads)
            {
                if (squad.Stage == SquadStage.Vanilla || squad.Live.Count == 0) continue;
                string label = squad.Unit.Label + ": " + ("HD_SquadAI_Stage_" + squad.Stage).Translate();
                foreach (SquadTeam team in squad.Teams)
                    if (team.Flank) label += " / " + "HD_SquadAI_Flank".Translate();
                Vector2 position = GenMapUI.LabelDrawPosFor(squad.Centroid()).ToNative(); position.Y -= 14f;
                GenMapUI.DrawThingLabel(position.ToGame(), label, SimpleColor.White.ToUnityColor());
            }
        }
    }
}
