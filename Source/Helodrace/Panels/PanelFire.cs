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
            HelodGasDef smoke = DefDatabase<HelodGasDef>.GetNamedSilentFail("HD_PolymerFireSmoke");
            if (smoke == null) return;
            List<IntVec3> targets=EmissionCells(source);
            // A fixed pulse budget is divided among reachable air cells. A wall
            // with two exposed sides does not emit the full budget twice.
            foreach (IntVec3 cell in targets) HelodGasStore.AddGas(cell, source.Map, smoke, amount / targets.Count);
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

    public sealed class HelodGasWorker_PolymerSmoke : HelodGasWorker
    {
        public override void Apply(Pawn pawn,float density,float severityAtFullDensity)
        {
            if(pawn?.health==null || pawn.Dead || pawn.RaceProps?.IsFlesh!=true ||
                pawn.RaceProps.Humanlike && !GasUtility.IsAffectedByExposure(pawn)) return;
            float resistance=Math.Clamp(pawn.GetStatValue(StatDefOf.ToxicEnvironmentResistance),0,1);
            float gain=severityAtFullDensity*Math.Clamp(density,0,1)*(1-resistance);
            if(gain<=0)return;
            HediffDef def=DefDatabase<HediffDef>.GetNamedSilentFail("HD_PolymerSmokeInhalation");
            if(def==null)return;
            var exposure=pawn.health.hediffSet.GetFirstHediffOfDef(def) as Hediff_PolymerSmoke;
            if(exposure==null)
            {
                exposure=(Hediff_PolymerSmoke)HediffMaker.MakeHediff(def,pawn);
                exposure.Severity=MathF.Min(def.maxSeverity,gain);
                pawn.health.AddHediff(exposure);
                exposure.Touch();
            }
            else exposure.AddDose(gain);
            HealthUtility.AdjustSeverity(pawn,HediffDefOf.ToxicBuildup,gain*.25f*(1-Math.Clamp(pawn.GetStatValue(StatDefOf.ToxicResistance),0,1)));
        }
    }

    public sealed class Hediff_PolymerSmoke : HediffWithComps
    {
        private int lastExposure=-1;
        public void Touch() => lastExposure=Find.TickManager.TicksGame;
        public void AddDose(float amount) {Severity=MathF.Min(def.maxSeverity,Severity+amount);Touch();}
        public override bool ShouldRemove => base.ShouldRemove||Severity<=0;
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref lastExposure,"lastPolymerSmokeExposure",-1);}
        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if(Find.TickManager.TicksGame-lastExposure>=600) Severity=MathF.Max(0,Severity-.00004f*delta);
        }
    }
}
