using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Panels
{
    public sealed class CompProperties_PolymerCore : CompProperties
    {
        public int burnTicks = 1800;
        public float totalSmoke = 24;
        public CompProperties_PolymerCore() { compClass = typeof(CompPolymerCore); }
    }

    public sealed class CompPolymerCore : ThingComp
    {
        private float remaining = 1;
        public float FractionRemaining => remaining;
        public CompProperties_PolymerCore Props => (CompProperties_PolymerCore)props;
        public override void PostExposeData() => Scribe_Values.Look(ref remaining, "polymerCoreRemaining", 1f);
        public override void PostSplitOff(Thing piece)
        {
            if(piece.TryGetComp<CompPolymerCore>() is CompPolymerCore core) core.remaining=remaining;
        }
        public override void PreAbsorbStack(Thing otherStack,int count)
        {
            if(otherStack.TryGetComp<CompPolymerCore>() is CompPolymerCore core && parent.stackCount+count>0)
                remaining=(remaining*parent.stackCount+core.remaining*count)/(parent.stackCount+count);
        }

        public static float Consume(ref float remaining, int elapsed, int burnTicks)
        {
            float amount = MathF.Min(Math.Clamp(remaining, 0, 1), Math.Max(0, elapsed) / (float)Math.Max(1, burnTicks));
            remaining = MathF.Max(0, remaining - amount);
            return amount;
        }
        public override void CompTickRare()
        {
            if (!parent.Spawned || remaining <= 0) return;
            bool burning = parent.IsBurning();
            if (!burning && parent.AmbientTemperature >= 300f)
                burning = FireUtility.TryStartFireIn(parent.Position, parent.Map, .1f, parent);
            if (!burning) return;
            float burned = Consume(ref remaining, 250, Props.burnTicks);
            Emit(parent, burned * Props.totalSmoke * parent.stackCount);
        }
        public static void Emit(Thing source, float amount)
        {
            if (amount <= 0 || source?.Spawned != true) return;
            List<IntVec3> targets=EmissionCells(source);
            if (targets.Count == 0) return;
            // Burning polystyrene releases Biotech tox gas. One normalized unit is
            // one full cell of gas (255 grid density). A fixed pulse budget is
            // divided among reachable air cells, so a wall with two exposed sides
            // does not emit the full budget twice.
            int perCell = (int)MathF.Round(amount * 255f / targets.Count);
            if (perCell <= 0) return;
            foreach (IntVec3 cell in targets) source.Map.gasGrid.AddGas(cell, GasType.ToxGas, perCell, true);
        }
        public static List<IntVec3> EmissionCells(Thing source)
        {
            var targets = new List<IntVec3>();
            if(source?.Spawned!=true)return targets;
            var seen = new HashSet<IntVec3>();
            var queue = new Queue<IntVec3>();
            if (source is Building_WallPanel panel) queue.Enqueue(PanelRules.Front(source.Position, panel.Rotation));
            else foreach (IntVec3 direction in GenAdj.CardinalDirections) queue.Enqueue(source.Position + direction);
            while (queue.Count > 0)
            {
                IntVec3 cell = queue.Dequeue();
                if (!seen.Add(cell) || Math.Abs(cell.x-source.Position.x)+Math.Abs(cell.z-source.Position.z)>2 ||
                    !HelodGasStore.GasCanMoveTo(cell, source.Map)) continue;
                targets.Add(cell);
                foreach (IntVec3 d in GenAdj.CardinalDirections) queue.Enqueue(cell+d);
            }
            return targets;
        }
        public override string CompInspectStringExtra() => "HD_Panel_CoreRemaining".Translate((remaining * 100f).ToString("F0"));
    }

    public static class PanelSurface
    {
        public static Rot4 FaceFromDirection(float incomingDegrees)
        {
            float rad = incomingDegrees * (MathF.PI/180f);
            float x=-MathF.Sin(rad), z=-MathF.Cos(rad);
            return MathF.Abs(x)>MathF.Abs(z) ? (x>0?Rot4.East:Rot4.West) : (z>0?Rot4.North:Rot4.South);
        }
        public static Rot4 FaceToward(IntVec3 center, IntVec3 source)
        {
            int x=source.x-center.x,z=source.z-center.z;
            return Math.Abs(x)>Math.Abs(z)?(x>0?Rot4.East:Rot4.West):(z>0?Rot4.North:Rot4.South);
        }
        public static float FaceFactor(Building host, Rot4 face, bool flame)
        {
            float factor=1;
            foreach (Building_WallPanel p in host.Map.GetComponent<MapComponent_WallPanels>().For(host))
                if (p.Rotation==face && PanelRules.IsActive(p) && (p.GetComp<CompPolymerCore>()?.FractionRemaining ?? 1)>0)
                    factor *= flame?p.Properties.flameMultiplier:p.Properties.ignitionMultiplier;
            return Math.Clamp(factor,0,flame?1:1.5f);
        }
        public static float FireFactor(Building host, bool flame, IntVec3? source = null, float angle = -1)
        {
            if (source.HasValue && source.Value != host.Position) return FaceFactor(host,FaceToward(host.Position,source.Value),flame);
            if (angle>=0) return FaceFactor(host,FaceFromDirection(angle),flame);
            // Unlocated ignition/heat is reduced only as far as the least
            // protected exposed face. A one-sided lining cannot fireproof wood.
            float worst=0;bool any=false;
            for(int d=0;d<4;d++)
            {
                IntVec3 air=host.Position+GenAdj.CardinalDirections[d];
                if(!air.InBounds(host.Map)||air.GetEdifice(host.Map)!=null)continue;
                worst=MathF.Max(worst,FaceFactor(host,new Rot4(d),flame));any=true;
            }
            return any?worst:1;
        }
        public static IntVec3? CurrentFireSource => PanelFireSource.Current?.Position;
        public static float IgnitionFactor(Building host)
        {
            if(CurrentFireSource.HasValue)return FireFactor(host,false,CurrentFireSource);
            PanelShot shot=PanelBulletScope.Current;
            return shot?.host==host?FaceFactor(host,shot.face,false):FireFactor(host,false);
        }
    }

    [HarmonyPatch]
    public static class PanelFireSource
    {
        [ThreadStatic] private static Stack<(Fire fire, bool spread)> sources;
        public static Fire Current => sources?.Count>0?sources.Peek().fire:null;
        public static bool Spreading => sources?.Count>0&&sources.Peek().spread;
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Fire),"TrySpread");
            yield return AccessTools.Method(typeof(Fire),"DoFireDamage");
        }
        static void Prefix(Fire __instance, MethodBase __originalMethod) =>
            (sources??=new()).Push((__instance,__originalMethod.Name=="TrySpread"));
        static Exception Finalizer(Exception __exception) {sources.Pop();return __exception;}
    }

    [HarmonyPatch(typeof(FireUtility),nameof(FireUtility.ChanceToStartFireIn))]
    public static class PanelDirectionalIgnitionChance
    {
        static void Postfix(IntVec3 c, Map map, ref float __result)
        {
            if(__result>0 && PanelRules.HostAt(c,map) is Building host)
                __result=Math.Clamp(__result*PanelSurface.IgnitionFactor(host),0,1);
        }
    }

    [HarmonyPatch(typeof(FireUtility),nameof(FireUtility.TryStartFireIn))]
    public static class PanelDirectIgnition
    {
        static bool Prefix(IntVec3 c, Map map, ref bool __result)
        {
            // TrySpread already rolled its adjusted chance; do not roll twice.
            if(PanelFireSource.Spreading || PanelRules.HostAt(c,map) is not Building host) return true;
            float factor=PanelSurface.IgnitionFactor(host);
            if(factor>=1 || Rand.Chance(factor))return true;
            __result=false;return false;
        }
    }

    // Added to the Flammability stat at startup instead of patching every
    // GetStatValue call in the game.
    public sealed class StatPart_SpentPolymerCore : StatPart
    {
        private static bool Spent(StatRequest req) =>
            req.Thing is ThingWithComps thing && thing.GetComp<CompPolymerCore>() is CompPolymerCore core && core.FractionRemaining <= 0;
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (Spent(req)) val = 0;
        }
        public override string ExplanationPart(StatRequest req) => Spent(req) ? "HD_Panel_CoreSpent".Translate() : null;
    }
}
