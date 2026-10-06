using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Profiling
{
    public sealed class GameComponent_AgentMethodProfiler : GameComponent
    {
        public GameComponent_AgentMethodProfiler(Game game) { }
        public override void GameComponentUpdate() => AgentMethodProfiler.Poll();
    }

    // Independent instrumentation: no Dubs/RimDoctor code or dependency.
    internal static class AgentMethodProfiler
    {
        private static readonly Dictionary<MethodBase, int> ids = new Dictionary<MethodBase, int>();
        private static readonly List<MethodInfo> targets = new List<MethodInfo>();
        private static readonly WindowsMethodClock clock = new WindowsMethodClock();
        private static string root, hash, latest;
        private static bool initialized, failed;
        private static MethodCapture capture;
        private static ProfileSnapshot snapshot;
        private static long started;
        private static double deadline, nextPoll;

        internal static bool Initialize()
        {
            if (initialized) return root != null && !failed;
            initialized = true;
            if (!GenCommandLine.TryGetCommandLineArg("hdMethodProfile", out string path)) return false;
            try
            {
                root = Path.GetFullPath(path); Directory.CreateDirectory(root);
                using (var sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(AgentMethodProfiler).Assembly.Location))).Replace("-", "").ToLowerInvariant();
                bool detailed = GenCommandLine.TryGetCommandLineArg("hdMethodProfilePreset", out string preset) && preset == "detailed";
                if (preset != null && preset != "coarse" && preset != "detailed") throw new ArgumentException("Unknown profiler preset.");
                Add(typeof(TickManager), "DoSingleTick");
                Add(typeof(MapComponent_RaidTacticalCommunications), "MapComponentTick", "Frame", "RefreshFrames", "ProcessTick", "Validate", "Share", "Queue");
                if (detailed)
                {
                    Add(typeof(RaidCommunicationFrame), "Edge", "VoiceTo", "RadioTo");
                    Add(typeof(RaidCommunicationPolicy), "Delays");
                    Add(typeof(RaidTacticalRadioUtility), "Radios", "InstalledRadios", "OperatorAvailable");
                    foreach (Type type in typeof(RaidTacticalRadioUtility).GetNestedTypes(BindingFlags.NonPublic))
                        if (type.Name.Contains("<Radios>") || type.Name.Contains("<InstalledRadios>")) Add(type, "MoveNext");
                    Add(typeof(RaidSmokeUtility), "CoveringSmokeAt");
                }
                Add(typeof(MapComponent_RaidMovementAreas), "MapComponentTick", "MapComponentUpdate", "Pump", "Trim", "Select", "For", "ReadyFor", "GetArea", "CompleteReaders");
                Add(typeof(RaidMovementArea), "Pump");
                // Resolve concrete overrides, not the empty MapComponent base method.
                foreach (Type type in typeof(AgentMethodProfiler).Assembly.GetTypes().Where(type => !type.ContainsGenericParameters
                    && (typeof(MapComponent).IsAssignableFrom(type) || typeof(GameComponent).IsAssignableFrom(type))
                    && type.Name.Contains("Raid") && !type.Name.Contains("Audit")))
                    Add(type, "MapComponentTick", "MapComponentUpdate", "GameComponentTick", "GameComponentUpdate");
                if (GenCommandLine.TryGetCommandLineArg("hdMethodProfileTargets", out string extra))
                    foreach (string selector in extra.Split(';'))
                    {
                        string[] parts = selector.Split(new[] { "::" }, StringSplitOptions.None);
                        Type type = parts.Length == 2 ? AccessTools.TypeByName(parts[0]) : null;
                        if (type == null || type.ContainsGenericParameters || type.Namespace == typeof(AgentMethodProfiler).Namespace
                            || (type.Assembly != typeof(AgentMethodProfiler).Assembly && type.Assembly != typeof(Map).Assembly))
                            throw new ArgumentException("Unsupported target: " + selector);
                        int previousCount = targets.Count; Add(type, parts[1]);
                        if (targets.Count == previousCount && !targets.Any(m => m.DeclaringType == type && m.Name == parts[1]))
                            throw new ArgumentException("No concrete method found: " + selector);
                    }
                var harmony = new Harmony("Helodrace.AgentMethodProfiler");
                if (targets.Count > 128) throw new InvalidOperationException("Profiler target budget exceeded.");
                foreach (MethodInfo target in targets)
                    harmony.Patch(target, prefix: new HarmonyMethod(typeof(AgentMethodProfiler), nameof(Enter)),
                        finalizer: new HarmonyMethod(typeof(AgentMethodProfiler), nameof(Leave)));
                Write("capabilities.json", new ProfileSnapshot { label = "main-thread selective instrumentation; no native sampler",
                    assemblySha256 = hash, cpuSource = "GetThreadTimes (coarse); elapsed Stopwatch; self excludes tracked children only",
                    methods = targets.Select((m, i) => new ProfileMethod { id = i, method = Signature(m), cpuMeasured = CpuTarget(m) }).ToArray() });
                Status();
                Log.Message("Agent method profiler ready: " + root);
                return true;
            }
            catch (Exception error)
            {
                failed = true; capture = null; new Harmony("Helodrace.AgentMethodProfiler").UnpatchAll("Helodrace.AgentMethodProfiler");
                Log.Error("Agent method profiler initialization failed: " + error); return false;
            }
        }
        private static void Add(Type type, params string[] names)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (names.Contains(method.Name) && !method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() != null && !ids.ContainsKey(method))
                { ids.Add(method, targets.Count); targets.Add(method); }
        }
        private static string Signature(MethodInfo m) => m.DeclaringType.FullName + "." + m.Name
            + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName)) + ")";
        // Windows accounting is quantized. Repeated sub-millisecond reads can
        // badly misattribute CPU; only aggregate the encompassing game tick.
        private static bool CpuTarget(MethodInfo m) => m.DeclaringType == typeof(TickManager) && m.Name == "DoSingleTick";
        private static void Enter(MethodBase __originalMethod, out MethodToken __state)
        {
            MethodCapture current = capture;
            __state = current == null ? default : current.Enter(ids[__originalMethod]);
        }
        private static Exception Leave(Exception __exception, MethodToken __state)
        {
            __state.Owner?.Leave(__state, __exception != null);
            return __exception;
        }
        internal static void Begin(string label, int scenario = -1, int population = -1, int speed = -1, bool cpu = true, double seconds = 120)
        {
            if (!Initialize()) return;
            if (capture != null) throw new InvalidOperationException("A method capture is already active.");
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentException("Capture duration must be finite.");
            cpu = cpu && clock.Cpu100ns() >= 0;
            snapshot = new ProfileSnapshot { label = label, utc = DateTime.UtcNow.ToString("O"), assemblySha256 = hash,
                gameVersion = VersionControl.CurrentVersionString, runtime = Environment.Version.ToString(), operatingSystem = Environment.OSVersion.ToString(),
                mods = LoadedModManager.RunningModsListForReading.Select(mod => mod.PackageId).ToArray(),
                cpuSource = cpu ? "Windows GetThreadTimes; user+kernel, 100ns units, coarse resolution" : "disabled",
                startTick = GenTicks.TicksGame, startFrame = Time.frameCount, scenario = scenario, population = population, speed = speed,
                mapId = Find.CurrentMap?.uniqueID ?? -1 };
            started = clock.Timestamp(); deadline = Time.realtimeSinceStartup + Math.Max(1, Math.Min(300, seconds));
            capture = new MethodCapture(clock, targets.Select(m => cpu && CpuTarget(m)).ToArray());
            Status();
        }
        internal static void End()
        {
            MethodCapture current = capture;
            if (current == null) return;
            current.Stop();
            if (!current.Ready) return; // Never snapshot a half-written frame.
            capture = null;
            snapshot.endTick = GenTicks.TicksGame; snapshot.endFrame = Time.frameCount;
            snapshot.wallSeconds = (clock.Timestamp() - started) / (double)clock.Frequency;
            snapshot.dropped = current.Dropped; snapshot.complete = current.Dropped == 0;
            snapshot.methods = targets.Select((m, i) => new ProfileMethod { id = i, method = Signature(m),
                cpuMeasured = snapshot.cpuSource != "disabled" && CpuTarget(m), calls = current.Calls[i], exceptions = current.Errors[i],
                inclusiveMs = current.Milliseconds(current.Inclusive[i]), trackedSelfMs = current.Milliseconds(current.TrackedSelf[i]),
                maxMs = current.Milliseconds(current.Maximum[i]), threadCpuMs = current.CpuTicks[i] / 10000.0 }).ToArray();
            latest = "capture-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + snapshot.startTick + ".json";
            Write(latest, snapshot); Status();
        }
        internal static void Poll()
        {
            if (!Initialize()) return;
            try
            {
                if (capture != null && Time.realtimeSinceStartup >= deadline) End();
                if (Time.realtimeSinceStartup < nextPoll) return;
                nextPoll = Time.realtimeSinceStartup + 0.25;
                string command = Path.Combine(root, "command.json");
                if (!File.Exists(command)) return;
                ProfileCommand request;
                using (var stream = File.OpenRead(command)) request = (ProfileCommand)new DataContractJsonSerializer(typeof(ProfileCommand)).ReadObject(stream);
                File.Delete(command);
                if (request.action == "start") Begin(request.label ?? "cli", cpu: request.cpu, seconds: request.seconds);
                else if (request.action == "stop") End();
                else if (request.action != "status") throw new ArgumentException("Unknown profiler action.");
                Status();
            }
            catch (Exception error) { Write("error.json", new ProfileSnapshot { label = error.ToString() }); Log.Warning("Agent profiler command: " + error.Message); }
        }
        private static void Status() => Write("status.json", new ProfileStatus { state = capture == null ? "idle" : "capturing",
            utc = DateTime.UtcNow.ToString("O"), assemblySha256 = hash, latestCapture = latest,
            startTick = snapshot?.startTick ?? 0, currentTick = GenTicks.TicksGame });
        private static void Write<T>(string name, T value)
        {
            string path = Path.Combine(root, name), temporary = path + ".tmp";
            using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
