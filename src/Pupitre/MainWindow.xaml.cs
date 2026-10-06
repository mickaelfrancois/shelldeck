using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Pupitre.Shells;
using Pupitre.Terminal;
using Pupitre.ViewModels;

namespace Pupitre;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const double ToastMargin = 12;
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(8);

    private readonly MainViewModel _viewModel = new(
        ShellDiscovery.DiscoverShells(),
        ShellDiscovery.DiscoverSshConnections(),
        AppSettings.Load(),
        settings => settings.Save());

    private readonly DispatcherTimer _toastTimer;

    // Editors write a file in several steps: reload once they are done.
    private readonly DispatcherTimer _settingsReloadTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly IDisposable _settingsWatcher;

    // The state to restore when the window is closed while minimized from the taskbar.
    private bool _wasMaximized;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        RestoreWindowPlacement();

        AddHandler(TerminalView.ShortcutInvokedEvent, new EventHandler<ShortcutInvokedEventArgs>((_, e) => Execute(e.Shortcut)));
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;

        _toastTimer = new DispatcherTimer { Interval = ToastDuration };
        _toastTimer.Tick += (_, _) => HideToast();
        AttentionToast.CustomPopupPlacementCallback = PlaceToastAtBottomRight;
        _viewModel.AttentionRequested += (_, tab) => ShowToast(tab);
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTab))
            {
                HideToast();
            }
        };

        // The toast is a separate window: it would not follow the main window, and would stay above other apps.
        LocationChanged += (_, _) => HideToast();
        Deactivated += (_, _) => HideToast();
        SourceInitialized += (_, _) => UseDarkTitleBar();

        _settingsReloadTimer.Tick += (_, _) =>
        {
            _settingsReloadTimer.Stop();
            // A file that does not parse yet (half-typed edit) keeps the current settings.
            if (AppSettings.TryLoad(out var settings))
            {
                _viewModel.ReloadSettings(settings);
            }
        };
        _settingsWatcher = AppSettings.Watch(() => Dispatcher.BeginInvoke(() =>
        {
            _settingsReloadTimer.Stop();
            _settingsReloadTimer.Start();
        }));
        Closed += (_, _) => _settingsWatcher.Dispose();
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
            {
                _wasMaximized = WindowState == WindowState.Maximized;
            }
        };

        if (_viewModel.DefaultProfile is { } defaultProfile)
        {
            _viewModel.Open(defaultProfile);
        }
    }

    private void OnProfileClicked(object sender, RoutedEventArgs e) =>
        _viewModel.Open(DataContextOf<ShellProfile>(sender));

    private void OnSetDefaultProfileClicked(object sender, RoutedEventArgs e) =>
        _viewModel.SetDefaultProfile(DataContextOf<ShellProfile>(sender));

    private void OnCloseTabClicked(object sender, RoutedEventArgs e) =>
        CloseTab(DataContextOf<TerminalTab>(sender));

    /// <summary>Shortcuts typed outside the terminal (filter, tab list); the terminal reports its own.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape && ShortcutHelpPopup.IsOpen)
        {
            ShortcutHelpPopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (Shortcuts.Find(key, Keyboard.Modifiers) is { } shortcut)
        {
            Execute(shortcut);
            e.Handled = true;
        }
    }

    private void Execute(Shortcut shortcut)
    {
        switch (shortcut.Action)
        {
            case ShortcutAction.NewTab:
                _viewModel.OpenDefault();
                break;
            case ShortcutAction.ClosePane when _viewModel.SelectedTab is { } tab:
                ClosePane(tab, tab.ActivePane);
                break;
            case ShortcutAction.NextTab:
                _viewModel.SelectNextTab();
                break;
            case ShortcutAction.PreviousTab:
                _viewModel.SelectPreviousTab();
                break;
            case ShortcutAction.SelectTab:
                _viewModel.SelectTabByNumber(shortcut.TabNumber);
                break;
            case ShortcutAction.SplitRight:
                _viewModel.SelectedTab?.Split(SplitDirection.Right);
                break;
            case ShortcutAction.SplitDown:
                _viewModel.SelectedTab?.Split(SplitDirection.Down);
                break;
            case ShortcutAction.SplitAuto when _viewModel.SelectedTab is { } tab:
                tab.Split(LongerSide(tab.ActivePane));
                break;
            case ShortcutAction.FocusPaneLeft:
                _viewModel.SelectedTab?.MoveFocus(PaneDirection.Left);
                break;
            case ShortcutAction.FocusPaneUp:
                _viewModel.SelectedTab?.MoveFocus(PaneDirection.Up);
                break;
            case ShortcutAction.FocusPaneRight:
                _viewModel.SelectedTab?.MoveFocus(PaneDirection.Right);
                break;
            case ShortcutAction.FocusPaneDown:
                _viewModel.SelectedTab?.MoveFocus(PaneDirection.Down);
                break;
            case ShortcutAction.ResizePaneLeft:
                _viewModel.SelectedTab?.MoveDivider(PaneDirection.Left);
                break;
            case ShortcutAction.ResizePaneUp:
                _viewModel.SelectedTab?.MoveDivider(PaneDirection.Up);
                break;
            case ShortcutAction.ResizePaneRight:
                _viewModel.SelectedTab?.MoveDivider(PaneDirection.Right);
                break;
            case ShortcutAction.ResizePaneDown:
                _viewModel.SelectedTab?.MoveDivider(PaneDirection.Down);
                break;
        }
    }

    /// <summary>As Windows Terminal: splitting along the longer side keeps both panes as square as possible.</summary>
    private SplitDirection LongerSide(TerminalPane pane) =>
        pane.Bounds.Width * TerminalArea.ActualWidth >= pane.Bounds.Height * TerminalArea.ActualHeight
            ? SplitDirection.Right
            : SplitDirection.Down;

    private void ClosePane(TerminalTab tab, TerminalPane pane)
    {
        if (!tab.IsSplit)
        {
            CloseTab(tab);
            return;
        }

        var interruption = pane.IsMain && tab.IsClaudeSession ? "Claude Code sera interrompu." : "La commande en cours sera interrompue.";
        if (pane.HasActivity && !Confirm($"Fermer ce volet ?\n\n{interruption}"))
        {
            return;
        }

        tab.ClosePane(pane);
    }

    private void OnSplitterDragged(object sender, DragDeltaEventArgs e)
    {
        var split = DataContextOf<PaneSplit>(sender);
        split.MoveDivider(new Vector(e.HorizontalChange / TerminalArea.ActualWidth, e.VerticalChange / TerminalArea.ActualHeight));
    }

    private void CloseTab(TerminalTab tab)
    {
        if (tab.HasActivity && !Confirm($"Fermer « {tab.Title} » ?\n\n{InterruptionWarning(tab)}"))
        {
            return;
        }

        _viewModel.Close(tab);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var activeTabs = _viewModel.Tabs.Where(tab => tab.HasActivity).ToList();
        if (activeTabs.Count > 0)
        {
            var list = string.Join("\n", activeTabs.Select(tab => $"  • {tab.Title} : {ActivityName(tab)}"));
            if (!Confirm($"Quitter {App.ProductName} ?\n\nCes onglets ont une activité en cours :\n{list}"))
            {
                e.Cancel = true;
                return;
            }
        }

        SaveWindowPlacement();
    }

    private static string ActivityName(TerminalTab tab) =>
        tab.IsClaudeSession ? "Claude Code" : "commande en cours";

    private static string InterruptionWarning(TerminalTab tab) =>
        tab.IsClaudeSession ? "Claude Code sera interrompu." : "La commande en cours sera interrompue.";

    private bool Confirm(string question) =>
        MessageBox.Show(this, question, App.ProductName, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    private void RestoreWindowPlacement()
    {
        if (_viewModel.WindowPlacement is not { IsValid: true } saved)
        {
            return;
        }

        // After unplugging the monitor the window was on, it opens on the nearest remaining one.
        var placement = saved.KeepOnScreen(Monitors.WorkAreas());
        WindowStartupLocation = WindowStartupLocation.Manual;
        (Left, Top, Width, Height) = (placement.Left, placement.Top, placement.Width, placement.Height);
        WindowState = placement.IsMaximized ? WindowState.Maximized : WindowState.Normal;
        _wasMaximized = placement.IsMaximized;
    }

    // RestoreBounds keeps the normal size of a maximized window, so un-maximizing after a restart still works.
    private void SaveWindowPlacement() =>
        _viewModel.SaveWindowPlacement(new WindowPlacement(
            RestoreBounds.Left, RestoreBounds.Top, RestoreBounds.Width, RestoreBounds.Height,
            WindowState == WindowState.Maximized || WindowState == WindowState.Minimized && _wasMaximized));

    private void OnNewTabClicked(object sender, RoutedEventArgs e) =>
        _viewModel.OpenDefault();

    private void OnTogglePanelSideClicked(object sender, RoutedEventArgs e) =>
        _viewModel.TogglePanelSide();

    /// <summary>Opens settings.json in the editor associated with .json files, Notepad when there is none.</summary>
    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(AppSettings.FilePath))
        {
            _viewModel.SaveCurrentSettings();
        }

        try
        {
            Process.Start(new ProcessStartInfo(AppSettings.FilePath) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppSettings.FilePath}\"") { UseShellExecute = true });
        }
    }

    private void OnFilterKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.OpenFirstMatch();
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.FilterText = string.Empty;
                e.Handled = true;
                break;
        }
    }

    private void OnToastClicked(object sender, MouseButtonEventArgs e)
    {
        if (AttentionToast.DataContext is TerminalTab tab)
        {
            _viewModel.SelectedTab = tab;
            tab.Activate(tab.MainPane);
        }
    }

    private void ShowToast(TerminalTab tab)
    {
        AttentionToast.DataContext = tab;
        AttentionToast.IsOpen = false;
        AttentionToast.IsOpen = IsActive;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        AttentionToast.IsOpen = false;
    }

    private static CustomPopupPlacement[] PlaceToastAtBottomRight(Size popupSize, Size targetSize, Point offset) =>
    [
        new(new Point(targetSize.Width - popupSize.Width - ToastMargin, targetSize.Height - popupSize.Height - ToastMargin), PopupPrimaryAxis.None),
    ];

    private void UseDarkTitleBar()
    {
        var enabled = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    private static T DataContextOf<T>(object sender) => (T)((FrameworkElement)sender).DataContext;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
