// Frozen from 9066b886; original helicopter visual and medevac dialog geometry.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
namespace Helodrace.Tests.Frozen
{
    public sealed class Dialog_HelodMedevac : Window
    {
        private readonly Map map;
        private readonly Action<List<Pawn>> accept;
        private readonly List<Pawn> selected = new List<Pawn>();
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(520f, 550f);
        public Dialog_HelodMedevac(Map map, Action<List<Pawn>> accept)
        {
            this.map = map; this.accept = accept;
            forcePause = true; doCloseX = true; closeOnClickedOutside = true; absorbInputAroundWindow = true;
        }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width, 55), "HD_Heli_SelectPatients".Translate(selected.Count));
            var pawns = map.mapPawns.AllPawnsSpawned.Where(p => HelodHelicopterSupport.AvailablePatient(p, map)).ToList();
            selected.RemoveAll(p => !pawns.Contains(p));
            Rect list = new Rect(0, 60, rect.width, rect.height - 115);
            Widgets.BeginScrollView(list, ref scroll, new Rect(0, 0, list.width - 18, pawns.Count * 34));
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                bool value = selected.Contains(pawn), previous = value;
                Widgets.CheckboxLabeled(new Rect(0, i * 34, list.width - 22, 30), pawn.LabelShortCap
                    + (Tactical.TcccUtility.MistReady(pawn) ? " (MIST)" : ""), ref value);
                if (Tactical.TcccUtility.MistReady(pawn))
                    TooltipHandler.TipRegion(new Rect(0, i * 34, list.width - 22, 30),
                        Tactical.TcccUtility.Effect(pawn, "HD_TCCC_Mist").report);
                if (value && !previous && selected.Count < 3) selected.Add(pawn);
                if (!value) selected.Remove(pawn);
            }
            Widgets.EndScrollView();
            if (Widgets.ButtonText(new Rect(0, rect.height - 42, rect.width, 38), "HD_Heli_ChooseDZ".Translate())
                && selected.Count > 0)
            {
                Close(); accept(new List<Pawn>(selected));
            }
        }
    }

    internal sealed class SupportHelicopterVisual
    {
        private static readonly Material[] BodyMaterials = { AircraftMaterial("DoorOutline"), AircraftMaterial("Base"),
            AircraftMaterial("DoorFront"), AircraftMaterial("WingFly") };
        private static readonly Material[] RotorMaterials = { AircraftMaterial("1Fly"), AircraftMaterial("2Fly"),
            AircraftMaterial("3Fly"), AircraftMaterial("4Fly") };
        private static Material AircraftMaterial(string suffix) => MaterialPool.MatFrom(
            "Effects/Aircraft/MH60M/HD_MH60M_" + suffix, ShaderDatabase.Transparent);
        private enum FlightPhase { Arriving, Loading, LeavingForBase, AtBase, Returning, Unloading, Leaving }
        private FlightPhase phase;
        private int phaseTicks;
        private bool mistDispatch;
        private const int FlightTicks = 240;
        internal SupportHelicopterVisual(int phase, int ticks, bool mist)
        { this.phase = (FlightPhase)phase; phaseTicks = ticks; mistDispatch = mist; }
        internal void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float progress = Mathf.Clamp01(phaseTicks / (float)(phase == FlightPhase.Arriving && mistDispatch ? FlightTicks * 3 / 4 : FlightTicks));
            bool arriving = phase == FlightPhase.Arriving || phase == FlightPhase.Returning;
            bool leaving = phase == FlightPhase.Leaving || phase == FlightPhase.LeavingForBase;
            float height = arriving ? (1f - Mathf.SmoothStep(0, 1, progress)) : leaving ? Mathf.SmoothStep(0, 1, progress) : 0f;
            drawLoc.z += height * 18f;
            drawLoc.x += arriving ? -height * 12f : height * 12f;
            float doorOffset = (phase == FlightPhase.Loading || phase == FlightPhase.Unloading)
                ? .45f * Mathf.SmoothStep(0f, 1f, phaseTicks / 60f) : 0f;
            for (int i = 0; i < 5; i++)
            {
                Vector3 partPosition = drawLoc;
                partPosition.y = AltitudeLayer.MoteOverhead.AltitudeFor() + i * .01f;
                if (i == 0 || i == 2) partPosition.x -= doorOffset;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(partPosition, Quaternion.identity,
                    new Vector3(5.2f, 1, 5.2f)), i < 4 ? BodyMaterials[i]
                        : RotorMaterials[Find.TickManager.TicksGame / 2 % 4], 0);
            }
        }
    }
}
