using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Aircraft
{
    public sealed class ITab_AircraftStatus : ITab
    {
        private Vector2 scrollPosition;

        public ITab_AircraftStatus()
        {
            size = new Vector2(480f, 460f);
            labelKey = "HD_ITab_Aircraft_Title";
        }

        public override bool IsVisible => SelectedAircraft != null;

        private AircraftThing SelectedAircraft => Find.Selector.SingleSelectedThing as AircraftThing;

        protected override void FillTab()
        {
            AircraftThing aircraft = SelectedAircraft;
            Rect outRect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            if (aircraft == null)
            {
                Widgets.Label(outRect, "HD_ITab_Aircraft_NoAircraft".Translate().Resolve());
                return;
            }

            CompAircraftManifest manifest = aircraft.Manifest;
            CompAircraftBombBay bombBay = aircraft.TryGetComp<CompAircraftBombBay>();
            CompAircraftTransporter transporter = aircraft.TryGetComp<CompAircraftTransporter>();
            CompRefuelable fuel = aircraft.FuelComp;
            int passengerRows = manifest?.Passengers.Count() ?? 0;
            int cargoRows = transporter?.SearchableContents.Count ?? 0;
            int bombRows = bombBay?.LoadedBombStacks.GroupBy(thing => thing.def).Count() ?? 0;
            float viewHeight = 250f + (passengerRows + cargoRows + bombRows) * 26f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, viewHeight));

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect, true);
            float y = 0f;
            DrawHeader(aircraft.LabelCap, viewRect.width, ref y);
            DrawLine("HD_ITab_Aircraft_FlightState".Translate(),
                ("HD_Aircraft_State_" + aircraft.FlightState).Translate(), viewRect.width, ref y);
            DrawLine("HD_ITab_Aircraft_Altitude".Translate(), aircraft.Altitude.ToString(), viewRect.width, ref y);
            DrawLine("HD_ITab_Aircraft_Speed".Translate(), aircraft.CurrentSpeed.ToString("0.###"), viewRect.width, ref y);
            if (fuel != null)
            {
                DrawLine("HD_ITab_Aircraft_Fuel".Translate(),
                    fuel.Fuel.ToString("0.#") + " / " + fuel.TargetFuelLevel.ToString("0.#"), viewRect.width, ref y);
            }

            DrawSection("HD_ITab_Aircraft_Occupants".Translate(), viewRect.width, ref y);
            if (manifest == null)
            {
                DrawPlain("HD_ITab_Aircraft_None".Translate(), viewRect.width, ref y);
            }
            else
            {
                DrawLine("HD_ITab_Aircraft_Pilot".Translate(),
                    manifest.Pilot?.LabelShortCap ?? "HD_ITab_Aircraft_None".Translate(), viewRect.width, ref y);
                foreach (Pawn passenger in manifest.Passengers)
                    DrawLine("HD_ITab_Aircraft_Passenger".Translate(), passenger.LabelShortCap, viewRect.width, ref y);
                if (!manifest.Passengers.Any())
                    DrawLine("HD_ITab_Aircraft_Passenger".Translate(), "HD_ITab_Aircraft_None".Translate(), viewRect.width, ref y);
            }

            DrawSection("HD_ITab_Aircraft_Cargo".Translate(), viewRect.width, ref y);
            if (transporter == null || transporter.SearchableContents.Count == 0)
            {
                DrawPlain("HD_ITab_Aircraft_None".Translate(), viewRect.width, ref y);
            }
            else
            {
                foreach (Thing thing in transporter.SearchableContents)
                    DrawLine(thing.LabelCap, "x" + thing.stackCount, viewRect.width, ref y);
            }

            DrawSection("HD_ITab_Aircraft_Bombs".Translate(), viewRect.width, ref y);
            if (bombBay == null || !bombBay.LoadedBombStacks.Any())
            {
                DrawPlain("HD_ITab_Aircraft_None".Translate(), viewRect.width, ref y);
            }
            else
            {
                foreach (IGrouping<ThingDef, Thing> group in bombBay.LoadedBombStacks.GroupBy(thing => thing.def))
                    DrawLine(group.Key.LabelCap, "x" + group.Sum(thing => thing.stackCount), viewRect.width, ref y);
            }

            Widgets.EndScrollView();
        }

        private static void DrawHeader(string text, float width, ref float y)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, width, 32f), text);
            Text.Font = GameFont.Small;
            y += 36f;
        }

        private static void DrawSection(string text, float width, ref float y)
        {
            y += 8f;
            Widgets.DrawLineHorizontal(0f, y, width);
            y += 5f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, width, 28f), text);
            Text.Font = GameFont.Small;
            y += 30f;
        }

        private static void DrawLine(string label, string value, float width, ref float y)
        {
            Widgets.Label(new Rect(0f, y, width * 0.48f, 24f), label);
            Widgets.Label(new Rect(width * 0.5f, y, width * 0.5f, 24f), value);
            y += 26f;
        }

        private static void DrawPlain(string value, float width, ref float y)
        {
            Widgets.Label(new Rect(0f, y, width, 24f), value);
            y += 26f;
        }
    }
}
