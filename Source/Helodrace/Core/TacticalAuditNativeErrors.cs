using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Waylith.Logging;
using Waylith.Runtime;
using Verse;

namespace Helodrace
{
    // Isolated CLI diagnostics only. Preserve errors even if they arrive after
    // the final audit JSON, when the native host is shutting down. No production hook.
    internal static class TacticalAuditNativeErrors
    {
        [DataContract]
        private sealed class Entry
        {
            [DataMember] public string utc, kind, category, message, nativeStack, callbackStack;
            [DataMember] public int tick;
            [DataMember] public bool auditWritten;
        }
        private static string path, auditPath;
        private static int count;
        [ThreadStatic] private static bool recording;
        internal static void Start(string output)
        {
            if (path != null) return;
            auditPath = output; path = Path.Combine(Path.GetDirectoryName(output), "native-errors.jsonl");
            LogStream.MessageReceived += Record;
        }
        private static void Record(LogLevel kind, string category, string message, string stack)
        {
            // Match the original main-thread diary; native logging also includes worker events.
            if (Environment.CurrentManagedThreadId != WaylithRuntime.Current.App.MainThreadId
                || recording || kind != LogLevel.Error && kind != LogLevel.Critical || count >= 32) return;
            count++;
            recording = true;
            // Never recursively log an error in the error diary itself.
            try
            {
                int tick = -1;
                try { tick = GenTicks.TicksGame; } catch { }
                var entry = new Entry { utc = DateTime.UtcNow.ToString("O"), kind = kind.ToString(), category = category, tick = tick,
                    auditWritten = File.Exists(auditPath), message = message, nativeStack = stack,
                    callbackStack = Environment.StackTrace };
                using (var stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(Entry)).WriteObject(stream, entry);
                    File.AppendAllText(path, Encoding.UTF8.GetString(stream.ToArray()) + "\n", new UTF8Encoding(false));
                }
            }
            catch { }
            finally { recording = false; }
        }
    }
}
