using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static Pupitre.Pty.NativeMethods;

namespace Pupitre.Pty;

/// <summary>
/// A child process attached to a Windows pseudo console (ConPTY).
/// <see cref="Output"/> carries the UTF-8 VT stream to render; <see cref="Input"/> receives the keystrokes.
/// </summary>
public sealed class PtySession : IDisposable
{
    private readonly IntPtr _pseudoConsole;
    private readonly ProcessExitWaitHandle _processExit;
    private bool _disposed;

    private PtySession(IntPtr pseudoConsole, Stream input, Stream output, ProcessExitWaitHandle processExit, int processId)
    {
        ProcessId = processId;
        _pseudoConsole = pseudoConsole;
        Input = input;
        Output = output;
        _processExit = processExit;
    }

    public int ProcessId { get; }

    /// <summary>True when the shell is running a command (a child process), e.g. <c>dotnet watch</c> or <c>ssh</c>.</summary>
    public bool HasRunningCommand => !_disposed && ProcessTree.HasChildProcesses(ProcessId);

    public Stream Input { get; }

    public Stream Output { get; }

    public static PtySession Start(string commandLine, string? workingDirectory, TerminalSize size)
    {
        CreatePipeOrThrow(out var consoleInputRead, out var inputWrite);
        CreatePipeOrThrow(out var outputRead, out var consoleOutputWrite);

        var result = CreatePseudoConsole(size.ToCoord(), consoleInputRead, consoleOutputWrite, 0, out var pseudoConsole);
        Marshal.ThrowExceptionForHR(result);

        // ConPTY duplicated its ends of the pipes; keeping ours would prevent EOF on the output pipe.
        consoleInputRead.Dispose();
        consoleOutputWrite.Dispose();

        var process = StartAttachedProcess(commandLine, workingDirectory, pseudoConsole);

        return new PtySession(
            pseudoConsole,
            new FileStream(inputWrite, FileAccess.Write),
            new FileStream(outputRead, FileAccess.Read),
            new ProcessExitWaitHandle(process.hProcess),
            process.dwProcessId);
    }

    /// <returns>The exit code of the process, or null when the session was disposed first.</returns>
    public Task<int?> WaitForExitAsync()
    {
        var completion = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ThreadPool.RegisterWaitForSingleObject(
            _processExit,
            (_, _) => completion.TrySetResult(TryReadExitCode()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);
        return completion.Task;
    }

    public void Resize(TerminalSize size)
    {
        if (_disposed)
        {
            return;
        }

        Marshal.ThrowExceptionForHR(ResizePseudoConsole(_pseudoConsole, size.ToCoord()));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Closing the pseudo console terminates the attached processes and closes the output pipe,
        // which ends any pending read on Output.
        ClosePseudoConsole(_pseudoConsole);
        Input.Dispose();
        Output.Dispose();
        _processExit.Dispose();
    }

    private int? TryReadExitCode()
    {
        try
        {
            return GetExitCodeProcess(_processExit.SafeWaitHandle, out var exitCode) ? exitCode : null;
        }
        catch (ObjectDisposedException)
        {
            // Closing the tab disposes the session while the process is ending: nobody is waiting for the code.
            return null;
        }
    }

    private static void CreatePipeOrThrow(out SafeFileHandle read, out SafeFileHandle write)
    {
        if (!CreatePipe(out read, out write, IntPtr.Zero, 0))
        {
            throw new Win32Exception();
        }
    }

    private static ProcessInformation StartAttachedProcess(string commandLine, string? workingDirectory, IntPtr pseudoConsole)
    {
        var attributeListSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
        var attributeList = Marshal.AllocHGlobal(attributeListSize);

        try
        {
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
            {
                throw new Win32Exception();
            }

            if (!UpdateProcThreadAttribute(attributeList, 0, ProcThreadAttributePseudoConsole, pseudoConsole, IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            {
                throw new Win32Exception();
            }

            var startupInfo = new StartupInfoEx { lpAttributeList = attributeList };
            startupInfo.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();

            // Null standard handles: otherwise a child would inherit ours when this app runs with redirected stdio
            // (e.g. launched from a script) and write there instead of to the pseudo console.
            startupInfo.StartupInfo.dwFlags = StartfUseStdHandles;

            if (!CreateProcess(
                    null,
                    new StringBuilder(commandLine),
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out var processInfo))
            {
                throw new Win32Exception();
            }

            CloseHandle(processInfo.hThread);
            return processInfo;
        }
        finally
        {
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
        }
    }

    private sealed class ProcessExitWaitHandle : WaitHandle
    {
        public ProcessExitWaitHandle(IntPtr processHandle) =>
            SafeWaitHandle = new SafeWaitHandle(processHandle, ownsHandle: true);
    }
}

public readonly record struct TerminalSize(short Columns, short Rows)
{
    internal Coord ToCoord() => new(Columns, Rows);
}
