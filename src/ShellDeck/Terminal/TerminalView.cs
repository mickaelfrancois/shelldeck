using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ShellDeck.Pty;
using ShellDeck.ViewModels;

namespace ShellDeck.Terminal;

/// <summary>
/// Renders one <see cref="TerminalPane"/>: xterm.js (in WebView2) emulates the terminal, the pane's
/// <see cref="ShellSession"/> runs the shell. Expects a <see cref="TerminalPane"/> as DataContext.
/// </summary>
/// <remarks>
/// WPF may unload and reload the view while its pane lives on (session locked, display turned off):
/// the view then detaches from the shell, and reattaches with a new page that replays the recent output.
/// </remarks>
public sealed class TerminalView : UserControl, IOutputSink
{
    /// <summary>Bubbles up when an application shortcut is pressed while the terminal has the keyboard.</summary>
    public static readonly RoutedEvent ShortcutInvokedEvent = EventManager.RegisterRoutedEvent(
        nameof(ShortcutInvoked), RoutingStrategy.Bubble, typeof(EventHandler<ShortcutInvokedEventArgs>), typeof(TerminalView));

    private const string VirtualHostName = "shelldeck.local";
    private const string CtrlV = "\x16";
    private const string ClipboardBusyMessage = "Presse-papiers occupé par une autre application, réessayez.";

    // Output not yet handed to the page; past this size the pump stops reading, which throttles the program.
    private const int MaxPendingOutput = 1024 * 1024;

    private static readonly string WebViewDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.ProductName, "WebView2");

    private static Task<CoreWebView2Environment>? _sharedEnvironment;

    private readonly StringBuilder _pendingOutput = new();
    private bool _isFlushScheduled;
    private TerminalPane? _pane;
    private Window? _hostWindow;

    // Null while the view is unloaded; a new one is created on each load.
    private WebView2? _webView;
    private ShellSession? _attachedSession;

    public TerminalView()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public event EventHandler<ShortcutInvokedEventArgs> ShortcutInvoked
    {
        add => AddHandler(ShortcutInvokedEvent, value);
        remove => RemoveHandler(ShortcutInvokedEvent, value);
    }

    // Kept from the first Loaded: once the pane is removed from its tab, WPF swaps the DataContext
    // for a placeholder before Unloaded is raised.
    private TerminalPane Pane => _pane ?? (TerminalPane)DataContext;

    /// <summary>One browser process for all panes; a failed creation is retried by the next pane.</summary>
    private static Task<CoreWebView2Environment> GetSharedEnvironment()
    {
        if (_sharedEnvironment is null || _sharedEnvironment.IsFaulted || _sharedEnvironment.IsCanceled)
        {
            _sharedEnvironment = CoreWebView2Environment.CreateAsync(userDataFolder: WebViewDataFolder);
        }

        return _sharedEnvironment;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_webView is not null)
        {
            return;
        }

        _pane ??= (TerminalPane)DataContext;
        var webView = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0C, 0x0C, 0x0E) };
        _webView = webView;
        Content = webView;

        // Win32 focus does not return to the WebView2 child window when the user switches back to the app.
        _hostWindow = Window.GetWindow(this);
        _hostWindow?.Activated += OnHostWindowActivated;
        _pane.PropertyChanged += OnPanePropertyChanged;

        try
        {
            var environment = await GetSharedEnvironment();
            if (_webView != webView)
            {
                return;
            }

            await webView.EnsureCoreWebView2Async(environment);
        }
        catch (Exception exception) when (_webView != webView && exception is ObjectDisposedException or InvalidOperationException)
        {
            // The view was unloaded while the browser was starting.
            return;
        }
        catch (Exception exception) when (exception is WebView2RuntimeNotFoundException or COMException or IOException or UnauthorizedAccessException)
        {
            Content = new TextBlock
            {
                Text = $"Impossible d'afficher le terminal (WebView2) : {exception.Message}",
                Foreground = System.Windows.Media.Brushes.IndianRed,
                Margin = new Thickness(12),
                TextWrapping = TextWrapping.Wrap,
            };
            return;
        }

        if (_webView != webView)
        {
            return;
        }

        var core = webView.CoreWebView2;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.WebMessageReceived += OnWebMessageReceived;
        core.ProcessFailed += OnProcessFailed;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.NavigationStarting += (_, args) => args.Cancel = !IsTerminalPage(args.Uri);
        core.Navigate($"https://{VirtualHostName}/terminal.html");
    }

    /// <summary>The shell keeps running: the pane ends it when it is closed.</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_webView is not { } webView)
        {
            return;
        }

        _webView = null;
        _hostWindow?.Activated -= OnHostWindowActivated;
        _pane?.PropertyChanged -= OnPanePropertyChanged;
        _attachedSession?.Detach(this);
        _attachedSession = null;
        ((IOutputSink)this).Clear();
        Content = null;
        webView.Dispose();
    }

    /// <summary>The renderer died (out of memory, GPU driver): reload the page; the shell keeps running.</summary>
    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (_webView?.CoreWebView2 is not { } core)
        {
            return;
        }

        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive
            or CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
        {
            core.Reload();
        }
    }

    private void OnHostWindowActivated(object? sender, EventArgs e) => FocusIfActive();

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => FocusIfActive();

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalPane.IsActive))
        {
            FocusIfActive();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = JsonSerializer.Deserialize<TerminalMessage>(e.WebMessageAsJson, JsonSerializerOptions.Web);
        switch (message?.Type)
        {
            case MessageType.Ready:
                // A fresh page: the first one of the pane, or one that replaced a page lost with its view or renderer.
                SendShortcutsToPage();
                var size = new TerminalSize(message.Cols, message.Rows);
                if (Pane.Session is null && !Pane.IsExited)
                {
                    StartSession(size);
                }
                else
                {
                    // The old page may have had another size.
                    ResizeSession(size);
                }

                AttachSession();
                break;
            case MessageType.Shortcut when message.Index >= 0 && message.Index < Shortcuts.All.Count:
                RaiseEvent(new ShortcutInvokedEventArgs(ShortcutInvokedEvent, Shortcuts.All[message.Index]));
                break;
            case MessageType.OpenLink when message.Data is not null && WebLink.TryParse(message.Data, out var link):
                OpenLink(link);
                break;
            case MessageType.Input when message.Data is not null:
                Pane.Session?.WriteInput(message.Data);
                break;
            case MessageType.Resize:
                ResizeSession(new TerminalSize(message.Cols, message.Rows));
                break;
            case MessageType.Focus:
                Pane.RequestActivation();
                break;
            case MessageType.Title:
                Pane.ApplyTerminalTitle(message.Title ?? string.Empty);
                break;
            case MessageType.Bell:
                Pane.ReceiveBell();
                break;
            case MessageType.Notification when !string.IsNullOrWhiteSpace(message.Data):
                Pane.ReceiveNotification(message.Data);
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
        var pane = Pane;
        try
        {
            pane.Session = ShellSession.Start(
                pane.Profile.LaunchCommandLine,
                pane.Profile.WorkingDirectory(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                size,
                pane.MarkExited);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or COMException or ArgumentException)
        {
            WriteToTerminal($"\x1b[31mImpossible de démarrer {pane.Profile.LaunchCommandLine} : {exception.Message}\x1b[0m\r\n");
            pane.MarkExited(exception is System.ComponentModel.Win32Exception win32 ? win32.NativeErrorCode : exception.HResult, TimeSpan.Zero);
            return;
        }

        var session = pane.Session;
        pane.TrackRunningCommands(() => session.HasRunningCommand);
        FocusIfActive();
    }

    /// <summary>Shows the shell in this page: its recent output first, then what it writes next.</summary>
    private void AttachSession()
    {
        if (Pane.Session is not { } session)
        {
            return;
        }

        _attachedSession = session;
        WriteToTerminal(session.Attach(this));
    }

    private void ResizeSession(TerminalSize size)
    {
        if (Pane.Session is not { } session || size.Columns <= 0 || size.Rows <= 0)
        {
            return;
        }

        try
        {
            session.Resize(size);
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            // A refused size keeps the previous one; the next resize will try again.
        }
    }

    /// <summary>
    /// Coalesces output into a single pending dispatcher operation, so a flood (<c>yes</c>, a large <c>cat</c>)
    /// cannot pile up work ahead of the user's input, Ctrl+C included. Called from the pump thread.
    /// </summary>
    void IOutputSink.Append(ReadOnlySpan<char> text)
    {
        lock (_pendingOutput)
        {
            _pendingOutput.Append(text);
            if (_isFlushScheduled)
            {
                return;
            }

            _isFlushScheduled = true;
        }

        Dispatcher.InvokeAsync(FlushOutput, System.Windows.Threading.DispatcherPriority.Background);
    }

    void IOutputSink.Clear()
    {
        lock (_pendingOutput)
        {
            _pendingOutput.Clear();
            Monitor.PulseAll(_pendingOutput);
        }
    }

    void IOutputSink.WaitForRoom()
    {
        lock (_pendingOutput)
        {
            while (_pendingOutput.Length >= MaxPendingOutput && _webView is not null)
            {
                Monitor.Wait(_pendingOutput, TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private void FlushOutput()
    {
        string text;
        lock (_pendingOutput)
        {
            text = _pendingOutput.ToString();
            _pendingOutput.Clear();
            _isFlushScheduled = false;
            Monitor.PulseAll(_pendingOutput);
        }

        if (text.Length > 0)
        {
            WriteToTerminal(text);
        }
    }

    private void WriteToTerminal(string text)
    {
        if (_webView?.CoreWebView2 is not { } core)
        {
            return;
        }

        core.PostWebMessageAsString(text);
    }


    /// <summary>In a split tab, only the active pane takes the keyboard.</summary>
    private void FocusIfActive()
    {
        if (IsVisible && Pane.IsActive)
        {
            FocusTerminal();
        }
    }

    private void FocusTerminal()
    {
        if (_webView is not { CoreWebView2: { } core } webView)
        {
            return;
        }

        webView.Focus();
        _ = core.ExecuteScriptAsync("focusTerminal()");
    }

    private static bool IsTerminalPage(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Host == VirtualHostName;

    private void SendShortcutsToPage()
    {
        var chords = Shortcuts.All.Select(shortcut => new
        {
            code = shortcut.KeyCode,
            ctrl = shortcut.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control),
            alt = shortcut.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt),
            shift = shortcut.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift),
            character = shortcut.Character?.ToString(),
        });
        if (_webView?.CoreWebView2 is not { } core)
        {
            return;
        }

        _ = core.ExecuteScriptAsync($"setShortcuts({JsonSerializer.Serialize(chords)})");
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
                Pane.Session?.WriteInput(CtrlV);
            }

            return;
        }

        if (PasteGuard.RequiresConfirmation(text, bracketedPasteMode) && !ConfirmPaste(text, bracketedPasteMode))
        {
            FocusTerminal();
            return;
        }

        // The confirmation box runs a message loop: the pane may have been closed meanwhile.
        if (_webView?.CoreWebView2 is not { } core)
        {
            return;
        }

        _ = core.ExecuteScriptAsync($"pasteText({JsonSerializer.Serialize(text)})");
        FocusTerminal();
    }

    private bool ConfirmPaste(string text, bool bracketedPasteMode)
    {
        var lineCount = text.TrimEnd('\r', '\n').Split('\n').Length;
        return bracketedPasteMode
            ? MessageDialog.Confirm(Window.GetWindow(this), $"Coller {text.Length:N0} caractères ?", string.Empty, "Coller")
            : MessageDialog.Confirm(Window.GetWindow(this), $"Coller {lineCount} ligne(s) ?",
                "Dans ce terminal, chaque retour à la ligne exécute une commande.", "Coller");
    }

    private void ShowStatus(string text)
    {
        if (_webView?.CoreWebView2 is not { } core)
        {
            return;
        }

        _ = core.ExecuteScriptAsync($"showStatus({JsonSerializer.Serialize(text)})");
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
        public const string Focus = "focus";
    }
}

public sealed class ShortcutInvokedEventArgs(RoutedEvent routedEvent, Shortcut shortcut) : RoutedEventArgs(routedEvent)
{
    public Shortcut Shortcut { get; } = shortcut;
}
