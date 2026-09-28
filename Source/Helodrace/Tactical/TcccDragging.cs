using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    public sealed class TcccDragLink : IExposable
    {
        public Pawn carrier;
        public Pawn patient;
        public IntVec3 lastCarrierCell;
        public void ExposeData()
        {
            Scribe_References.Look(ref carrier, "carrier"); Scribe_References.Look(ref patient, "patient");
            Scribe_Values.Look(ref lastCarrierCell, "lastCarrierCell");
        }
    }

    [StaticConstructorOnStartup]
    public sealed class MapComponent_TcccDragging : MapComponent
    {
        private static readonly Material RopeOutline = SolidColorMaterials.SimpleSolidColorMaterial(new Color(.10f, .08f, .05f));
        private static readonly Material RopeCore = SolidColorMaterials.SimpleSolidColorMaterial(new Color(.85f, .70f, .36f));
        private List<TcccDragLink> links = new List<TcccDragLink>();
        public MapComponent_TcccDragging(Map map) : base(map) { }
        public bool IsDragging(Pawn pawn) => links.Any(l => l.carrier == pawn);
        public bool Attach(Pawn carrier, Pawn patient)
        {
            if (!TcccUtility.CanTreat(carrier, patient) || carrier == patient || !patient.Downed
                || !carrier.Position.AdjacentTo8WayOrInside(patient.Position)
                || links.Any(l => l.carrier == carrier || l.patient == patient || l.patient == carrier)) return false;
            links.Add(new TcccDragLink { carrier = carrier, patient = patient, lastCarrierCell = carrier.Position });
            carrier.health.AddHediff(DefDatabase<HediffDef>.GetNamed("HD_TCCC_Dragging"));
            return true;
        }
        public void Detach(Pawn carrier)
        {
            links.RemoveAll(l => l.carrier == carrier);
            if (carrier?.health != null) TcccUtility.RemoveEffect(carrier, "HD_TCCC_Dragging");
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Collections.Look(ref links, "tcccDragLinks", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && links == null) links = new List<TcccDragLink>();
        }
        public override void MapComponentTick()
        {
            for (int i = links.Count - 1; i >= 0; i--)
            {
                TcccDragLink link = links[i];
                Pawn carrier = link.carrier, patient = link.patient;
                if (!TcccUtility.CanTreat(carrier, patient) || !patient.Downed || carrier.Map != map
                    || !carrier.Position.AdjacentTo8WayOrInside(link.lastCarrierCell))
                { Detach(carrier); continue; }
                if (carrier.Position != link.lastCarrierCell)
                {
                    // Follow the cell the rescuer actually traversed, never shortcut through walls.
                    if (!CanSlide(patient.Position, link.lastCarrierCell)) { Detach(carrier); continue; }
                    patient.Position = link.lastCarrierCell;
                    patient.Notify_Teleported(false, false);
                    link.lastCarrierCell = carrier.Position;
                }
                else if (!carrier.Position.AdjacentTo8WayOrInside(patient.Position)) Detach(carrier);
            }
        }
        private bool CanSlide(IntVec3 from, IntVec3 to)
        {
            if (!to.InBounds(map) || !to.Walkable(map) || !from.AdjacentTo8WayOrInside(to)) return false;
            if (from.x != to.x && from.z != to.z
                && (!new IntVec3(from.x, 0, to.z).Walkable(map) || !new IntVec3(to.x, 0, from.z).Walkable(map))) return false;
            Building_Door door = to.GetEdifice(map) as Building_Door;
            return door == null || door.Open;
        }
        public override void MapComponentDraw()
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

    // Also removes an orphaned penalty when a link was invalidated by another mod or map transfer.
    public sealed class Hediff_TcccDragging : Hediff
    {
        public override bool ShouldRemove => pawn?.Map == null
            || !pawn.Map.GetComponent<MapComponent_TcccDragging>().IsDragging(pawn);
    }

    [HarmonyPatch(typeof(Verb), "get_WarmupTime")]
    public static class Patch_TcccDragWarmup
    {
        public static void Postfix(Verb __instance, ref float __result)
        {
            Pawn pawn = __instance.CasterPawn;
            if (pawn?.Map?.GetComponent<MapComponent_TcccDragging>().IsDragging(pawn) == true) __result *= 1.5f;
        }
    }

    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.DriverTick))]
    public static class Patch_TcccDragDelay
    {
        private sealed class DelayState { public int calls; public DelayState() { } }
        private static readonly ConditionalWeakTable<JobDriver, DelayState> States = new ConditionalWeakTable<JobDriver, DelayState>();
        private static readonly Func<JobDriver, Toil> CurrentToil = AccessTools.MethodDelegate<Func<JobDriver, Toil>>(
            AccessTools.PropertyGetter(typeof(JobDriver), "CurToil"));
        public static void Prefix(JobDriver __instance)
        {
            Pawn pawn = __instance.pawn;
            if (__instance.ticksLeftThisToil > 0
                && pawn?.Map?.GetComponent<MapComponent_TcccDragging>().IsDragging(pawn) == true
                && CurrentToil(__instance)?.defaultCompleteMode == ToilCompleteMode.Delay)
            {
                // Count actual driver calls rather than global tick modulo: tick throttling
                // can otherwise hit the same modulo forever and stall an interaction.
                DelayState state = States.GetOrCreateValue(__instance);
                if (++state.calls % 3 == 0) __instance.ticksLeftThisToil++;
            }
        }
    }
}
