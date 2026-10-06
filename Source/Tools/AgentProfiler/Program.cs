using System.Runtime.Serialization.Json;
using System.Text.Json;
using Helodrace.Profiling;

internal static class Program
{
    private static readonly JsonSerializerOptions options = new() { IncludeFields = true, WriteIndented = true };
    private static T Read<T>(string path)
    {
        // Mono and Windows may briefly hold a destination during replacement.
        // Retry reads only; never resend a state-changing command on an IO retry.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), options);
            }
            catch (IOException) when (attempt < 40) { Thread.Sleep(25); }
        }
    }
    private static string ReadText(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 40) { Thread.Sleep(25); }
        }
    }
    private static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, options));
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("Usage: capabilities ROOT | search ROOT TEXT | hotspots CAPTURE [inclusive|self|cpu|calls] [TOP] | compare BEFORE AFTER | start ROOT [SECONDS] [cpu] | stop ROOT | status ROOT");
            switch (args[0])
            {
                case "capabilities": Print(Read<ProfileSnapshot>(Path.Combine(args[1], "capabilities.json"))); break;
                case "search":
                    Print(Read<ProfileSnapshot>(Path.Combine(args[1], "capabilities.json")).methods
                        .Where(m => m.method.Contains(args.ElementAtOrDefault(2) ?? "", StringComparison.OrdinalIgnoreCase)).Take(128)); break;
                case "hotspots":
                    var capture = Read<ProfileSnapshot>(args[1]);
                    string metric = args.ElementAtOrDefault(2) ?? "self";
                    int top = Math.Clamp(int.Parse(args.ElementAtOrDefault(3) ?? "25"), 1, 128);
                    double Value(ProfileMethod m) => metric switch { "inclusive" => m.inclusiveMs, "self" => m.trackedSelfMs,
                        "cpu" => m.threadCpuMs, "calls" => m.calls, _ => throw new ArgumentException("Unknown metric.") };
                    int ticks = capture.endTick - capture.startTick;
                    Print(new { capture.label, capture.complete, capture.dropped, ticks, capture.wallSeconds,
                        warning = "Inclusive times overlap. Tracked self includes uninstrumented children and profiler overhead. CPU is coarse and only present for cpuMeasured scopes.",
                        methods = capture.methods.Where(m => m.calls > 0 && (metric != "cpu" || m.cpuMeasured)).OrderByDescending(Value).Take(top)
                            .Select(m => new { m.method, m.calls, m.exceptions, m.inclusiveMs, m.trackedSelfMs, m.maxMs, m.cpuMeasured, m.threadCpuMs,
                                inclusiveMsPerTick = ticks > 0 ? m.inclusiveMs / ticks : (double?)null }) }); break;
                case "compare":
                    var before = Read<ProfileSnapshot>(args[1]); var after = Read<ProfileSnapshot>(args[2]);
                    if (!before.complete || !after.complete || before.population != after.population || before.scenario != after.scenario
                        || before.speed != after.speed || before.gameVersion != after.gameVersion
                        || before.cpuSource != after.cpuSource || before.runtime != after.runtime || before.operatingSystem != after.operatingSystem
                        || !before.mods.SequenceEqual(after.mods) || before.endTick <= before.startTick || after.endTick <= after.startTick)
                        throw new ArgumentException("Comparison rejected: incomplete capture, no ticks, or differing scenario/population/speed/game/mods.");
                    bool sameTargets = before.methods.Select(m => m.method).OrderBy(name => name, StringComparer.Ordinal)
                        .SequenceEqual(after.methods.Select(m => m.method).OrderBy(name => name, StringComparer.Ordinal));
                    if (!sameTargets)
                        throw new ArgumentException("Comparison rejected: instrumentation target sets differ; use the same preset and additional targets.");
                    var old = before.methods.ToDictionary(m => m.method);
                    Print(new { beforeBuild = before.assemblySha256, afterBuild = after.assemblySha256,
                        warning = "Scenario metadata cannot prove identical map, phase, or machine load; check audit evidence. Self comparisons require identical target sets.",
                        sameTargets,
                        methods = after.methods.Where(m => old.ContainsKey(m.method)).Select(m => new { m.method,
                            beforeMsPerTick = old[m.method].inclusiveMs / (before.endTick - before.startTick),
                            afterMsPerTick = m.inclusiveMs / (after.endTick - after.startTick),
                            deltaMsPerTick = m.inclusiveMs / (after.endTick - after.startTick) - old[m.method].inclusiveMs / (before.endTick - before.startTick) })
                            .OrderByDescending(m => Math.Abs(m.deltaMsPerTick)).Take(25) }); break;
                case "start": case "stop": case "status":
                    var root = Path.GetFullPath(args[1]);
                    if (!File.Exists(Path.Combine(root, "capabilities.json"))) throw new ArgumentException("No profiler handshake. Launch the game with -hdMethodProfile=ROOT first.");
                    var command = new ProfileCommand { action = args[0], label = "agent-cli", cpu = args.Contains("cpu"),
                        seconds = args.Length > 2 && args[0] == "start" ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 10 };
                    string path = Path.Combine(root, "command.json"), temporary = Path.Combine(root, "command-" + Guid.NewGuid().ToString("N") + ".tmp");
                    if (File.Exists(path)) throw new InvalidOperationException("A command is pending. Do not overwrite it.");
                    if (File.Exists(Path.Combine(root, "error.json"))) File.Delete(Path.Combine(root, "error.json"));
                    string previous = ReadText(Path.Combine(root, "status.json"));
                    using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(ProfileCommand)).WriteObject(stream, command);
                    File.Move(temporary, path);
                    var timeout = System.Diagnostics.Stopwatch.StartNew();
                    while (timeout.Elapsed.TotalSeconds < 15)
                    {
                        Thread.Sleep(100);
                        string error = Path.Combine(root, "error.json"), status = Path.Combine(root, "status.json");
                        if (File.Exists(error)) throw new InvalidOperationException(Read<ProfileSnapshot>(error).label);
                        if (!File.Exists(path) && File.Exists(status) && ReadText(status) != previous) { Print(Read<ProfileStatus>(status)); return 0; }
                    }
                    throw new TimeoutException("Game did not acknowledge the command within 15 seconds; the pending command may still execute.");
                default: throw new ArgumentException("Unknown command.");
            }
            return 0;
        }
        catch (Exception error) { Print(new { error = error.Message }); return 1; }
    }
}
