using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ShellDeck.Pty;

/// <summary>Finds the processes a shell has started, through a Toolhelp snapshot of the system.</summary>
public static class ProcessTree
{
    private const uint SnapshotProcesses = 0x00000002;

    // Console hosts belong to the pseudo console itself, not to a command the user started.
    private static readonly HashSet<string> ConsoleHosts = new(StringComparer.OrdinalIgnoreCase) { "conhost.exe", "OpenConsole.exe" };

    public static bool HasChildProcesses(int parentProcessId)
    {
        using var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot.IsInvalid)
        {
            throw new Win32Exception();
        }

        var entry = new ProcessEntry { dwSize = Marshal.SizeOf<ProcessEntry>() };
        for (var found = Process32First(snapshot, ref entry); found; found = Process32Next(snapshot, ref entry))
        {
            if (entry.th32ParentProcessID == parentProcessId && !ConsoleHosts.Contains(entry.szExeFile))
            {
                return true;
            }
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public int dwSize;
        public int cntUsage;
        public int th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public int th32ModuleID;
        public int cntThreads;
        public int th32ParentProcessID;
        public int pcPriClassBase;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint dwFlags, int th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
    private static extern bool Process32First(SafeFileHandle hSnapshot, ref ProcessEntry lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
    private static extern bool Process32Next(SafeFileHandle hSnapshot, ref ProcessEntry lppe);
}
