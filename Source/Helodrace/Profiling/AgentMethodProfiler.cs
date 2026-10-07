using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Helodrace.Tactics;

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
        private static CoreReferenceCalibration reference;
        private static long started;
        private static long windowCpu, windowProcessCpu;
        private static bool ending;
        private static bool defaultSpikes;
        private static double defaultSpikeThreshold = 5;
        internal static bool Capturing => capture != null;
        private static double deadline, nextPoll, nextReference;

        internal static bool Initialize()
        {
            if (initialized) return root != null && !failed;
            initialized = true;
            if (!GenCommandLine.TryGetCommandLineArg("hdMethodProfile", out string path)) return false;
            try
            {
                root = Path.GetFullPath(path); Directory.CreateDirectory(root);
                CoreReferenceCalibration.WarmUp();
                using (var sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(AgentMethodProfiler).Assembly.Location))).Replace("-", "").ToLowerInvariant();
                bool detailed = GenCommandLine.TryGetCommandLineArg("hdMethodProfilePreset", out string preset) && preset == "detailed";
                if (preset != null && preset != "coarse" && preset != "detailed" && preset != "spikes") throw new ArgumentException("Unknown profiler preset.");
                defaultSpikes = preset == "spikes";
                if (GenCommandLine.TryGetCommandLineArg("hdMethodProfileSpikes", out string trace)) defaultSpikes = bool.Parse(trace);
                if (GenCommandLine.TryGetCommandLineArg("hdMethodProfileSpikeThresholdMs", out string threshold))
                    defaultSpikeThreshold = double.Parse(threshold, CultureInfo.InvariantCulture);
                ValidateThreshold(defaultSpikeThreshold);
                Add(typeof(TickManager), "DoSingleTick");
                Add(typeof(Map), "MapPreTick", "MapPostTick", "MapUpdate");
                Add(typeof(MapComponentUtility), "MapComponentTick", "MapComponentUpdate");
                Add(typeof(GameComponentUtility), "GameComponentTick", "GameComponentUpdate");
                if (preset == "spikes")
                {
                    Add(typeof(TickList), "Tick");
                    Add(typeof(Pawn), "TickInterval");
                    Add(typeof(Pawn_JobTracker), "StartJob", "EndCurrentJob", "TryFindAndStartJob", "DetermineNextJob");
                    Add(typeof(Pawn_PathFollower), "PatherTick");
                    Add(typeof(ThinkNode_JobGiver), "TryIssueJobPackage");
                    Add(typeof(JobGiver_AIFightEnemy), "TryGiveJob");
                    Add(typeof(PathFinder), "PathFinderTick", "ForceCompleteScheduledJobs", "CreateRequest");
                    Add(typeof(MapComponent_TacticalCommands), "Advance", "ReturnMembers", "EndOwned", "Issue", "Find");
                }
                else
                {
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
                }
                // Resolve concrete overrides, not the empty MapComponent base method.
                foreach (Type type in typeof(AgentMethodProfiler).Assembly.GetTypes().Where(type => !type.ContainsGenericParameters
                    && (typeof(MapComponent).IsAssignableFrom(type) || typeof(GameComponent).IsAssignableFrom(type))
                    && type.Namespace != typeof(AgentMethodProfiler).Namespace && !type.Name.Contains("Audit")))
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
                    harmony.Patch(target, prefix: new HarmonyMethod(typeof(AgentMethodProfiler), Prefix(target)),
                        finalizer: new HarmonyMethod(typeof(AgentMethodProfiler), nameof(Leave)));
                if (targets.Any(m => m.DeclaringType == typeof(GenRadial) && m.Name == nameof(GenRadial.NumCellsInRadius)))
                    throw new ArgumentException("The Core calibration method cannot also be instrumented.");
                Write("capabilities.json", new ProfileSnapshot { label = "main-thread selective instrumentation; no native sampler",
                    spikeTraceSupported = true, spikeTracing = defaultSpikes, spikeThresholdMs = defaultSpikeThreshold,
                    spikeCapacity = MethodCapture.SpikeCapacity, spikeCallCapacity = MethodCapture.SpikeCallCapacity,
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
        private static void ValidateThreshold(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < .1 || value > 1000)
                throw new ArgumentException("Spike threshold must be finite and between 0.1 and 1000ms.");
        }
        // Select typed bindings once, never allocate __args or reflect per call.
        private static string Prefix(MethodInfo method)
        {
            if (!method.IsStatic && typeof(Pawn).IsAssignableFrom(method.DeclaringType)) return nameof(EnterPawn);
            FieldInfo field = AccessTools.Field(method.DeclaringType, "pawn");
            if (!method.IsStatic && field?.FieldType == typeof(Pawn)) return nameof(EnterTracker);
            if (method.GetParameters().Any(p => p.Name == "pawn" && p.ParameterType == typeof(Pawn))) return nameof(EnterPawnArgument);
            if (method.GetParameters().Any(p => p.Name == "command" && p.ParameterType == typeof(TacticalSquadCommand))) return nameof(EnterCommand);
            if (method.GetParameters().Any(p => p.Name == "member" && p.ParameterType == typeof(TacticalMemberCommand))) return nameof(EnterMember);
            return nameof(Enter);
        }
        private static ProfileCallContext Context(MethodBase method) => new ProfileCallContext {
            Tick = GenTicks.TicksGame + (method.DeclaringType == typeof(TickManager) && method.Name == "DoSingleTick" ? 1 : 0),
            Frame = Time.frameCount, MapId = -1, PawnId = -1, Phase = -1 };
        private static ProfileCallContext PawnContext(MethodBase method, Pawn pawn)
        {
            ProfileCallContext result = Context(method); result.Identity = true;
            if (pawn == null) return result;
            result.PawnId = pawn.thingIDNumber; result.MapId = pawn.Map?.uniqueID ?? -1;
            result.Job = pawn.CurJob?.def?.defName;
            var service = pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
            if (service != null && service.TryProfileContext(pawn, out string id, out TacticalCommandPhase phase))
            { result.SquadId = id; result.Phase = (int)phase; }
            return result;
        }
        private static void EnterPawn(MethodBase __originalMethod, Pawn __instance, out MethodToken __state) => EnterActor(__originalMethod, __instance, out __state);
        private static void EnterTracker(MethodBase __originalMethod, Pawn ___pawn, out MethodToken __state) => EnterActor(__originalMethod, ___pawn, out __state);
        private static void EnterPawnArgument(MethodBase __originalMethod, Pawn pawn, out MethodToken __state) => EnterActor(__originalMethod, pawn, out __state);
        private static void EnterMember(MethodBase __originalMethod, TacticalMemberCommand member, out MethodToken __state) => EnterActor(__originalMethod, member?.Pawn, out __state);
        private static void EnterActor(MethodBase method, Pawn pawn, out MethodToken token)
        {
            MethodCapture current = capture;
            token = current == null ? default : current.Enter(ids[method], current.Tracing && current.OnCaptureThread ? PawnContext(method, pawn) : default);
        }
        private static void EnterCommand(MethodBase __originalMethod, TacticalSquadCommand command, out MethodToken __state)
        {
            MethodCapture current = capture;
            ProfileCallContext context = default;
            if (current?.Tracing == true && current.OnCaptureThread)
            {
                context = Context(__originalMethod); context.Identity = true;
                context.MapId = command?.Owner?.map.uniqueID ?? -1; context.SquadId = command?.Id;
                context.Phase = command == null ? -1 : (int)command.Phase;
            }
            __state = current == null ? default : current.Enter(ids[__originalMethod], context);
        }
        private static void Enter(MethodBase __originalMethod, out MethodToken __state)
        {
            MethodCapture current = capture;
            __state = current == null ? default : current.Enter(ids[__originalMethod], current.Tracing && current.OnCaptureThread ? Context(__originalMethod) : default);
        }
        private static Exception Leave(Exception __exception, MethodToken __state)
        {
            __state.Owner?.Leave(__state, __exception != null);
            return __exception;
        }
        internal static void Begin(string label, int scenario = -1, int population = -1, int speed = -1, bool cpu = true, double seconds = 120,
            ProfileBenchmark benchmark = null, bool? spikes = null, double? spikeThresholdMs = null)
        {
            if (!Initialize()) return;
            if (capture != null) throw new InvalidOperationException("A method capture is already active.");
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentException("Capture duration must be finite.");
            cpu = cpu && clock.Cpu100ns() >= 0;
            bool tracing = spikes ?? defaultSpikes;
            double threshold = spikeThresholdMs ?? defaultSpikeThreshold; ValidateThreshold(threshold);
            snapshot = new ProfileSnapshot { label = label, utc = DateTime.UtcNow.ToString("O"), assemblySha256 = hash,
                gameVersion = VersionControl.CurrentVersionString, runtime = Environment.Version.ToString(), operatingSystem = Environment.OSVersion.ToString(),
                mods = LoadedModManager.RunningModsListForReading.Select(mod => mod.PackageId).ToArray(),
                cpuSource = cpu ? "Windows GetThreadTimes; user+kernel, 100ns units, coarse resolution" : "disabled",
                selectedEngine = TacticalEngineSelection.Kind.ToString().ToLowerInvariant(),
                effectiveEngine = TacticalEngineSelection.EffectiveEngine, newEngineImplemented = TacticalEngineSelection.NewImplemented,
                startTick = GenTicks.TicksGame, startFrame = Time.frameCount, scenario = scenario, population = population, speed = speed,
                mapId = Find.CurrentMap?.uniqueID ?? -1, benchmark = benchmark,
                spikeTracing = tracing, spikeTraceSupported = true, spikeThresholdMs = tracing ? threshold : 0,
                spikeCapacity = tracing ? MethodCapture.SpikeCapacity : 0, spikeCallCapacity = tracing ? MethodCapture.SpikeCallCapacity : 0 };
            ending = false;
            reference = new CoreReferenceCalibration(); reference.Sample();
            nextReference = Time.realtimeSinceStartup + 1;
            double limit = GenCommandLine.TryGetCommandLineArg("hdTacticalEngineAudit", out _) ? 1800 : 300;
            started = clock.Timestamp(); deadline = Time.realtimeSinceStartup + Math.Max(1, Math.Min(limit, seconds));
            windowProcessCpu = cpu ? clock.ProcessCpu100ns() : -1;
            windowCpu = cpu ? clock.Cpu100ns() : -1;
            capture = new MethodCapture(clock, targets.Select(m => cpu && CpuTarget(m)).ToArray(), traceSpikes: tracing,
                tickMethodId: targets.FindIndex(CpuTarget), spikeThresholdMs: threshold, originTimestamp: started);
            Status();
        }
        internal static void End()
        {
            MethodCapture current = capture;
            if (current == null) return;
            if (!ending)
            {
                ending = true;
                snapshot.endTick = GenTicks.TicksGame; snapshot.endFrame = Time.frameCount;
                snapshot.wallSeconds = (clock.Timestamp() - started) / (double)clock.Frequency;
                long endingCpu = windowCpu >= 0 ? clock.Cpu100ns() : -1;
                snapshot.mainThreadWindowCpuMs = endingCpu >= 0 ? (endingCpu - windowCpu) / 10000.0 : (double?)null;
                snapshot.processWindowCpuMs = windowProcessCpu >= 0 ? (clock.ProcessCpu100ns() - windowProcessCpu) / 10000.0 : (double?)null;
            }
            current.Stop();
            if (!current.Ready) return; // Never snapshot a half-written frame.
            capture = null;
            reference.Sample(); snapshot.reference = reference.Snapshot();
            snapshot.dropped = current.Dropped; snapshot.complete = current.Dropped == 0;
            snapshot.methods = targets.Select((m, i) => { double[] p = current.Percentiles(i); return new ProfileMethod { id = i, method = Signature(m),
                cpuMeasured = snapshot.cpuSource != "disabled" && CpuTarget(m), calls = current.Calls[i], exceptions = current.Errors[i],
                inclusiveMs = current.Milliseconds(current.Inclusive[i]), trackedSelfMs = current.Milliseconds(current.TrackedSelf[i]),
                maxMs = current.Milliseconds(current.Maximum[i]), threadCpuMs = current.CpuTicks[i] / 10000.0,
                referencePercentPerCall = ReferenceMetrics.Percent(current.Milliseconds(current.Inclusive[i]), current.Calls[i], snapshot.reference),
                referencePercentPerTick = ReferenceMetrics.Percent(current.Milliseconds(current.Inclusive[i]), snapshot.endTick - snapshot.startTick, snapshot.reference),
                distributionSamples = (int)Math.Min(current.Calls[i], MethodCapture.DistributionCapacity), p50Ms = p[0], p95Ms = p[1], p99Ms = p[2] }; }).ToArray();
            snapshot.slowCalls = current.SlowRecords.Select(value => Call(current, value))
                .Where(value => value.milliseconds > 0).OrderByDescending(value => value.milliseconds).ToArray();
            snapshot.spikeCandidates = current.SpikeCandidates;
            if (current.Tracing) snapshot.tickSpikes = current.TickSpikes.Where(s => s.Count > 0).OrderByDescending(s => s.Root.Elapsed)
                .Select(s => new ProfileTickSpike { root = Call(current, s.Root),
                    calls = s.Calls.Take(s.Count).OrderBy(c => c.Start).ThenBy(c => c.CallId).Select(c => Call(current, c)).ToArray(),
                    callsSeen = s.Seen, detailsDropped = s.Seen - s.Count, detailsComplete = s.Seen == s.Count,
                    gc0 = s.Gc0, gc1 = s.Gc1, gc2 = s.Gc2 }).ToArray();
            latest = "capture-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + snapshot.startTick + ".json";
            Write(latest, snapshot); Status();
        }
        private static ProfileSlowCall Call(MethodCapture current, RecordedProfileCall call) => new ProfileSlowCall {
            methodId = call.Method, milliseconds = current.Milliseconds(call.Elapsed), trackedSelfMs = current.Milliseconds(call.Self),
            callId = call.CallId, parentCallId = call.ParentId, rootCallId = call.RootId, depth = call.Depth,
            startMs = current.Milliseconds(call.Start), threadCpuMs = call.Cpu >= 0 ? call.Cpu / 10000.0 : (double?)null,
            tick = current.Tracing ? call.Context.Tick : -1, frame = current.Tracing ? call.Context.Frame : -1,
            mapId = current.Tracing ? call.Context.MapId : -1, pawnId = current.Tracing ? call.Context.PawnId : -1,
            squadId = call.Context.SquadId, job = call.Context.Job,
            phase = current.Tracing && call.Context.Phase >= 0 ? ((TacticalCommandPhase)call.Context.Phase).ToString() : null };
        internal static void Poll()
        {
            if (!Initialize()) return;
            try
            {
                if (capture != null && Time.realtimeSinceStartup >= deadline) End();
                if (capture != null && Time.realtimeSinceStartup >= nextReference)
                {
                    // Exclude calibration (and any patched callees) from target statistics.
                    MethodCapture current = capture; capture = null;
                    try { reference.Sample(); } finally { capture = current; }
                    nextReference = Time.realtimeSinceStartup + 1;
                }
                if (Time.realtimeSinceStartup < nextPoll) return;
                nextPoll = Time.realtimeSinceStartup + 0.25;
                string command = Path.Combine(root, "command.json");
                if (!File.Exists(command)) return;
                ProfileCommand request;
                using (var stream = File.OpenRead(command)) request = (ProfileCommand)new DataContractJsonSerializer(typeof(ProfileCommand)).ReadObject(stream);
                File.Delete(command);
                if (request.action == "start") Begin(request.label ?? "cli", cpu: request.cpu, seconds: request.seconds,
                    spikes: request.spikes, spikeThresholdMs: request.spikeThresholdMs);
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
