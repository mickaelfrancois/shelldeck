using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ShellDeck;

/// <summary>The Windows color dialog (comdlg32), as Windows Terminal opens it for a custom tab color.</summary>
public static class ColorPicker
{
    private const int RgbInit = 0x1;
    private const int FullOpen = 0x2;
    private const int AnyColor = 0x100;

    // The dialog's 16 "custom colors" slots: kept while the app runs, so a color made once can be picked again.
    private static readonly int[] CustomColors = new int[16];

    /// <summary>The chosen color as <c>#RRGGBB</c>, or null when the user cancels.</summary>
    public static unsafe string? Pick(Window owner, string? initialHex)
    {
        fixed (int* customColors = CustomColors)
        {
            var options = new ChooseColorOptions
            {
                Size = sizeof(ChooseColorOptions),
                Owner = new WindowInteropHelper(owner).Handle,
                Result = ToColorRef(initialHex),
                CustomColors = (IntPtr)customColors,
                Flags = RgbInit | FullOpen | AnyColor,
            };

            return ChooseColor(ref options) ? ToHex(options.Result) : null;
        }
    }

    // A COLORREF is 0x00BBGGRR.
    private static int ToColorRef(string? hex) =>
        hex is ['#', .. var digits] && int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? (rgb >> 16 & 0xFF) | (rgb & 0xFF00) | (rgb & 0xFF) << 16
            : 0;

    private static string ToHex(int colorRef) =>
        $"#{colorRef & 0xFF:X2}{colorRef >> 8 & 0xFF:X2}{colorRef >> 16 & 0xFF:X2}";

    [StructLayout(LayoutKind.Sequential)]
    private struct ChooseColorOptions
    {
        public int Size;
        public IntPtr Owner;
        public IntPtr Instance;
        public int Result;
        public IntPtr CustomColors;
        public int Flags;
        public IntPtr CustomData;
        public IntPtr Hook;
        public IntPtr TemplateName;
    }

    [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW")]
    private static extern bool ChooseColor(ref ChooseColorOptions options);
}
