using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Helodrace.Profiling
{
    public sealed class WindowsMethodClock : IMethodClock
    {
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] private static extern bool GetThreadTimes(IntPtr handle,
            out long creation, out long exit, out long kernel, out long user);
        public long Frequency => Stopwatch.Frequency;
        public long Timestamp() => Stopwatch.GetTimestamp();
        public long Cpu100ns() => Environment.OSVersion.Platform == PlatformID.Win32NT
            && GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user) ? kernel + user : -1;
        public long ProcessCpu100ns()
        {
            using (Process process = Process.GetCurrentProcess()) return process.TotalProcessorTime.Ticks;
        }
    }
}
