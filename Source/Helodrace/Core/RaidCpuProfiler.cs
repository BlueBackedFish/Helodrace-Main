using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Helodrace
{
    internal enum RaidCpuStage { Planning, Observation, LocalMap, Formation, Orders, Execution,
        Navigation, BreachSearch, RouteSearch, OpeningQueue, GameTick, ExecutionMaintenance }
    internal sealed class RaidCpuProfiler
    {
        internal static bool Enabled;
        internal static void Reset(Map map) => maps.Remove(map);
        private static readonly ConditionalWeakTable<Map, RaidCpuProfiler> maps = new ConditionalWeakTable<Map, RaidCpuProfiler>();
        private readonly TacticalCpuSamples[] samples = new TacticalCpuSamples[Enum.GetValues(typeof(RaidCpuStage)).Length];
        private readonly TacticalCpuSamples gotoDelays = new TacticalCpuSamples();
        internal static void RecordGoto(Map map, int ticks)
        { if (Enabled && ticks >= 0 && maps.TryGetValue(map, out RaidCpuProfiler owner)) owner.gotoDelays.Add(ticks); }
        private readonly int[] startingGc = { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        private int frame = -1, rateFrame, rateTick;
        private double frameCost, worstFrameCost;
        private float rateTime = Time.realtimeSinceStartup;
        private double fps, tps;
        private RaidCpuProfiler()
        {
            rateFrame = Time.frameCount; rateTick = GenTicks.TicksGame;
            for (int i = 0; i < samples.Length; i++) samples[i] = new TacticalCpuSamples();
        }
        internal static Scope Measure(Map map, RaidCpuStage stage) => Enabled && Prefs.DevMode && map != null
            ? new Scope(maps.GetValue(map, value => new RaidCpuProfiler()), stage) : default(Scope);
        internal readonly struct Scope : IDisposable
        {
            private readonly RaidCpuProfiler owner;
            private readonly RaidCpuStage stage;
            private readonly long start;
            internal Scope(RaidCpuProfiler owner, RaidCpuStage stage)
            { this.owner = owner; this.stage = stage; start = Stopwatch.GetTimestamp(); }
            public void Dispose()
            {
                if (owner == null) return;
                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                owner.samples[(int)stage].Add(ms);
                // Execution includes its children: only count the inclusive execution
                // and standalone planning/orders for the frame high-water sample.
                if (stage != RaidCpuStage.Execution) return;
                if (owner.frame != Time.frameCount) { owner.frame = Time.frameCount; owner.frameCost = 0; }
                owner.frameCost += ms; owner.worstFrameCost = Math.Max(owner.worstFrameCost, owner.frameCost);
            }
        }
        internal static string Report(Map map)
        {
            Enabled = true;
            if (!maps.TryGetValue(map, out RaidCpuProfiler owner)) return "CPU profile: enable developer mode and let tactics run.";
            float now = Time.realtimeSinceStartup;
            if (now - owner.rateTime >= 1f)
            {
                owner.fps = (Time.frameCount - owner.rateFrame) / (now - owner.rateTime);
                owner.tps = (GenTicks.TicksGame - owner.rateTick) / (now - owner.rateTime);
                owner.rateTime = now; owner.rateFrame = Time.frameCount; owner.rateTick = GenTicks.TicksGame;
            }
            var text = new StringBuilder();
            text.AppendLine($"FPS={owner.fps:0.0} TPS={owner.tps:0.0} worst execution/frame={owner.worstFrameCost:0.000} ms "
                + $"managed heap={GC.GetTotalMemory(false) / 1048576.0:0.0} MiB "
                + $"GC={GC.CollectionCount(0) - owner.startingGc[0]}/{GC.CollectionCount(1) - owner.startingGc[1]}/{GC.CollectionCount(2) - owner.startingGc[2]}");
            for (int i = 0; i < owner.samples.Length; i++)
            {
                TacticalCpuSamples sample = owner.samples[i]; double[] p = sample.Percentiles();
                text.AppendLine($"  CPU {(RaidCpuStage)i}: n={sample.TotalSamples} sum={sample.TotalMilliseconds:0.0} "
                    + $"p50/p95/p99={p[0]:0.000}/{p[1]:0.000}/{p[2]:0.000} max={sample.Maximum:0.000} ms");
            }
            double[] latency = owner.gotoDelays.Percentiles();
            text.AppendLine($"  Goto request/start ticks: n={owner.gotoDelays.TotalSamples} "
                + $"p50/p95/p99={latency[0]:0}/{latency[1]:0}/{latency[2]:0} max={owner.gotoDelays.Maximum:0}");
            return text.ToString();
        }
    }

    [HarmonyPatch(typeof(TickManager), "DoSingleTick")]
    public static class Patch_RaidTacticalTickCpu
    {
        internal static void Prefix(out RaidCpuProfiler.Scope __state)
            => __state = RaidCpuProfiler.Enabled ? RaidCpuProfiler.Measure(Find.CurrentMap, RaidCpuStage.GameTick) : default;
        internal static void Postfix(RaidCpuProfiler.Scope __state) => __state.Dispose();
    }
}
