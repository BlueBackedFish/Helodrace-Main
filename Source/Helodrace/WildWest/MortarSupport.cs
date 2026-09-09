using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public static class HelodMortarSupportCombatBridge
    {
        public delegate bool ProjectileLaunchHandler(ThingDef projectileDef,
            Pawn launcher, IntVec3 source, IntVec3 impact, Map map);

        public static ProjectileLaunchHandler ExternalProjectileLauncher;

        public static bool TryLaunchProjectile(ThingDef projectileDef,
            Pawn launcher, IntVec3 source, IntVec3 impact, Map map)
        {
            ProjectileLaunchHandler handler = ExternalProjectileLauncher;
            if (handler == null)
            {
                return false;
            }

            try
            {
                return handler(projectileDef, launcher, source, impact, map);
            }
            catch (System.Exception exception)
            {
                Log.ErrorOnce("[Helodrace] External artillery projectile handler failed: "
                    + exception, 1976431203);
                return false;
            }
        }
    }

    public static class HelodMortarSupportUtility
    {
        public const int VolleyCount = 5;
        public const int ShellsPerVolley = 2;
        public const int VolleyIntervalTicks = 3 * 60;
        public const float ScatterRadius = 8f;
        public const float ChemicalScatterRadius = 14f;

        public static bool CanUseBase(Map map, HelodForwardBase forwardBase)
        {
            return CanUseBase(map, forwardBase, HelodForwardBaseService.InfantryMortarSupport);
        }

        public static bool CanUseBase(Map map, HelodForwardBase forwardBase,
            HelodForwardBaseService service)
        {
            if (map == null || forwardBase == null || !forwardBase.HasService(service)
                || !forwardBase.HasServiceCapacity(service)) return false;
            return Find.WorldGrid.ApproxDistanceInTiles(forwardBase.Tile, map.Tile)
                <= HelodForwardBaseServiceUtility.SupportRange(service);
        }

        public static void BeginTargeting(Map map, HelodForwardBase forwardBase, ThingDef shellDef,
            CompTelegraphTable telegraphComp = null, Pawn radioOperator = null,
            HelodForwardBaseService service = HelodForwardBaseService.InfantryMortarSupport)
        {
            if (radioOperator != null && SCR300RadioUtility.IsBlackout(map))
            {
                Messages.Message("HD_SCR300_SolarFlare".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (!CanUseBase(map, forwardBase, service) || shellDef?.projectileWhenLoaded == null)
            {
                Messages.Message("HD_MortarSupport_Unavailable".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            Find.WorldTargeter.StopTargeting();
            Find.World.renderer.wantedMode = WorldRenderMode.None;
            Current.Game.CurrentMap = map;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            CameraJumper.TryJump(new TargetInfo(map.Center, map));
            if (IsSmokeShell(shellDef))
            {
                map.GetComponent<MapComponent_HelodMortarSupport>().BeginSmokeLineTargeting(
                    forwardBase, shellDef, telegraphComp, radioOperator, service);
                Messages.Message("HD_MortarSupport_SmokeDragPrompt".Translate(), MessageTypeDefOf.NeutralEvent);
                return;
            }
            float radius = IsChemicalShell(shellDef) ? ChemicalScatterRadius : ScatterRadius;
            Find.Targeter.BeginTargeting(new TargetingParameters { canTargetLocations = true, validator = t => t.Cell.InBounds(map) && !t.Cell.Fogged(map) && SCR300RadioUtility.HasLineOfSight(radioOperator, map, t.Cell) },
                target => TryCall(map, target.Cell, forwardBase, shellDef, default(IntVec3), telegraphComp, radioOperator, service),
                target => { if (target.Cell.InBounds(map)) GenDraw.DrawRadiusRing(target.Cell, radius); });
            MapComponent_PersistentTargetingOverlay.Set(map, target =>
            {
                if (target.Cell.InBounds(map)) GenDraw.DrawRadiusRing(target.Cell, radius);
            });
        }

        public static bool TryCall(Map map, IntVec3 cell, HelodForwardBase forwardBase,
            ThingDef shellDef, IntVec3 lineEnd = default(IntVec3),
            CompTelegraphTable telegraphComp = null, Pawn radioOperator = null,
            HelodForwardBaseService service = HelodForwardBaseService.InfantryMortarSupport)
        {
            if (radioOperator != null && SCR300RadioUtility.IsBlackout(map))
            {
                Messages.Message("HD_SCR300_SolarFlare".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            if (!SCR300RadioUtility.HasLineOfSight(radioOperator, map, cell)
                || (lineEnd.IsValid && !SCR300RadioUtility.HasLineOfSight(radioOperator, map, lineEnd)))
            {
                Messages.Message("HD_SCR300_TargetNotVisible".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            if (!CanUseBase(map, forwardBase, service) || !cell.InBounds(map)) return false;
            if (telegraphComp != null && !telegraphComp.HasPrimaryCell)
            {
                Messages.Message("HD_TelegraphTable_ActionNeedsPrimaryCell".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            if (!forwardBase.TryConsumeAmmunitionSupport(
                service,
                shellDef,
                VolleyCount * ShellsPerVolley,
                out string reason))
            {
                Messages.Message(reason ?? "HD_MortarSupport_Unavailable".Translate().ToString(), MessageTypeDefOf.RejectInput);
                return false;
            }
            map.GetComponent<MapComponent_HelodMortarSupport>().QueueStrike(
                cell,
                lineEnd.IsValid ? lineEnd : cell,
                shellDef,
                forwardBase,
                radioOperator);
            telegraphComp?.ConsumePrimaryCell();
            Messages.Message("HD_MortarSupport_Called".Translate(VolleyCount * ShellsPerVolley), MessageTypeDefOf.NeutralEvent);
            return true;
        }

        public static bool IsSmokeShell(ThingDef shellDef) =>
            shellDef?.defName.IndexOf("WP", System.StringComparison.OrdinalIgnoreCase) >= 0;
        public static bool IsChemicalShell(ThingDef shellDef) =>
            shellDef?.defName.IndexOf("chemical", System.StringComparison.OrdinalIgnoreCase) >= 0
            || shellDef?.defName.EndsWith("BA") == true
            || shellDef?.defName.EndsWith("CN") == true;
    }

    public static class HelodW48SupportUtility
    {
        public static bool CanRequest(Map map, HelodForwardBase forwardBase)
        {
            return map != null
                && forwardBase != null
                && !forwardBase.HasPendingW48Order
                && forwardBase.HasService(HelodForwardBaseService.Artillery155mmSupport)
                && HelodMortarSupportUtility.CanUseBase(
                    map,
                    forwardBase,
                    HelodForwardBaseService.W48Support);
        }

        public static void BeginRequestTargeting(
            Map map,
            HelodForwardBase forwardBase,
            CompTelegraphTable telegraphComp,
            Pawn radioOperator = null)
        {
            if (radioOperator != null && SCR300RadioUtility.IsBlackout(map))
            {
                Messages.Message("HD_SCR300_SolarFlare".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            if (!CanRequest(map, forwardBase))
            {
                Messages.Message("HD_W48_Unavailable".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            if (telegraphComp != null && !telegraphComp.HasPrimaryCell)
            {
                Messages.Message("HD_TelegraphTable_ActionNeedsPrimaryCell".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WorldTargeter.StopTargeting();
            Find.World.renderer.wantedMode = WorldRenderMode.None;
            Current.Game.CurrentMap = map;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            CameraJumper.TryJump(new TargetInfo(map.Center, map));
            Find.Targeter.BeginTargeting(
                new TargetingParameters
                {
                    canTargetLocations = true,
                    validator = target => target.Cell.InBounds(map)
                },
                target => TryRequest(map, target.Cell, forwardBase, telegraphComp, radioOperator),
                target =>
                {
                    if (target.Cell.InBounds(map))
                    {
                        GenDraw.DrawRadiusRing(target.Cell, 25f);
                    }
                });
        }

        private static void TryRequest(
            Map map,
            IntVec3 targetCell,
            HelodForwardBase forwardBase,
            CompTelegraphTable telegraphComp,
            Pawn radioOperator)
        {
            if (radioOperator != null && SCR300RadioUtility.IsBlackout(map))
            {
                Messages.Message("HD_SCR300_SolarFlare".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            if (!forwardBase.TryRequestW48(map, targetCell, out string failReason))
            {
                Messages.Message(failReason ?? "HD_W48_Unavailable".Translate().ToString(), MessageTypeDefOf.RejectInput);
                return;
            }

            telegraphComp?.ConsumePrimaryCell();
            Messages.Message("HD_W48_Requested".Translate(
                HelodForwardBase.W48DeliveryHours,
                HelodForwardBase.W48AuthorizationHours), forwardBase, MessageTypeDefOf.CautionInput);
        }
    }

    public class MapComponent_HelodMortarSupport : MapComponent
    {
        private List<MortarSupportStrike> strikes = new List<MortarSupportStrike>();
        private HelodForwardBase lineTargetBase;
        private ThingDef lineTargetShell;
        private CompTelegraphTable lineTargetTelegraph;
        private Pawn lineTargetRadioOperator;
        private HelodForwardBaseService lineTargetService = HelodForwardBaseService.InfantryMortarSupport;
        private IntVec3 lineDragStart = IntVec3.Invalid;
        private IntVec3 lineDragEnd = IntVec3.Invalid;
        public MapComponent_HelodMortarSupport(Map map) : base(map) { }

        public void QueueStrike(
            IntVec3 center,
            IntVec3 lineEnd,
            ThingDef shellDef,
            HelodForwardBase forwardBase,
            Pawn caller,
            int volleyCount = HelodMortarSupportUtility.VolleyCount,
            int shellsPerVolley = HelodMortarSupportUtility.ShellsPerVolley,
            int volleyIntervalTicks = HelodMortarSupportUtility.VolleyIntervalTicks,
            float scatterRadius = HelodMortarSupportUtility.ScatterRadius)
        {
            strikes.Add(new MortarSupportStrike(
                center,
                lineEnd,
                shellDef,
                forwardBase,
                IncomingEdgeCell(forwardBase),
                Find.TickManager.TicksGame + 120,
                caller,
                volleyCount,
                shellsPerVolley,
                volleyIntervalTicks,
                scatterRadius));
        }

        public void BeginSmokeLineTargeting(HelodForwardBase forwardBase, ThingDef shellDef,
            CompTelegraphTable telegraphComp, Pawn radioOperator,
            HelodForwardBaseService service = HelodForwardBaseService.InfantryMortarSupport)
        {
            lineTargetBase = forwardBase; lineTargetShell = shellDef;
            lineTargetTelegraph = telegraphComp; lineTargetRadioOperator = radioOperator;
            lineTargetService = service;
            lineDragStart = IntVec3.Invalid; lineDragEnd = IntVec3.Invalid;
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (lineTargetShell == null) return;
            Event evt = Event.current;
            IntVec3 mouseCell = UI.MouseCell();
            if (evt.type == EventType.MouseDown && evt.button == 0 && mouseCell.InBounds(map))
            {
                lineDragStart = mouseCell; lineDragEnd = mouseCell; evt.Use();
            }
            else if (lineDragStart.IsValid && (evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && mouseCell.InBounds(map))
            {
                lineDragEnd = mouseCell;
                if (evt.type == EventType.MouseDrag) evt.Use();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0 && lineDragStart.IsValid)
            {
                IntVec3 start = lineDragStart; IntVec3 end = lineDragEnd.IsValid ? lineDragEnd : mouseCell;
                HelodForwardBase targetBase = lineTargetBase; ThingDef shell = lineTargetShell;
                CompTelegraphTable telegraph = lineTargetTelegraph;
                Pawn radioOperator = lineTargetRadioOperator;
                HelodForwardBaseService service = lineTargetService;
                CancelLineTargeting(); evt.Use();
                HelodMortarSupportUtility.TryCall(map, start, targetBase, shell, end, telegraph, radioOperator, service);
            }
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            if (lineTargetShell == null) return;
            IntVec3 mouseCell = UI.MouseCell();
            if (!lineDragStart.IsValid)
            {
                if (mouseCell.InBounds(map)) GenDraw.DrawRadiusRing(mouseCell, 2f);
                return;
            }
            IntVec3 end = lineDragEnd.IsValid ? lineDragEnd : mouseCell;
            GenDraw.DrawLineBetween(lineDragStart.ToVector3Shifted(), end.ToVector3Shifted());
            GenDraw.DrawRadiusRing(lineDragStart, 2f);
            if (end.InBounds(map)) GenDraw.DrawRadiusRing(end, 2f);
        }

        private void CancelLineTargeting() { lineTargetBase = null; lineTargetShell = null; lineTargetTelegraph = null; lineTargetRadioOperator = null; lineTargetService = HelodForwardBaseService.InfantryMortarSupport; lineDragStart = IntVec3.Invalid; lineDragEnd = IntVec3.Invalid; }

        private IntVec3 IncomingEdgeCell(HelodForwardBase forwardBase)
        {
            Vector3 target = Find.WorldGrid.GetTileCenter(map.Tile).normalized;
            Vector3 origin = Find.WorldGrid.GetTileCenter(forwardBase.Tile).normalized;
            Vector3 towardBase = Vector3.ProjectOnPlane(origin - target, target).normalized;
            Vector3 east = Vector3.Cross(Vector3.up, target).normalized;
            if (east.sqrMagnitude < 0.01f) east = Vector3.right;
            Vector3 north = Vector3.Cross(target, east).normalized;
            Vector2 direction = new Vector2(Vector3.Dot(towardBase, east), Vector3.Dot(towardBase, north)).normalized;
            if (direction.sqrMagnitude < 0.01f) direction = Vector2.up;
            float halfX = (map.Size.x - 1) * 0.5f;
            float halfZ = (map.Size.z - 1) * 0.5f;
            float scale = Mathf.Min(Mathf.Abs(direction.x) > 0.001f ? halfX / Mathf.Abs(direction.x) : float.MaxValue,
                Mathf.Abs(direction.y) > 0.001f ? halfZ / Mathf.Abs(direction.y) : float.MaxValue);
            return new IntVec3(Mathf.Clamp(Mathf.RoundToInt(halfX + direction.x * scale), 0, map.Size.x - 1), 0,
                Mathf.Clamp(Mathf.RoundToInt(halfZ + direction.y * scale), 0, map.Size.z - 1));
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int now = Find.TickManager.TicksGame;
            for (int i = strikes.Count - 1; i >= 0; i--)
            {
                MortarSupportStrike strike = strikes[i];
                if (now < strike.NextVolleyTick) continue;
                for (int shell = 0; shell < strike.ShellsPerVolley; shell++) FireShell(strike, shell);
                strike.VolleyFired();
                if (strike.Finished) strikes.RemoveAt(i);
            }
        }

        private void FireShell(MortarSupportStrike strike, int shellInVolley)
        {
            IntVec3 aim = strike.AimCellForNextShell(shellInVolley);
            float scatter = strike.IsSmokeLine ? 2f : strike.IsChemical ? 1.8f : strike.ScatterRadius;
            IntVec3 impact = CellFinder.RandomClosewalkCellNear(aim, map, Mathf.RoundToInt(scatter));
            IntVec3 source = strike.IncomingEdgeCell;
            if (!HelodMortarSupportCombatBridge.TryLaunchProjectile(
                strike.ProjectileDef, strike.Caller, source, impact, map))
            {
                Projectile projectile = (Projectile)GenSpawn.Spawn(
                    strike.ProjectileDef, source, map);
                projectile.Launch(
                    strike.Caller,
                    source.ToVector3Shifted(),
                    impact,
                    impact,
                    ProjectileHitFlags.All);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref strikes, "helodMortarSupportStrikes", LookMode.Deep);
            if (strikes == null) strikes = new List<MortarSupportStrike>();
        }
    }

    public class MortarSupportStrike : IExposable
    {
        private IntVec3 center;
        private IntVec3 lineEnd;
        private ThingDef shellDef;
        private ThingDef projectileDef;
        private HelodForwardBase forwardBase;
        private IntVec3 incomingEdgeCell;
        private int volleysFired;
        private int nextVolleyTick;
        private float patternRotation;
        private Pawn caller;
        private int volleyCount;
        private int shellsPerVolley;
        private int volleyIntervalTicks;
        private float scatterRadius;
        public IntVec3 Center => center;
        public ThingDef ProjectileDef => projectileDef;
        public bool IsSmokeLine => HelodMortarSupportUtility.IsSmokeShell(shellDef) && lineEnd != center;
        public bool IsChemical => HelodMortarSupportUtility.IsChemicalShell(shellDef);
        public int NextVolleyTick => nextVolleyTick;
        public IntVec3 IncomingEdgeCell => incomingEdgeCell;
        public bool Finished => volleysFired >= VolleyCount;
        public Pawn Caller => caller;
        public int VolleyCount => volleyCount > 0 ? volleyCount : HelodMortarSupportUtility.VolleyCount;
        public int ShellsPerVolley => shellsPerVolley > 0 ? shellsPerVolley : HelodMortarSupportUtility.ShellsPerVolley;
        public int VolleyIntervalTicks => volleyIntervalTicks > 0 ? volleyIntervalTicks : HelodMortarSupportUtility.VolleyIntervalTicks;
        public float ScatterRadius => scatterRadius > 0f ? scatterRadius : HelodMortarSupportUtility.ScatterRadius;
        public MortarSupportStrike() { }
        public MortarSupportStrike(
            IntVec3 center,
            IntVec3 lineEnd,
            ThingDef shellDef,
            HelodForwardBase forwardBase,
            IntVec3 incomingEdgeCell,
            int firstTick,
            Pawn caller,
            int volleyCount = HelodMortarSupportUtility.VolleyCount,
            int shellsPerVolley = HelodMortarSupportUtility.ShellsPerVolley,
            int volleyIntervalTicks = HelodMortarSupportUtility.VolleyIntervalTicks,
            float scatterRadius = HelodMortarSupportUtility.ScatterRadius)
        {
            this.center = center;
            this.lineEnd = lineEnd;
            this.shellDef = shellDef;
            projectileDef = shellDef.projectileWhenLoaded;
            this.forwardBase = forwardBase;
            this.incomingEdgeCell = incomingEdgeCell;
            nextVolleyTick = firstTick;
            patternRotation = Rand.Range(0f, 360f);
            this.caller = caller;
            this.volleyCount = volleyCount;
            this.shellsPerVolley = shellsPerVolley;
            this.volleyIntervalTicks = volleyIntervalTicks;
            this.scatterRadius = scatterRadius;
        }
        public IntVec3 AimCellForNextShell(int shellInVolley)
        {
            int shellIndex = volleysFired * ShellsPerVolley + shellInVolley;
            if (IsChemical)
            {
                int total = VolleyCount * ShellsPerVolley;
                float radius = Mathf.Sqrt((shellIndex + 0.5f) / total) * (HelodMortarSupportUtility.ChemicalScatterRadius - 1.5f);
                float angle = (patternRotation + shellIndex * 137.50777f) * Mathf.Deg2Rad;
                return new IntVec3(center.x + Mathf.RoundToInt(Mathf.Cos(angle) * radius), 0, center.z + Mathf.RoundToInt(Mathf.Sin(angle) * radius));
            }
            if (!IsSmokeLine) return center;
            float t = (shellIndex + Rand.Value) / Mathf.Max(1f, VolleyCount * ShellsPerVolley - 1f);
            return new IntVec3(Mathf.RoundToInt(Mathf.Lerp(center.x, lineEnd.x, t)), 0, Mathf.RoundToInt(Mathf.Lerp(center.z, lineEnd.z, t)));
        }
        public void VolleyFired() { volleysFired++; nextVolleyTick += VolleyIntervalTicks; }
        public void ExposeData() { Scribe_Values.Look(ref center, "center"); Scribe_Values.Look(ref lineEnd, "lineEnd"); Scribe_Defs.Look(ref shellDef, "shellDef"); Scribe_Defs.Look(ref projectileDef, "projectileDef"); Scribe_References.Look(ref forwardBase, "forwardBase"); Scribe_References.Look(ref caller, "caller"); Scribe_Values.Look(ref incomingEdgeCell, "incomingEdgeCell"); Scribe_Values.Look(ref volleysFired, "volleysFired"); Scribe_Values.Look(ref nextVolleyTick, "nextVolleyTick"); Scribe_Values.Look(ref patternRotation, "patternRotation", 0f); Scribe_Values.Look(ref volleyCount, "volleyCount", HelodMortarSupportUtility.VolleyCount); Scribe_Values.Look(ref shellsPerVolley, "shellsPerVolley", HelodMortarSupportUtility.ShellsPerVolley); Scribe_Values.Look(ref volleyIntervalTicks, "volleyIntervalTicks", HelodMortarSupportUtility.VolleyIntervalTicks); Scribe_Values.Look(ref scatterRadius, "scatterRadius", HelodMortarSupportUtility.ScatterRadius); }
    }
}
