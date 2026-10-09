using System.Runtime.Serialization;

namespace Helodrace.Profiling
{
    [DataContract]
    public sealed class ProfileMethod
    {
        [DataMember] public int id;
        [DataMember] public string method;
        [DataMember] public bool cpuMeasured;
        [DataMember] public long calls, exceptions;
        [DataMember] public long foreignThreadCalls, depthLimitCalls;
        [DataMember] public double inclusiveMs, trackedSelfMs, maxMs, threadCpuMs;
        [DataMember] public double? referencePercentPerCall, referencePercentPerTick;
        [DataMember] public int distributionSamples;
        [DataMember] public double p50Ms, p95Ms, p99Ms;
    }
    [DataContract]
    public sealed class ProfileSnapshot
    {
        [DataMember] public int schema = 9;
        [DataMember] public string label, utc, assemblySha256, gameVersion, runtime, operatingSystem, cpuSource;
        [DataMember] public string[] mods;
        [DataMember] public int startTick, endTick, startFrame, endFrame, population, scenario, speed, mapId;
        [DataMember] public double wallSeconds;
        [DataMember] public double? mainThreadWindowCpuMs, processWindowCpuMs;
        [DataMember] public string selectedEngine, effectiveEngine;
        [DataMember] public bool newEngineImplemented;
        [DataMember] public long dropped;
        [DataMember] public bool complete;
        [DataMember] public ProfileMethod[] methods;
        [DataMember] public ProfileReference reference;
        [DataMember] public ProfileBenchmark benchmark;
        [DataMember] public ProfileSlowCall[] slowCalls;
        [DataMember] public bool spikeTracing, spikeTraceSupported;
        [DataMember] public double spikeThresholdMs;
        [DataMember] public int? spikePawnId;
        [DataMember] public int spikeCapacity, spikeCallCapacity;
        [DataMember] public long spikeCandidates;
        [DataMember] public ProfileTickSpike[] tickSpikes;
        [DataMember] public int initialCallCapacity;
        [DataMember] public ProfileSlowCall[] initialCalls;
        [DataMember] public ProfileCpuCheckpoint[] cpuCheckpoints;
    }
    [DataContract]
    public sealed class ProfileCpuCheckpoint
    {
        [DataMember] public string label, phases;
        [DataMember] public int tick, frame, requestedOffset, activeCommands;
        [DataMember] public long? tickCalls;
        [DataMember] public double? tickCpuMs, tickElapsedMs, mainCpuMs, processCpuMs;
        [DataMember] public double wallSeconds;
    }
    [DataContract]
    public sealed class ProfileSlowCall
    {
        [DataMember] public int methodId;
        [DataMember] public double milliseconds;
        [DataMember] public int callId, parentCallId, rootCallId, depth, tick, frame, mapId, pawnId;
        [DataMember] public long invocation;
        [DataMember] public string squadId, phase, job;
        [DataMember] public double startMs, trackedSelfMs;
        [DataMember] public double? threadCpuMs;
    }
    [DataContract]
    public sealed class ProfileTickSpike
    {
        [DataMember] public ProfileSlowCall root;
        [DataMember] public ProfileSlowCall[] calls;
        [DataMember] public int callsSeen, callsFiltered, detailsDropped, gc0, gc1, gc2;
        [DataMember] public bool detailsComplete;
        [DataMember] public ProfileTickMethod[] methods;
        [DataMember] public bool methodsComplete;
    }
    [DataContract]
    public sealed class ProfileTickMethod
    {
        [DataMember] public int methodId;
        [DataMember] public long calls, exceptions;
        [DataMember] public double inclusiveMs, trackedSelfMs, maxMs;
    }
    [DataContract]
    public sealed class ProfileBenchmark
    {
        [DataMember] public string seed, mapFingerprint, faction, startPhases, endPhases;
        [DataMember] public string engine, effectiveEngine, workload, pawnFingerprint, fixtureCase;
        [DataMember] public bool newEngineImplemented;
        [DataMember] public int fixtureVersion = 1, requestedPopulation, warmupTicks, sampleTicks, unitCount, radioOperators;
    }
    [DataContract]
    public sealed class ProfileStatus
    {
        [DataMember] public string state, utc, assemblySha256, latestCapture;
        [DataMember] public int startTick, currentTick;
    }
    [DataContract]
    public sealed class ProfileCommand
    {
        [DataMember] public string action;
        [DataMember] public string label;
        [DataMember] public double seconds = 10;
        [DataMember] public bool cpu;
        [DataMember] public bool? spikes;
        [DataMember] public double? spikeThresholdMs;
        [DataMember] public int? spikePawnId;
    }
}
