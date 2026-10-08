using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using Helodrace;

internal static class TacticalRemovalTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Defs"))) directory = directory.Parent;
        string root = directory?.FullName ?? throw new Exception("Repository Defs directory not found.");
        string manifest = Path.Combine(root, "Source/Benchmarks/TacticalEngineAudit");
        var retired = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(manifest, "retired-legacy-types.json")))
            .Concat(JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(manifest, "retired-cqb-types.json")))).ToArray();
        var assembly = typeof(TacticalEngineSelection).Assembly;
        Check(retired.Length == 533 && retired.Distinct().Count() == retired.Length, "Pre-removal native DLL inventory must be complete and unique.");
        foreach (string name in retired.Append("Helodrace.LegacyTacticalAttribute"))
            Check(assembly.GetType(name) == null, "Retired runtime type remains: " + name);
        Type audit = assembly.GetType("Helodrace.TacticalRetirementAudit", true);
        var captured = (HashSet<string>)AccessTools.Field(audit, "Names").GetValue(null);
        Check(captured.SetEquals(retired.Append("Helodrace.LegacyTacticalAttribute"))
            && (bool)AccessTools.Method(audit, "TypesAbsent").Invoke(null, null), "Runtime retirement check differs from the captured native type inventory.");
        using (var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(manifest, "retired-legacy-source.json"))))
        {
            var files = inventory.RootElement.GetProperty("files").EnumerateArray().ToArray();
            Check(files.Length == 80, "Retirement source inventory must retain all 80 file hashes.");
            foreach (var file in files)
                Check(!File.Exists(Path.Combine(root, file.GetProperty("path").GetString())), "Retired source file remains: " + file);
        }
        using (var artifacts = JsonDocument.Parse(File.ReadAllText(Path.Combine(manifest, "retired-artifacts.json"))))
        {
            var files = artifacts.RootElement.GetProperty("files").EnumerateArray().ToArray();
            Check(files.Length == 109, "Retired source/test/definition/runner inventory must retain every removed artifact.");
            var deleted = files.Select(file => Path.GetFullPath(Path.Combine(root, file.GetProperty("path").GetString())))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string path in deleted) Check(!File.Exists(path), "Retired artifact remains: " + path);
            foreach (string project in Directory.EnumerateFiles(Path.Combine(root, "Source"), "*.csproj", SearchOption.AllDirectories))
                foreach (string include in XDocument.Load(project).Descendants("Compile").Attributes("Include").Select(attribute => attribute.Value))
                    Check(!deleted.Contains(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project), include))), "A surviving project compiles a retired source: " + project + ": " + include);
        }
        Check(TacticalEngineSelection.Parse(null) == TacticalEngineKind.New, "Production default must be the new engine.");
        try { TacticalEngineSelection.Parse("legacy"); throw new Exception("Removed legacy engine option was accepted."); }
        catch (ArgumentException) { checks++; }
        var retiredDefs = new HashSet<string> { "HD_RaidTacticalControl", "HD_RaidPrepareGrenade", "HD_RaidObserveOpening", "HD_RecoverSledgehammer" };
        var elements = Directory.EnumerateFiles(Path.Combine(root, "Defs"), "*.xml", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Descendants()).ToArray();
        Check(!elements.Any(element => element.Name.LocalName == "defName" && retiredDefs.Contains(element.Value)), "A retired tactical definition remains.");
        foreach (var element in elements)
            foreach (string value in element.Attributes().Select(attribute => attribute.Value).Append(element.HasElements ? "" : element.Value))
                Check(!captured.Contains(value.Trim()), "XML references a retired runtime class: " + value);
        foreach (string name in new[] { "Helodrace.Tactics.MapComponent_TacticalCommands", "Helodrace.Tactics.GameComponent_TacticalCommands",
            "Helodrace.Squads.GameComponent_CombatOrganizations", "Helodrace.RaidSmokeUtility", "Helodrace.RaidTacticalSpeech", "Helodrace.CompTacticalRadio" })
            Check(assembly.GetType(name) != null, "Shared or new functionality was accidentally removed: " + name);
        Console.WriteLine("PASS: " + checks + " retirement checks: 534 native types, 80 sources, XML dependencies, shared functionality and new default.");
    }
}
