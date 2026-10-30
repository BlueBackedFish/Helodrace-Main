using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Verse;
using Waylith.Runtime;

namespace Helodrace.Tests
{
    internal static class NativeRuntimeChecks
    {
        private static int observed, nativeStartFrame, compatStartFrame;
        private static double nativeStartTime, compatStartTime;
        internal static bool FrameProbeReady => observed >= 6;

        internal static void ObserveFrame()
        {
            if (observed++ != 0) return;
            nativeStartFrame = Waylith.Time.FrameCount;
            compatStartFrame = UnityEngine.Time.frameCount;
            nativeStartTime = Waylith.Time.RealtimeSinceStartupAsDouble;
            compatStartTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
        }

        internal static void Run(string output, Action<bool, string> check)
        {
            var app = WaylithRuntime.Current.App;
            check(ReferenceEquals(Waylith.EngineApplication.Instance.Runtime, WaylithRuntime.Current), "native runtime belongs to the actual host");
            double now = Waylith.Time.RealtimeSinceStartupAsDouble;
            check(double.IsFinite(now) && now >= 0 && Waylith.Time.RealtimeSinceStartupAsDouble >= now, "native real-time clock is finite and monotonic");
            double compatNow = UnityEngine.Time.realtimeSinceStartupAsDouble;
            int nativeFrames = Waylith.Time.FrameCount - nativeStartFrame;
            int compatFrames = UnityEngine.Time.frameCount - compatStartFrame;
            // Each clock has its own epoch; native counts at EndFrame and compatibility
            // counts at BeginFrame. Compare durations/cadence, never absolute values.
            var paths = new { app.DataPath, app.PersistentDataPath, app.LogDataPath, app.CacheDataPath, app.TemporaryCachePath,
                GameSavePath = GenFilePaths.SaveDataFolderPath, Frame = Waylith.Time.FrameCount, Time = now,
                CompatibilityFrame = UnityEngine.Time.frameCount, CompatibilityTime = compatNow,
                NativeElapsed = now - nativeStartTime, CompatibilityElapsed = compatNow - compatStartTime,
                NativeFrameDelta = nativeFrames, CompatibilityFrameDelta = compatFrames };
            File.WriteAllText(Path.Combine(output, "runtime.json"), JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true }));
            check(Math.Abs((now - nativeStartTime) - (compatNow - compatStartTime)) < .01,
                "native and compatibility clocks measure the same elapsed duration across actual frames");
            check(nativeFrames >= 5 && nativeFrames == compatFrames,
                "native frame counter advances with the actual game frame cadence");
            foreach (string path in new[] { app.DataPath, app.PersistentDataPath, app.LogDataPath, app.CacheDataPath, app.TemporaryCachePath })
                check(!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path), "native host path: " + path);
            check(Path.GetFullPath(UnityEngine.Application.persistentDataPath) == Path.GetFullPath(app.PersistentDataPath), "persistent path maps to native host state");

            Type diary = typeof(MapComponent_OilFields).Assembly.GetType("Helodrace.TacticalAuditNativeErrors", throwOnError: true);
            diary.GetMethod("Start", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { Path.Combine(output, "runtime-audit.json") });
            const string marker = "HELODRACE_EXPECTED_NATIVE_LOG_PROBE";
            Log.Warning(marker + "_warning");
            Log.Error(marker);
            string entries = File.ReadAllText(Path.Combine(output, "native-errors.jsonl"));
            check(entries.Contains(marker) && !entries.Contains(marker + "_warning"), "native error diary receives actual host errors and filters warnings");
        }
    }
}
