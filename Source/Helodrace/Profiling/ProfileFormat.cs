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
        [DataMember] public double inclusiveMs, trackedSelfMs, maxMs, threadCpuMs;
    }
    [DataContract]
    public sealed class ProfileSnapshot
    {
        [DataMember] public int schema = 1;
        [DataMember] public string label, utc, assemblySha256, gameVersion, runtime, operatingSystem, cpuSource;
        [DataMember] public string[] mods;
        [DataMember] public int startTick, endTick, startFrame, endFrame, population, scenario, speed, mapId;
        [DataMember] public double wallSeconds;
        [DataMember] public long dropped;
        [DataMember] public bool complete;
        [DataMember] public ProfileMethod[] methods;
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
    }
}
