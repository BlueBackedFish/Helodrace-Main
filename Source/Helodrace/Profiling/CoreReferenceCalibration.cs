using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace Helodrace.Profiling
{
    internal sealed class CoreReferenceCalibration
    {
        private const int Iterations = 10000;
        private readonly double[] samples = new double[304];
        private int count;
        private static volatile int sink;
        // Fixed inputs, no Pawn/Map reads, random numbers, allocation or per-call instrumentation.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Batch()
        {
            int result = 0;
            for (int i = 0; i < Iterations; i++) result += GenRadial.NumCellsInRadius(8f + i % 16);
            sink = result;
        }
        internal static void WarmUp() { Batch(); Batch(); }
        internal void Sample()
        {
            if (count == samples.Length) return;
            long started = Stopwatch.GetTimestamp(); Batch();
            samples[count++] = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        }
        internal ProfileReference Snapshot() => new ProfileReference
        {
            method = "Verse.GenRadial.NumCellsInRadius(System.Single)", workload = "radius=8+(i%16); iterations=10000; v1",
            iterations = Iterations, sampleMs = samples.Take(count).ToArray(),
            patchOwners = (Harmony.GetPatchInfo(AccessTools.Method(typeof(GenRadial), nameof(GenRadial.NumCellsInRadius)))?.Owners
                ?? Enumerable.Empty<string>()).OrderBy(v => v).ToArray()
        };
    }
}
