using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Helodrace.ModernWar
{
    // Editor-session data only. Def settings are shared, assembly rail positions
    // are not: export each definition once and each visited weapon separately.
    public sealed class ModularPlacementBatch
    {
        private sealed class Part
        {
            public Func<string> Read;
            public string Observed;
        }
        private readonly Dictionary<string, Part> parts = new Dictionary<string, Part>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> assemblies = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, string> savedParts = new Dictionary<string, string>();
        private Dictionary<string, string> savedAssemblies = new Dictionary<string, string>();

        public int PartCount => parts.Count;
        public int WeaponCount => assemblies.Count;
        public bool HasUnsavedChanges => parts.Any(p => !savedParts.TryGetValue(p.Key, out string value) || value != p.Value.Read())
            || assemblies.Any(a => !savedAssemblies.TryGetValue(a.Key, out string value) || value != a.Value);

        public void ObservePart(string defName, Func<string> readSettings)
        {
            if (string.IsNullOrEmpty(defName)) throw new ArgumentException("A part must have a defName.");
            if (readSettings == null) throw new ArgumentNullException(nameof(readSettings));
            if (parts.TryGetValue(defName, out Part part)) part.Read = readSettings;
            else parts.Add(defName, new Part {Read = readSettings, Observed = readSettings()});
        }

        public bool RefreshParts()
        {
            bool changed = false;
            foreach (Part part in parts.Values)
            {
                string current = part.Read();
                changed |= current != part.Observed;
                part.Observed = current;
            }
            return changed;
        }

        public void ObserveAssembly(string instanceKey, string assemblyXml)
        {
            // Validate before replacing the last good capture.
            XElement.Parse(assemblyXml);
            assemblies[instanceKey] = assemblyXml;
        }

        public sealed class Export
        {
            public string Xml { get; internal set; }
            internal Dictionary<string, string> Parts, Assemblies;
        }

        public Export Capture()
        {
            var values = parts.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value.Read());
            var layouts = new Dictionary<string, string>(assemblies);
            var document = new XElement("ModularWeaponPlacementBatchExport",
                new XElement("parts", values.Select(p => new XElement("li", new XElement("thingDef", p.Key),
                    XElement.Parse("<settings>" + p.Value + "</settings>")))),
                new XElement("assemblies", layouts.OrderBy(a => a.Key, StringComparer.Ordinal)
                    .Select(a => XElement.Parse(a.Value))));
            return new Export {Xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + document + "\n", Parts = values, Assemblies = layouts};
        }

        public void MarkSaved(Export export)
        {
            savedParts = new Dictionary<string, string>(export.Parts);
            savedAssemblies = new Dictionary<string, string>(export.Assemblies);
        }

        public string Save(string directory)
        {
            Export export = Capture(); // Serialization failures never touch disk or saved state.
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "PlacementBatch_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
                + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".xml");
            string temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(export.Xml);
                File.Move(temporary, path);
                MarkSaved(export);
                return path;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
