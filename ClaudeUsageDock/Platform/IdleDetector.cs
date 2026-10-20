using System.Runtime.InteropServices;

namespace ClaudeUsageDock.Platform;

/// <summary>
/// Time since the last keyboard/mouse input in this session. Also grows while the
/// screen is locked, so the poller pauses then too.
/// </summary>
internal static partial class IdleDetector
{
    public static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero; // unknown: assume active, keep polling
        }

        // Both are 32-bit tick counts; unchecked subtraction handles the ~49-day wraparound.
        var idleMs = unchecked((uint)Environment.TickCount - info.DwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }

    [LibraryImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo plii);
}
