using System.Runtime.InteropServices;
using System.Windows;

namespace Pupitre;

/// <summary>The monitors currently connected, as WPF sees them.</summary>
internal static class Monitors
{
    private const double DefaultDpi = 96;

    /// <summary>
    /// Each monitor's work area (without the taskbar), in device-independent units. The app is system-DPI aware
    /// (no per-monitor manifest): Windows reports coordinates at the system DPI, which WPF divides by the same scale.
    /// </summary>
    public static IReadOnlyList<Rect> WorkAreas()
    {
        var scale = GetDpiForSystem() / DefaultDpi;
        var workAreas = new List<Rect>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var area = info.WorkArea;
                workAreas.Add(new Rect(
                    area.Left / scale, area.Top / scale,
                    (area.Right - area.Left) / scale, (area.Bottom - area.Top) / scale));
            }

            return true;
        }, IntPtr.Zero);

        return workAreas;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr clip, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
