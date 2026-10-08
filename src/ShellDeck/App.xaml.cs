using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ShellDeck;

public partial class App : Application
{
    /// <summary>Shown to the user and used for the per-user data folder under %LOCALAPPDATA% (logs, WebView2 cache).</summary>
    public const string ProductName = "ShellDeck";

    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName, "errors.log");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log(e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// An unexpected error in one pane must not take down every other shell with it:
    /// log it, tell the user, and keep running.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true;
        var detail = $"{e.Exception.Message}\n\nDétails : {CrashLogPath}";
        try
        {
            MessageDialog.ShowError(MainWindow, "Une erreur inattendue s'est produite", detail);
        }
        catch (Exception dialogException)
        {
            // The app's own dialog may be what is broken: the system box still gets the message through.
            Log(dialogException);
            MessageBox.Show($"Une erreur inattendue s'est produite :\n{detail}", ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Log(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
            // Nowhere left to report it.
        }
    }
}
