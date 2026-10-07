using System.Text.Json;
using Helodrace.Profiling;

internal static class EngineComparisonChecks
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    internal static void Run()
    {
        string root = Path.Combine("C:/Users/Public/Documents/ESTsoft/CreatorTemp", "hd-engine-comparison-check-" + Guid.NewGuid().ToString("N"));
        string baseline = Write(root, "vanilla", "vanilla", 100, 3);
        string candidate = Write(root, "new", "new", 150, 3);
        var result = JsonSerializer.SerializeToElement(EngineComparison.Compare(baseline, candidate));
        var group = result.GetProperty("groups")[0];
        if (group.GetProperty("tickCpuRatio").GetDouble() != 1.5
            || !group.GetProperty("within2x").GetBoolean()
            || group.GetProperty("newAiPerformanceGateEligible").GetBoolean()
            || group.GetProperty("newAiFixedWindowCpuGatePassed").GetBoolean())
            throw new Exception("R1 fallback must never pass the new AI performance gate.");
        // Phases are deliberately different across engines; the comparison above must accept them.
        Reject(() => EngineComparison.Compare(candidate, baseline), "non-vanilla baseline");
        foreach (string fault in new[] { "build", "map", "equipment", "engine", "targets", "dropped", "cpu", "partial-tick", "leak", "unmoved", "audit-error", "no-audit", "reference", "missing-legacy", "sapper-ineligible" })
        {
            string bad = Write(root, fault, "new", 150, 1);
            string capturePath = Path.Combine(bad, "profiles/capture-0.json"), auditPath = Path.Combine(bad, "audit.json");
            var capture = JsonSerializer.Deserialize<ProfileSnapshot>(File.ReadAllText(capturePath), Options)!;
            var audit = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(auditPath))!;
            void Audit(string key, object value) => audit[key] = JsonSerializer.SerializeToElement(value);
            switch (fault)
            {
                case "build": capture.assemblySha256 = "different build"; break;
                case "map": capture.benchmark.mapFingerprint = "different geometry"; Audit("mapFingerprint", "different geometry"); break;
                case "equipment": capture.benchmark.pawnFingerprint = "different gear"; Audit("pawnFingerprint", "different gear"); break;
                case "engine": capture.selectedEngine = "legacy"; break;
                case "targets": capture.methods = capture.methods.Append(new ProfileMethod { method = "extra" }).ToArray(); break;
                case "dropped": capture.dropped = 1; break;
                case "cpu": capture.mainThreadWindowCpuMs = null; break;
                case "partial-tick": capture.methods[0].calls--; break;
                case "leak": Audit("legacyComponents", new[] { "leaked component" }); break;
                case "unmoved": Audit("moved", 0); break;
                case "audit-error": Audit("error", "fixture failed"); break;
                case "no-audit": File.Move(auditPath, auditPath + ".missing"); break;
                case "reference": capture.reference.workload = "different Core workload"; break;
                case "missing-legacy":
                    capture.selectedEngine = capture.effectiveEngine = capture.benchmark.engine = capture.benchmark.effectiveEngine = "legacy";
                    Audit("engine", "legacy"); Audit("effectiveEngine", "legacy"); break;
                case "sapper-ineligible":
                    capture.benchmark.workload = "sapper-wall"; Audit("workload", "sapper-wall"); Audit("sapperEligiblePawns", 0); break;
            }
            File.WriteAllText(capturePath, JsonSerializer.Serialize(capture, Options));
            if (fault != "no-audit") File.WriteAllText(auditPath, JsonSerializer.Serialize(audit));
            Reject(() => EngineComparison.Compare(baseline, bad), fault);
        }
        string noEntry = Write(root, "no-entry", "new", 150, 3, entered: 0, implemented: true);
        group = JsonSerializer.SerializeToElement(EngineComparison.Compare(baseline, noEntry)).GetProperty("groups")[0];
        if (group.GetProperty("newAiFixedWindowCpuGatePassed").GetBoolean())
            throw new Exception("A stalled implemented engine must not pass the CPU gate.");
        string r2 = Write(root, "r2-complete", "new", 150, 3, implemented: true);
        string r2Baseline = Write(root, "r2-baseline", "vanilla", 100, 3, implemented: true);
        foreach (string directory in new[] { r2, r2Baseline })
        {
            foreach (string path in Directory.GetFiles(Path.Combine(directory, "profiles"), "capture-*.json"))
            {
                var capture = JsonSerializer.Deserialize<ProfileSnapshot>(File.ReadAllText(path), Options)!;
                capture.benchmark.fixtureVersion = 9; capture.benchmark.fixtureCase = "normal";
                File.WriteAllText(path, JsonSerializer.Serialize(capture, Options));
            }
            string pathAudit = Path.Combine(directory, "audit.json");
            var audit = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(pathAudit))!;
            audit["newFunctionalComplete"] = JsonSerializer.SerializeToElement(true);
            audit["newPhysicalPlansValid"] = JsonSerializer.SerializeToElement(true);
            audit["newCompletedUnits"] = JsonSerializer.SerializeToElement(1);
            audit["newEnteredByOrder"] = JsonSerializer.SerializeToElement(12);
            File.WriteAllText(pathAudit, JsonSerializer.Serialize(audit));
        }
        group = JsonSerializer.SerializeToElement(EngineComparison.Compare(r2Baseline, r2)).GetProperty("groups")[0];
        if (!group.GetProperty("newAiFixedWindowCpuGatePassed").GetBoolean()) throw new Exception("Complete R2 CPU gate failed.");
        string r2Audit = Path.Combine(r2, "audit.json");
        var stalled = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(r2Audit))!;
        stalled["newFunctionalComplete"] = JsonSerializer.SerializeToElement(false);
        File.WriteAllText(r2Audit, JsonSerializer.Serialize(stalled));
        group = JsonSerializer.SerializeToElement(EngineComparison.Compare(r2Baseline, r2)).GetProperty("groups")[0];
        if (group.GetProperty("newAiFixedWindowCpuGatePassed").GetBoolean()) throw new Exception("Partially entered R2 passed the gate.");
        Console.WriteLine("Engine comparison checks passed: same fixture/different phases, CPU ratio/range, fallback and stalled gates, 15 invalid-condition rejections.");
    }
    private static void Reject(Func<object> action, string label)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new Exception("Invalid engine comparison accepted: " + label);
    }
    private static string Write(string parent, string name, string engine, double cpu, int repeats, int entered = 4, bool implemented = false)
    {
        string root = Path.Combine(parent, name); Directory.CreateDirectory(Path.Combine(root, "profiles"));
        string effective = engine == "new" && !implemented ? "vanilla-fallback" : engine;
        var capture = new ProfileSnapshot {
            complete = true, assemblySha256 = "same DLL", selectedEngine = engine, effectiveEngine = effective,
            newEngineImplemented = implemented, startTick = 600, endTick = 1800, population = 12, scenario = 100,
            speed = 3, gameVersion = "1.6", runtime = "mono", operatingSystem = "Windows", cpuSource = "actual thread CPU",
            mods = new[] { "Core", "Helodrace" }, mainThreadWindowCpuMs = cpu * 2, processWindowCpuMs = cpu * 3,
            methods = new[] { new ProfileMethod { method = "Verse.TickManager.DoSingleTick()", cpuMeasured = true,
                threadCpuMs = cpu, inclusiveMs = cpu * 1.1, calls = 1200 } },
            reference = new ProfileReference { method = "Core", workload = "fixed batch", iterations = 10000,
                sampleMs = new[] { 1.0, 1.0, 1.0 }, patchOwners = Array.Empty<string>() },
            benchmark = new ProfileBenchmark { fixtureVersion = 5, seed = "seed", mapFingerprint = "geometry",
                pawnFingerprint = "equipment", faction = "LOW", workload = "open-approach", requestedPopulation = 12,
                warmupTicks = 600, sampleTicks = 1200, unitCount = 1, radioOperators = 0,
                engine = engine, effectiveEngine = effective, newEngineImplemented = implemented,
                startPhases = engine == "legacy" ? "Assemble:1" : "VanillaLord", endPhases = engine }
        };
        for (int i = 0; i < repeats; i++) File.WriteAllText(Path.Combine(root, $"profiles/capture-{i}.json"), JsonSerializer.Serialize(capture, Options));
        var audit = new {
            complete = true, isolationVerified = true, error = (string)null,
            engine, effectiveEngine = effective, newEngineImplemented = implemented,
            requestedPopulation = 12, population = 12, alive = 12, units = 1, radioOperators = 0,
            mapFingerprint = "geometry", pawnFingerprint = "equipment", workload = "open-approach", seed = "seed",
            warmupTicks = 600, sampleTicks = 1200, moved = 12, entered,
            legacyComponents = Array.Empty<string>(), installedLegacyHooks = Array.Empty<string>()
        };
        File.WriteAllText(Path.Combine(root, "audit.json"), JsonSerializer.Serialize(audit));
        return root;
    }
}
