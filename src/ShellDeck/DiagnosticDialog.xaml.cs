using System.Windows;

namespace ShellDeck;

/// <summary>A report to read and copy, in the app's own chrome (the diagnostic of the tab command).</summary>
public partial class DiagnosticDialog : Window
{
    private DiagnosticDialog(Window? owner, string heading, string report)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);

        Title = App.ProductName;
        if (owner is { IsLoaded: true })
        {
            Owner = owner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        HeadingText.Text = heading;
        ReportText.Text = report;
    }

    public static void Show(Window? owner, string heading, string report) =>
        new DiagnosticDialog(owner, heading, report).ShowDialog();

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ReportText.Text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The clipboard is held by another program: the text stays selectable in the window.
        }
    }
}
