using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Panels
{
    public enum PanelSlot { Insulation, Shielding, Reinforcement }

    public sealed class WallPanelProperties : DefModExtension
    {
        public PanelSlot slot;
        public float thermalResistance;
        public bool electromagneticShielding;
        public bool aramid;
        public float ignitionMultiplier = 1;
        public float flameMultiplier = 1;
        public Helodrace.Ballistics.ArmorMaterialDef armorMaterial;
        public float armorThicknessMeters;
        // Overlay for walls and doors other than the sandwich wall, which draw a
        // flat atlas; the def's own graphicData is the sandwich-wall overlay.
        public GraphicData otherHostGraphic;
    }

    // The wall itself is an ordinary linked map-mesh wall; the class marks the
    // sandwich construction for thermal resistance and panel overlays.
    public sealed class Building_SandwichPanelWall : Building { }

    // Doors do not carry the Wall link flag, so a linked wall normally ends
    // short of them. Sandwich walls run their connection arm into a door.
    [HarmonyPatch(typeof(Graphic_Linked), nameof(Graphic_Linked.ShouldLinkWith))]
    public static class SandwichWallDoorLink
    {
        static void Postfix(IntVec3 c, Thing parent, ref bool __result)
        {
            if (!__result && parent is Building_SandwichPanelWall && parent.Spawned && c.InBounds(parent.Map) &&
                c.GetEdifice(parent.Map) is Building_Door)
                __result = true;
        }
    }

    public static class PanelRules
    {
        public static bool IsHost(ThingDef def) => def?.building != null && !def.IsBlueprint && !def.IsFrame && def.size.x == 1 && def.size.z == 1 &&
            (def.building.isWall || typeof(Building_Door).IsAssignableFrom(def.thingClass));

        public static ThingDef BuiltDef(ThingDef def) => def?.entityDefToBuild as ThingDef ?? def;
        public static WallPanelProperties Properties(ThingDef def) => BuiltDef(def)?.GetModExtension<WallPanelProperties>();
        public static Building HostAt(IntVec3 pos, Map map) => pos.InBounds(map) && pos.GetEdifice(map) is Building b && IsHost(b.def) ? b : null;
        public static IntVec3 Front(IntVec3 pos, Rot4 face) => pos + GenAdj.CardinalDirections[face.AsInt];

        public static bool DoorFaceAllowed(Rot4 door, Rot4 face) => (door.AsInt & 1) == (face.AsInt & 1);
        // A lining is like wallpaper: furniture and machines may stand right in
        // front of it. Only a wall, door or rock against the face buries it.
        public static bool Buries(Thing edifice) => edifice != null &&
            (IsHost(BuiltDef(edifice.def)) || edifice.def.building?.isNaturalRock == true);
        public static bool FaceBuried(IntVec3 pos, Rot4 face, Map map)
        {
            IntVec3 front = Front(pos, face);
            return !front.InBounds(map) || Buries(front.GetEdifice(map));
        }
        // Blueprints and frames of panels anchored to this support.
        public static IEnumerable<Thing> Orders(Building host) => host.Position.GetThingList(host.Map)
            .Where(t => (t is Blueprint || t is Frame) && Properties(t.def) != null &&
                (t as ThingWithComps)?.GetComp<CompPanelAnchor>()?.host == host).ToArray();
        public static IEnumerable<Rot4> Faces(Building host) =>
            new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West }.Where(f => !(host is Building_Door door) || DoorFaceAllowed(door.Rotation, f));
        // Work from the face side when a pawn can stand there; otherwise any
        // touching cell, as with other buildings.
        public static bool WorkFromFront(Thing panelThing, Pawn pawn, out IntVec3 front)
        {
            front = Front(panelThing.Position, panelThing.Rotation);
            return panelThing.Spawned && front.InBounds(panelThing.Map) && front.Standable(panelThing.Map) &&
                pawn.CanReach(front, PathEndMode.OnCell, Danger.Some);
        }
        public static bool SameSlot(Rot4 faceA, PanelSlot slotA, Rot4 faceB, PanelSlot slotB) => faceA == faceB && slotA == slotB;

        public static AcceptanceReport Check(ThingDef panelDef, IntVec3 pos, Rot4 face, Map map, Thing ignore = null)
        {
            WallPanelProperties props = Properties(panelDef);
            if (props == null) return false;
            Building host = HostAt(pos, map);
            if (host == null) return "HD_Panel_NeedHost".Translate();
            if (host is Building_Door door && !DoorFaceAllowed(door.Rotation, face))
                return "HD_Panel_DoorFaces".Translate();
            IntVec3 front = Front(pos, face);
            if (FaceBuried(pos, face, map) || front.GetThingList(map).Any(t => IsHost(BuiltDef(t.def))))
                return "HD_Panel_BuriedFace".Translate();
            foreach (Thing other in pos.GetThingList(map))
            {
                if (other == ignore) continue;
                WallPanelProperties existing = Properties(other.def);
                if (existing != null && SameSlot(face, props.slot, other.Rotation, existing.slot))
                    return "HD_Panel_OccupiedSlot".Translate();
            }
            return true;
        }

        public static bool IsActive(Building_WallPanel panel) => panel.Spawned && panel.Host != null &&
            panel.Host.Spawned && panel.Host.Map == panel.Map && panel.Host.Position == panel.Position &&
            HostAt(panel.Position, panel.Map) == panel.Host &&
            !FaceBuried(panel.Position, panel.Rotation, panel.Map) &&
            !(panel.Host is Building_Door door && door.Open);
    }

    [HarmonyPatch]
    public static class PanelPreventBurying
    {
        static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(GenConstruct))
            .Where(m => m.Name == nameof(GenConstruct.CanPlaceBlueprintAt) || m.Name == "CanPlaceBlueprintAt_NewTemp");
        static void Postfix(BuildableDef __0, IntVec3 __1, Rot4 __2, Map __3, ref AcceptanceReport __result)
        {
            if (!__result || __0 is not ThingDef building || !PanelRules.IsHost(building)) return;
            foreach (IntVec3 cell in GenAdj.OccupiedRect(__1, __2, building.size))
                for (int d = 0; d < 4; d++)
                {
                    IntVec3 neighbor = cell + GenAdj.CardinalDirections[d];
                    if (!neighbor.InBounds(__3)) continue;
                    if (neighbor.GetThingList(__3).Any(t => PanelRules.Properties(t.def) != null && PanelRules.Front(t.Position, t.Rotation) == cell))
                    {
                        __result = "HD_Panel_RemoveBeforeWall".Translate();
                        return;
                    }
                }
        }
    }

    // This comp is also placed on the generated blueprint/frame definitions.
    // A reference is transferred through construction, so replacing a support at
    // the same cell never silently attaches an old construction order to it.
    public sealed class CompProperties_PanelAnchor : CompProperties
    {
        public CompProperties_PanelAnchor() { compClass = typeof(CompPanelAnchor); }
    }

    public sealed class CompPanelAnchor : ThingComp
    {
        public Building host;
        public bool anchored;
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad && !anchored)
            {
                host = PanelConstructionTransfer.InTransfer ? PanelConstructionTransfer.ExpectedHost : PanelRules.HostAt(parent.Position, parent.Map);
                anchored = true;
            }
        }
        public override void PostExposeData()
        {
            Scribe_References.Look(ref host, "panelSupport");
            Scribe_Values.Look(ref anchored, "panelAnchored");
        }
        public bool Valid => host != null && host.Spawned && parent.Spawned && host.Map == parent.Map &&
            host.Position == parent.Position && PanelRules.HostAt(parent.Position, parent.Map) == host &&
            !(host is Building_Door door && door.Open) &&
            (bool)PanelRules.Check(PanelRules.BuiltDef(parent.def), parent.Position, parent.Rotation, parent.Map, parent);
    }

    [HarmonyPatch(typeof(Blueprint_Build), "MakeSolidThing")]
    public static class PanelBlueprintAnchorTransfer
    {
        static void Postfix(Blueprint_Build __instance, Thing __result)
        {
            if (PanelRules.Properties(__instance.def) == null || __result is not ThingWithComps made) return;
            CompPanelAnchor anchor = made.GetComp<CompPanelAnchor>();
            if (anchor == null) return;
            anchor.host = __instance.GetComp<CompPanelAnchor>()?.host;
            anchor.anchored = true;
        }
    }

    [HarmonyPatch(typeof(RCellFinder), nameof(RCellFinder.TryFindGoodAdjacentSpotToTouch))]
    public static class PanelWorkingSide
    {
        static bool Prefix(Pawn __0, Thing __1, ref IntVec3 __2, ref bool __result)
        {
            if (PanelRules.Properties(__1?.def) == null || !PanelRules.WorkFromFront(__1, __0, out IntVec3 front)) return true;
            __2 = front;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Toils_Goto), nameof(Toils_Goto.GotoThing), new[] {typeof(TargetIndex), typeof(PathEndMode), typeof(bool)})]
    public static class PanelTouchWorkSide
    {
        static void Postfix(TargetIndex __0, Toil __result)
        {
            Action original = __result.initAction;
            __result.initAction = () =>
            {
                Pawn pawn = __result.actor;
                Thing target = pawn.CurJob?.GetTarget(__0).Thing;
                if (PanelRules.Properties(target?.def) == null || !PanelRules.WorkFromFront(target, pawn, out IntVec3 front)) { original?.Invoke(); return; }
                pawn.pather.StartPath(front, PathEndMode.OnCell);
            };
        }
    }

    public sealed class PlaceWorker_WallPanel : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot,
            Map map, Thing thingToIgnore = null, Thing thing = null)
            => PanelRules.Check((ThingDef)checkingDef, loc, rot, map, thingToIgnore);
    }

    public sealed class Building_WallPanel : Building
    {
        public Building Host => GetComp<CompPanelAnchor>()?.host;
        public WallPanelProperties Properties => def.GetModExtension<WallPanelProperties>();

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            map.GetComponent<MapComponent_WallPanels>().Register(this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map?.GetComponent<MapComponent_WallPanels>().Unregister(this);
            base.DeSpawn(mode);
        }

        // Actual override contract in 1.7 still uses Unity's compatibility value.
        protected override void DrawAt(UnityEngine.Vector3 drawLoc, bool flip = false)
        {
            if (!PanelRules.IsActive(this)) return;
            Graphic overlay = Host is Building_SandwichPanelWall ? Graphic : Properties.otherHostGraphic?.Graphic ?? Graphic;
            overlay.Draw(drawLoc, Rotation, this);
        }

        public override string GetInspectString()
        {
            string state = Host == null || !Host.Spawned ? "HD_Panel_SupportLost" :
                PanelRules.IsActive(this) ? "HD_Panel_Attached" : "HD_Panel_Inactive";
            return PanelText.AppendLine(PanelText.AppendLine(base.GetInspectString(), state.Translate()),
                "HD_Panel_Face".Translate() + ": " + Rotation.ToStringWord());
        }
    }

    public sealed class MapComponent_WallPanels : MapComponent
    {
        private readonly Dictionary<int, List<Building_WallPanel>> byHost = new();
        public MapComponent_WallPanels(Map map) : base(map) { }
        public void Register(Building_WallPanel panel)
        {
            Building host = panel.Host;
            if (host == null) return;
            if (!byHost.TryGetValue(host.thingIDNumber, out var list))
                byHost[host.thingIDNumber] = list = new();
            if (!list.Contains(panel)) list.Add(panel);
            PanelShielding.Invalidate(map,host);
        }
        public void Unregister(Building_WallPanel panel)
        {
            if (panel.Host != null && byHost.TryGetValue(panel.Host.thingIDNumber, out var list))
            {
                list.Remove(panel);
                if (list.Count == 0) byHost.Remove(panel.Host.thingIDNumber);
            }
            PanelShielding.Invalidate(map,panel.Host);
        }
        public Building_WallPanel[] For(Building host) => host != null && byHost.TryGetValue(host.thingIDNumber, out var list)
            ? list.ToArray() : Array.Empty<Building_WallPanel>();

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            byHost.Clear();
            foreach (var panel in map.listerThings.AllThings.OfType<Building_WallPanel>()) Register(panel);
        }
        public void SupportRemoved(Building host, DestroyMode mode)
        {
            // Deconstructing the support takes its attachments down with the
            // usual deconstruction return; any other loss destroys them.
            DestroyMode panelMode = mode == DestroyMode.Deconstruct || mode == DestroyMode.Refund ? mode : DestroyMode.KillFinalize;
            foreach (Building_WallPanel panel in For(host))
                if (panel.Spawned) panel.Destroy(panelMode);
            byHost.Remove(host.thingIDNumber);
            // Orders anchored to this support can never become valid again.
            // Cancel them so delivered materials come back and the cell is free.
            foreach (Thing order in PanelRules.Orders(host))
                if (order.Spawned) order.Destroy(DestroyMode.Cancel);
        }
    }

    [StaticConstructorOnStartup]
    public static class PanelConstructionSetup
    {
        static PanelConstructionSetup()
        {
            // Generated defs are available at this stage, before saves or maps spawn.
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                if (PanelRules.Properties(def) != null)
                {
                    def.comps ??= new List<CompProperties>();
                    if (!def.comps.Any(c => c.compClass == typeof(CompPanelAnchor)))
                        def.comps.Add(new CompProperties_PanelAnchor());
                }
            StatDef flammability = StatDefOf.Flammability;
            flammability.parts ??= new List<StatPart>();
            if (!flammability.parts.OfType<StatPart_SpentPolymerCore>().Any())
                flammability.parts.Add(new StatPart_SpentPolymerCore { parentStat = flammability });
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.GetGizmos))]
    public static class PanelHostSelection
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, ThingWithComps __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            if (__instance is not Building host || !host.Spawned || !PanelRules.IsHost(host.def)) yield break;
            ThingDef first = host.Map.GetComponent<MapComponent_WallPanels>().For(host).Select(p => p.def)
                .Concat(PanelRules.Orders(host).Select(o => PanelRules.BuiltDef(o.def))).FirstOrDefault();
            if (first == null) yield break;
            yield return new Command_Action
            {
                defaultLabel = "HD_Panel_Layers".Translate(),
                defaultDesc = "HD_Panel_LayersDesc".Translate(),
                icon = first.uiIcon,
                groupable = false,
                action = () => Find.WindowStack.Add(new Dialog_WallPanelLayers(host))
            };
        }
    }

    [HarmonyPatch]
    public static class PanelConstructionTransfer
    {
        [ThreadStatic] private static Stack<Building> hosts;
        public static bool InTransfer => hosts?.Count > 0;
        public static Building ExpectedHost => InTransfer ? hosts.Peek() : null;
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Blueprint_Build), "MakeSolidThing");
            yield return AccessTools.Method(typeof(Frame), nameof(Frame.CompleteConstruction));
        }
        static void Prefix(Thing __instance, out bool __state)
        {
            __state = PanelRules.Properties(__instance.def) != null;
            if (__state) (hosts ??= new Stack<Building>()).Push((__instance as ThingWithComps)?.GetComp<CompPanelAnchor>()?.host);
        }
        static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) hosts.Pop();
            return __exception;
        }
    }

    // Scope both exceptions to our non-edifice attachments, never all buildings.
    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintOver))]
    public static class PanelPlacementCoexistence
    {
        static void Postfix(BuildableDef newDef, ThingDef oldDef, ref bool __result)
        {
            if (newDef is ThingDef nd && PanelRules.Properties(nd) != null &&
                (PanelRules.IsHost(PanelRules.BuiltDef(oldDef)) || PanelRules.Properties(oldDef) != null)) __result = true;
        }
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes))]
    public static class PanelSpawnCoexistence
    {
        static void Postfix(BuildableDef newEntDef, BuildableDef oldEntDef, ref bool __result)
        {
            if (newEntDef is not ThingDef nd || oldEntDef is not ThingDef od) return;
            // Keep support/panels when spawning a panel, frame or blueprint.
            if (PanelRules.Properties(nd) != null && (PanelRules.IsHost(PanelRules.BuiltDef(od)) || PanelRules.Properties(od) != null)) __result = false;
            // Support rebuilding must not wipe adjacent-slot construction entities.
            else if (PanelRules.IsHost(PanelRules.BuiltDef(nd)) && PanelRules.Properties(od) != null) __result = false;
        }
    }

    [HarmonyPatch]
    public static class PanelConstructionValidity
    {
        static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(GenConstruct))
            .Where(m => m.Name == nameof(GenConstruct.CanConstruct));
        static void Postfix(Thing t, ref bool __result)
        {
            if (__result && PanelRules.Properties(t.def) != null)
                __result = (t as ThingWithComps)?.GetComp<CompPanelAnchor>()?.Valid == true;
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.DeSpawn))]
    public static class PanelSupportRemoved
    {
        static void Prefix(Building __instance, DestroyMode mode)
        {
            if (__instance.Spawned && PanelRules.IsHost(__instance.def))
                __instance.Map.GetComponent<MapComponent_WallPanels>().SupportRemoved(__instance, mode);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class PanelDirectionalDurability
    {
        static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            if (__instance is Building host && host.Spawned && PanelRules.IsHost(host.def) && dinfo.Amount > 0)
                PanelCombat.Resolve(host, ref dinfo);
        }
    }
}
