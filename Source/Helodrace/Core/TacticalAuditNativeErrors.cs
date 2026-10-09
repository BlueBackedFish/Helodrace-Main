using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;
using Verse;

namespace Helodrace
{
    // Isolated CLI diagnostics only. Preserve errors even if they arrive after
    // the final audit JSON, when Unity is shutting down. No production hook.
    internal static class TacticalAuditNativeErrors
    {
        [DataContract]
        private sealed class Entry
        {
            [DataMember] public string utc, kind, message, nativeStack, callbackStack;
            [DataMember] public int tick;
            [DataMember] public bool auditWritten;
        }
        private static string path, auditPath;
        private static int count;
        internal static void Start(string output)
        {
            if (path != null) return;
            auditPath = output; path = Path.Combine(Path.GetDirectoryName(output), "native-errors.jsonl");
            Application.logMessageReceived += Record;
        }
        private static void Record(string message, string stack, LogType kind)
        {
            if (kind != LogType.Error && kind != LogType.Exception && kind != LogType.Assert || count >= 32) return;
            count++;
            // Never recursively log an error in the error diary itself.
            try
            {
                int tick = -1;
                try { tick = GenTicks.TicksGame; } catch { }
                var entry = new Entry { utc = DateTime.UtcNow.ToString("O"), kind = kind.ToString(), tick = tick,
                    auditWritten = File.Exists(auditPath), message = message, nativeStack = stack,
                    callbackStack = Environment.StackTrace };
                using (var stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(Entry)).WriteObject(stream, entry);
                    File.AppendAllText(path, Encoding.UTF8.GetString(stream.ToArray()) + "\n", new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }
}
