using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Pupitre;

/// <summary>Dark caption bar for a window, matching the app's chrome (Windows 10 20H1 and later).</summary>
public static class DarkTitleBar
{
    private const int DwmUseImmersiveDarkMode = 20;

    /// <summary>Call once the window has a handle, e.g. from <see cref="Window.SourceInitialized"/>.</summary>
    public static void Apply(Window window)
    {
        var enabled = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
