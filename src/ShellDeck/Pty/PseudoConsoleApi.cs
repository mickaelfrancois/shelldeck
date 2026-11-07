using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static ShellDeck.Pty.NativeMethods;

namespace ShellDeck.Pty;

/// <summary>
/// The pseudo console (ConPTY) entry points, either from the <c>conpty.dll</c> shipped next to the executable
/// (same engine as Windows Terminal) or from the ConPTY built into Windows (<c>kernel32</c>).
/// A pseudo console must be resized and closed by the instance that created it.
/// </summary>
public sealed unsafe class PseudoConsoleApi
{
    /// <summary>
    /// The embedded ConPTY behaves like a recent Windows whatever the OS build,
    /// so the page must not apply the heuristics meant for older conhost builds.
    /// </summary>
    private const int EmbeddedBuildNumber = 21376;

    private static readonly Lazy<PseudoConsoleApi> CurrentApi = new(() => Select(AppContext.BaseDirectory));

    private readonly delegate* unmanaged<Coord, IntPtr, IntPtr, uint, IntPtr*, int> _create;
    private readonly delegate* unmanaged<IntPtr, Coord, int> _resize;
    private readonly delegate* unmanaged<IntPtr, void> _close;

    private PseudoConsoleApi()
    {
        BuildNumber = Environment.OSVersion.Version.Build;
    }

    private PseudoConsoleApi(
        delegate* unmanaged<Coord, IntPtr, IntPtr, uint, IntPtr*, int> create,
        delegate* unmanaged<IntPtr, Coord, int> resize,
        delegate* unmanaged<IntPtr, void> close)
    {
        _create = create;
        _resize = resize;
        _close = close;
        IsEmbedded = true;
        BuildNumber = Math.Max(Environment.OSVersion.Version.Build, EmbeddedBuildNumber);
    }

    /// <summary>The ConPTY built into Windows.</summary>
    public static PseudoConsoleApi Inbox { get; } = new();

    /// <summary>The ConPTY chosen once for the process, from the application folder.</summary>
    public static PseudoConsoleApi Current => CurrentApi.Value;

    public bool IsEmbedded { get; }

    /// <summary>The Windows build number to report to the terminal page for this ConPTY.</summary>
    public int BuildNumber { get; }

    /// <summary>
    /// Loads <c>conpty.dll</c> from <paramref name="directory"/> by absolute path (never through PATH or the
    /// current directory), provided <c>OpenConsole.exe</c> sits next to it and the exports exist.
    /// Falls back to <see cref="Inbox"/> otherwise.
    /// </summary>
    public static PseudoConsoleApi Select(string directory)
    {
        try
        {
            var dllPath = Path.Combine(directory, "conpty.dll");
            if (!File.Exists(dllPath) || !File.Exists(Path.Combine(directory, "OpenConsole.exe")))
            {
                return Inbox;
            }

            if (!NativeLibrary.TryLoad(dllPath, out var library))
            {
                return Inbox;
            }

            if (NativeLibrary.TryGetExport(library, "ConptyCreatePseudoConsole", out var create)
                && NativeLibrary.TryGetExport(library, "ConptyResizePseudoConsole", out var resize)
                && NativeLibrary.TryGetExport(library, "ConptyClosePseudoConsole", out var close))
            {
                // The library stays loaded for the life of the process: sessions hold these pointers.
                return new PseudoConsoleApi(
                    (delegate* unmanaged<Coord, IntPtr, IntPtr, uint, IntPtr*, int>)create,
                    (delegate* unmanaged<IntPtr, Coord, int>)resize,
                    (delegate* unmanaged<IntPtr, void>)close);
            }

            NativeLibrary.Free(library);
            return Inbox;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Inbox;
        }
    }

    internal int Create(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr pseudoConsole)
    {
        if (!IsEmbedded)
        {
            return CreatePseudoConsole(size, input, output, flags, out pseudoConsole);
        }

        var inputAdded = false;
        var outputAdded = false;
        try
        {
            input.DangerousAddRef(ref inputAdded);
            output.DangerousAddRef(ref outputAdded);

            IntPtr created;
            var result = _create(size, input.DangerousGetHandle(), output.DangerousGetHandle(), flags, &created);
            pseudoConsole = created;
            return result;
        }
        finally
        {
            if (inputAdded)
            {
                input.DangerousRelease();
            }

            if (outputAdded)
            {
                output.DangerousRelease();
            }
        }
    }

    internal int Resize(IntPtr pseudoConsole, Coord size) =>
        IsEmbedded ? _resize(pseudoConsole, size) : ResizePseudoConsole(pseudoConsole, size);

    internal void Close(IntPtr pseudoConsole)
    {
        if (IsEmbedded)
        {
            _close(pseudoConsole);
        }
        else
        {
            ClosePseudoConsole(pseudoConsole);
        }
    }
}
