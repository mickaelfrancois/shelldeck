using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ShellDeck.Pty;

/// <summary>Finds the processes a shell has started, through a Toolhelp snapshot of the system.</summary>
public static class ProcessTree
{
    private const uint SnapshotProcesses = 0x00000002;

    // Console hosts belong to the pseudo console itself, and wslhost.exe to the WSL session: not commands the user started.
    private static readonly HashSet<string> HostProcesses = new(StringComparer.OrdinalIgnoreCase) { "conhost.exe", "OpenConsole.exe", "wslhost.exe" };

    public static bool HasChildProcesses(int parentProcessId) => HasCommand(Snapshot(), parentProcessId);

    /// <summary>
    /// True when <paramref name="processId"/> runs a command. A child with the same executable is the shell relaunching
    /// itself, not a command: wsl.exe starts a second wsl.exe that holds the Linux session, whose own processes are not
    /// Windows processes. Its children are looked at instead: a Windows program run from WSL (code.exe…) is one of them.
    /// </summary>
    public static bool HasCommand(IReadOnlyList<ProcessInfo> processes, int processId)
    {
        var executable = processes.FirstOrDefault(process => process.Id == processId)?.Executable;
        return processes
            .Where(process => process.ParentId == processId && !HostProcesses.Contains(process.Executable))
            .Any(child => !string.Equals(child.Executable, executable, StringComparison.OrdinalIgnoreCase) || HasCommand(processes, child.Id));
    }

    private static List<ProcessInfo> Snapshot()
    {
        using var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot.IsInvalid)
        {
            throw new Win32Exception();
        }

        var processes = new List<ProcessInfo>();
        var entry = new ProcessEntry { dwSize = Marshal.SizeOf<ProcessEntry>() };
        for (var found = Process32First(snapshot, ref entry); found; found = Process32Next(snapshot, ref entry))
        {
            processes.Add(new(entry.th32ProcessID, entry.th32ParentProcessID, entry.szExeFile));
        }

        return processes;
    }

    public sealed record ProcessInfo(int Id, int ParentId, string Executable);

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
