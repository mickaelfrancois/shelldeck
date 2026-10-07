using System.Diagnostics;
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
/// Renders one <see cref="TerminalPane"/>: xterm.js (in WebView2) emulates the terminal,
/// a <see cref="PtySession"/> runs the shell. Expects a <see cref="TerminalPane"/> as DataContext.
/// </summary>
public sealed class TerminalView : UserControl
{
    /// <summary>Bubbles up when an application shortcut is pressed while the terminal has the keyboard.</summary>
    public static readonly RoutedEvent ShortcutInvokedEvent = EventManager.RegisterRoutedEvent(
        nameof(ShortcutInvoked), RoutingStrategy.Bubble, typeof(EventHandler<ShortcutInvokedEventArgs>), typeof(TerminalView));

    private const string VirtualHostName = "shelldeck.local";
    private const int OutputBufferSize = 16 * 1024;
    private const string CtrlV = "\x16";
    private const string ClipboardBusyMessage = "Presse-papiers occupé par une autre application, réessayez.";

    // Output not yet handed to the page; past this size the pump stops reading, which throttles the program.
    private const int MaxPendingOutput = 1024 * 1024;

    // ConPTY may still be rendering the last output when the process ends: let it reach the page first.
    private static readonly TimeSpan ExitFlushDelay = TimeSpan.FromMilliseconds(150);

    private static readonly string WebViewDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.ProductName, "WebView2");

    private static Task<CoreWebView2Environment>? _sharedEnvironment;

    private readonly WebView2 _webView = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0C, 0x0C, 0x0E) };
    private readonly StringBuilder _pendingOutput = new();
    private bool _isFlushScheduled;
    private TerminalPane? _pane;
    private PtySession? _session;
    private Window? _hostWindow;
    private bool _isInitialized;
    private bool _isDisposed;
    private Task _pendingInput = Task.CompletedTask;

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
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        _pane = (TerminalPane)DataContext;

        // Win32 focus does not return to the WebView2 child window when the user switches back to the app.
        _hostWindow = Window.GetWindow(this);
        _hostWindow?.Activated += OnHostWindowActivated;
        _pane.PropertyChanged += OnPanePropertyChanged;

        try
        {
            var environment = await GetSharedEnvironment();
            if (_isDisposed)
            {
                return;
            }

            await _webView.EnsureCoreWebView2Async(environment);
        }
        catch (Exception exception) when (_isDisposed && exception is ObjectDisposedException or InvalidOperationException)
        {
            // The pane was closed while the browser was starting.
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
        core.ProcessFailed += OnProcessFailed;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.NavigationStarting += (_, args) => args.Cancel = !IsTerminalPage(args.Uri);
        core.Navigate($"https://{VirtualHostName}/terminal.html");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _hostWindow?.Activated -= OnHostWindowActivated;
        _pane?.PropertyChanged -= OnPanePropertyChanged;
        _webView.Dispose();

        // Before Windows 11 24H2, closing a pseudo console waits for its programs to handle the close event:
        // off the UI thread, so closing a tab never freezes the window.
        if (_session is { } session)
        {
            _ = Task.Run(session.Dispose);
        }
    }

    /// <summary>The renderer died (out of memory, GPU driver): reload the page; the shell keeps running.</summary>
    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (_isDisposed || _webView.CoreWebView2 is null)
        {
            return;
        }

        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive
            or CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
        {
            _webView.CoreWebView2.Reload();
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
                SendShortcutsToPage();
                if (_session is null)
                {
                    StartSession(new TerminalSize(message.Cols, message.Rows));
                }
                else
                {
                    // The page was reloaded after a renderer crash: its size may differ from the old one.
                    ResizeSession(new TerminalSize(message.Cols, message.Rows));
                    WriteToTerminal("\x1b[90m[affichage rechargé]\x1b[0m\r\n");
                }

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
        if (_session is not null)
        {
            return;
        }

        try
        {
            _session = PtySession.Start(
                Pane.Profile.LaunchCommandLine,
                Pane.Profile.WorkingDirectory(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                size);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or COMException or ArgumentException)
        {
            WriteToTerminal($"\x1b[31mImpossible de démarrer {Pane.Profile.LaunchCommandLine} : {exception.Message}\x1b[0m\r\n");
            Pane.MarkExited(exception is System.ComponentModel.Win32Exception win32 ? win32.NativeErrorCode : exception.HResult, TimeSpan.Zero);
            return;
        }

        var session = _session;
        Pane.TrackRunningCommands(() => session.HasRunningCommand);

        // The read blocks for the whole life of the shell: a dedicated thread rather than one taken from the pool.
        new Thread(() => PumpOutput(session)) { IsBackground = true, Name = "ShellDeck output pump" }.Start();
        _ = ReportExitAsync(session);
        FocusIfActive();
    }

    private void ResizeSession(TerminalSize size)
    {
        if (_session is null || size.Columns <= 0 || size.Rows <= 0)
        {
            return;
        }

        try
        {
            _session.Resize(size);
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            // A refused size keeps the previous one; the next resize will try again.
        }
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
                QueueOutput(chars.AsSpan(0, charCount));
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Disposing the session closes the pipe under the pending read: this is the normal end of the pump.
        }
    }

    /// <summary>
    /// Coalesces output into a single pending dispatcher operation, so a flood (<c>yes</c>, a large <c>cat</c>)
    /// cannot pile up work ahead of the user's input, Ctrl+C included. Called from the pump thread.
    /// </summary>
    private void QueueOutput(ReadOnlySpan<char> text)
    {
        lock (_pendingOutput)
        {
            while (_pendingOutput.Length >= MaxPendingOutput && !_isDisposed)
            {
                Monitor.Wait(_pendingOutput, TimeSpan.FromMilliseconds(100));
            }

            _pendingOutput.Append(text);
            if (_isFlushScheduled)
            {
                return;
            }

            _isFlushScheduled = true;
        }

        Dispatcher.InvokeAsync(FlushOutput, System.Windows.Threading.DispatcherPriority.Background);
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

    private async Task ReportExitAsync(PtySession session)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var exitCode = await session.WaitForExitAsync();
        if (exitCode is not { } code || _isDisposed)
        {
            return;
        }

        var lifetime = Stopwatch.GetElapsedTime(startedAt);
        await Task.Delay(ExitFlushDelay);
        if (_isDisposed)
        {
            return;
        }

        FlushOutput();
        WriteToTerminal($"\r\n\x1b[90m[processus terminé, code {code}]\x1b[0m\r\n");
        Pane.MarkExited(code, lifetime);
    }

    private void WriteInput(string data)
    {
        if (_session is not { } session)
        {
            return;
        }

        // The input pipe holds a few KB: a large paste into a busy program would block until it reads,
        // so the write happens off the UI thread. Writes stay in order: each waits for the previous one.
        var bytes = Encoding.UTF8.GetBytes(data);
        _pendingInput = _pendingInput.ContinueWith(
            _ =>
            {
                try
                {
                    session.Input.Write(bytes);
                    session.Input.Flush();
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    // The shell is gone: its input no longer matters.
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private void WriteToTerminal(string text)
    {
        if (_isDisposed || _webView.CoreWebView2 is null)
        {
            return;
        }

        _webView.CoreWebView2.PostWebMessageAsString(text);
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
        var chords = Shortcuts.All.Select(shortcut => new
        {
            code = shortcut.KeyCode,
            ctrl = shortcut.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control),
            alt = shortcut.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt),
            shift = shortcut.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift),
            character = shortcut.Character?.ToString(),
        });
        if (_isDisposed || _webView.CoreWebView2 is null)
        {
            return;
        }

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

        // The confirmation box runs a message loop: the pane may have been closed meanwhile.
        if (_isDisposed || _webView.CoreWebView2 is null)
        {
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
        public const string Focus = "focus";
    }
}

public sealed class ShortcutInvokedEventArgs(RoutedEvent routedEvent, Shortcut shortcut) : RoutedEventArgs(routedEvent)
{
    public Shortcut Shortcut { get; } = shortcut;
}
