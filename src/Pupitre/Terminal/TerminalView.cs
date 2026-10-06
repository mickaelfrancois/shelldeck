using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Pupitre.Pty;
using Pupitre.ViewModels;

namespace Pupitre.Terminal;

/// <summary>
/// Renders one <see cref="TerminalTab"/>: xterm.js (in WebView2) emulates the terminal,
/// a <see cref="PtySession"/> runs the shell. Expects a <see cref="TerminalTab"/> as DataContext.
/// </summary>
public sealed class TerminalView : UserControl
{
    /// <summary>Bubbles up when an application shortcut is pressed while the terminal has the keyboard.</summary>
    public static readonly RoutedEvent ShortcutInvokedEvent = EventManager.RegisterRoutedEvent(
        nameof(ShortcutInvoked), RoutingStrategy.Bubble, typeof(EventHandler<ShortcutInvokedEventArgs>), typeof(TerminalView));

    private const string VirtualHostName = "pupitre.local";
    private const int OutputBufferSize = 16 * 1024;
    private const string CtrlV = "\x16";
    private const string ClipboardBusyMessage = "Presse-papiers occupé par une autre application, réessayez.";

    private static readonly Lazy<Task<CoreWebView2Environment>> SharedEnvironment = new(() =>
        CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.ProductName, "WebView2")));

    private readonly WebView2 _webView = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0C, 0x0C, 0x0E) };
    private PtySession? _session;
    private Window? _hostWindow;
    private bool _isInitialized;
    private bool _isDisposed;

    public TerminalView()
    {
        Content = _webView;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public event EventHandler<ShortcutInvokedEventArgs> ShortcutInvoked
    {
        add => AddHandler(ShortcutInvokedEvent, value);
        remove => RemoveHandler(ShortcutInvokedEvent, value);
    }

    private TerminalTab Tab => (TerminalTab)DataContext;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        // Win32 focus does not return to the WebView2 child window when the user switches back to the app.
        _hostWindow = Window.GetWindow(this);
        _hostWindow?.Activated += OnHostWindowActivated;

        await _webView.EnsureCoreWebView2Async(await SharedEnvironment.Value);
        if (_isDisposed)
        {
            return;
        }

        var core = _webView.CoreWebView2;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.WebMessageReceived += OnWebMessageReceived;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.NavigationStarting += (_, args) => args.Cancel = !IsTerminalPage(args.Uri);
        core.Navigate($"https://{VirtualHostName}/terminal.html");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isDisposed = true;
        _hostWindow?.Activated -= OnHostWindowActivated;
        _session?.Dispose();
        _webView.Dispose();
    }

    private void OnHostWindowActivated(object? sender, EventArgs e)
    {
        if (IsVisible)
        {
            FocusTerminal();
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            FocusTerminal();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = JsonSerializer.Deserialize<TerminalMessage>(e.WebMessageAsJson, JsonSerializerOptions.Web);
        switch (message?.Type)
        {
            case MessageType.Ready:
                SendShortcutsToPage();
                StartSession(new TerminalSize(message.Cols, message.Rows));
                break;
            case MessageType.Shortcut when message.Index >= 0 && message.Index < Shortcuts.All.Count:
                RaiseEvent(new ShortcutInvokedEventArgs(ShortcutInvokedEvent, Shortcuts.All[message.Index]));
                break;
            case MessageType.OpenLink when message.Data is not null && WebLink.TryParse(message.Data, out var link):
                OpenLink(link);
                break;
            case MessageType.Input when message.Data is not null:
                WriteInput(message.Data);
                break;
            case MessageType.Resize:
                _session?.Resize(new TerminalSize(message.Cols, message.Rows));
                break;
            case MessageType.Title:
                Tab.ApplyTerminalTitle(message.Title ?? string.Empty);
                break;
            case MessageType.Bell:
                Tab.ReceiveBell();
                break;
            case MessageType.Notification when !string.IsNullOrWhiteSpace(message.Data):
                Tab.ReceiveNotification(message.Data);
                break;
            case MessageType.Copy when !string.IsNullOrEmpty(message.Data):
                CopyToClipboard(message.Data);
                break;
            case MessageType.Paste:
                PasteFromClipboard(message.FromKeyboard, message.BracketedPasteMode);
                break;
        }
    }

    private void StartSession(TerminalSize size)
    {
        if (_session is not null)
        {
            return;
        }

        try
        {
            _session = PtySession.Start(
                Tab.Profile.CommandLine,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                size);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            WriteToTerminal($"\x1b[31mImpossible de démarrer {Tab.Profile.CommandLine} : {exception.Message}\x1b[0m\r\n");
            Tab.MarkExited(exception.NativeErrorCode, TimeSpan.Zero);
            return;
        }

        var session = _session;
        Tab.TrackRunningCommands(() => session.HasRunningCommand);
        _ = Task.Run(() => PumpOutput(session));
        _ = ReportExitAsync(session);
        FocusTerminal();
    }

    private void PumpOutput(PtySession session)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[OutputBufferSize];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(OutputBufferSize)];

        try
        {
            int read;
            while ((read = session.Output.Read(bytes)) > 0)
            {
                var charCount = decoder.GetChars(bytes, 0, read, chars, 0);
                var text = new string(chars, 0, charCount);
                Dispatcher.InvokeAsync(() => WriteToTerminal(text));
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Disposing the session closes the pipe under the pending read: this is the normal end of the pump.
        }
    }

    private async Task ReportExitAsync(PtySession session)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var exitCode = await session.WaitForExitAsync();
        if (exitCode is not { } code || _isDisposed)
        {
            return;
        }

        WriteToTerminal($"\r\n\x1b[90m[processus terminé, code {code}]\x1b[0m\r\n");
        Tab.MarkExited(code, Stopwatch.GetElapsedTime(startedAt));
    }

    private void WriteInput(string data)
    {
        if (_session is null)
        {
            return;
        }

        _session.Input.Write(Encoding.UTF8.GetBytes(data));
        _session.Input.Flush();
    }

    private void WriteToTerminal(string text)
    {
        if (_isDisposed || _webView.CoreWebView2 is null)
        {
            return;
        }

        _webView.CoreWebView2.PostWebMessageAsString(text);
    }

    private void FocusTerminal()
    {
        if (_isDisposed || _webView.CoreWebView2 is null)
        {
            return;
        }

        _webView.Focus();
        _ = _webView.CoreWebView2.ExecuteScriptAsync("focusTerminal()");
    }

    private static bool IsTerminalPage(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Host == VirtualHostName;

    private void SendShortcutsToPage()
    {
        var chords = Shortcuts.All.Select(shortcut => new { code = shortcut.KeyCode, shift = shortcut.Shift });
        _ = _webView.CoreWebView2.ExecuteScriptAsync($"setShortcuts({JsonSerializer.Serialize(chords)})");
    }

    private void OpenLink(Uri link)
    {
        try
        {
            WebLink.OpenInBrowser(link);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            ShowStatus("Aucun navigateur n'est configuré pour ouvrir ce lien.");
        }
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException)
        {
            ShowStatus(ClipboardBusyMessage);
        }
    }

    private void PasteFromClipboard(bool fromKeyboard, bool bracketedPasteMode)
    {
        string? text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (ExternalException)
        {
            ShowStatus(ClipboardBusyMessage);
            return;
        }

        if (text is not null)
        {
            text = PasteGuard.Sanitize(text);
        }

        if (string.IsNullOrEmpty(text))
        {
            // No text (e.g. a screenshot): hand the keystroke to the program, which may read the clipboard itself.
            if (fromKeyboard)
            {
                WriteInput(CtrlV);
            }

            return;
        }

        if (PasteGuard.RequiresConfirmation(text, bracketedPasteMode) && !ConfirmPaste(text, bracketedPasteMode))
        {
            FocusTerminal();
            return;
        }

        _ = _webView.CoreWebView2.ExecuteScriptAsync($"pasteText({JsonSerializer.Serialize(text)})");
        FocusTerminal();
    }

    private bool ConfirmPaste(string text, bool bracketedPasteMode)
    {
        var lineCount = text.TrimEnd('\r', '\n').Split('\n').Length;
        var question = bracketedPasteMode
            ? $"Coller {text.Length:N0} caractères ?"
            : $"Coller {lineCount} ligne(s) ?\n\nDans ce terminal, chaque retour à la ligne exécute une commande.";

        var answer = Window.GetWindow(this) is { } owner
            ? MessageBox.Show(owner, question, App.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(question, App.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return answer == MessageBoxResult.Yes;
    }

    private void ShowStatus(string text)
    {
        if (_isDisposed || _webView.CoreWebView2 is null)
        {
            return;
        }

        _ = _webView.CoreWebView2.ExecuteScriptAsync($"showStatus({JsonSerializer.Serialize(text)})");
    }

    private sealed record TerminalMessage(
        string Type,
        string? Data,
        short Cols,
        short Rows,
        string? Title,
        bool FromKeyboard,
        bool BracketedPasteMode,
        int Index);

    /// <summary>Message types posted by terminal.html.</summary>
    private static class MessageType
    {
        public const string Ready = "ready";
        public const string Input = "input";
        public const string Resize = "resize";
        public const string Title = "title";
        public const string Bell = "bell";
        public const string Notification = "notification";
        public const string Copy = "copy";
        public const string Paste = "paste";
        public const string Shortcut = "shortcut";
        public const string OpenLink = "open-link";
    }
}

public sealed class ShortcutInvokedEventArgs(RoutedEvent routedEvent, Shortcut shortcut) : RoutedEventArgs(routedEvent)
{
    public Shortcut Shortcut { get; } = shortcut;
}
