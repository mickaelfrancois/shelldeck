using System.ComponentModel;
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
                FocusLauncherFilter();
                break;
            case ShortcutAction.CloseTab when _viewModel.SelectedTab is { } tab:
                CloseTab(tab);
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
        }
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
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (_viewModel.WindowPlacement is not { } placement || !placement.IsVisibleOn(virtualScreen))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        (Left, Top, Width, Height) = (placement.Left, placement.Top, placement.Width, placement.Height);
        WindowState = placement.IsMaximized ? WindowState.Maximized : WindowState.Normal;
    }

    // RestoreBounds keeps the normal size of a maximized window, so un-maximizing after a restart still works.
    private void SaveWindowPlacement() =>
        _viewModel.SaveWindowPlacement(new WindowPlacement(
            RestoreBounds.Left, RestoreBounds.Top, RestoreBounds.Width, RestoreBounds.Height,
            WindowState == WindowState.Maximized));

    private void OnNewTabClicked(object sender, RoutedEventArgs e) =>
        FocusLauncherFilter();

    private void FocusLauncherFilter()
    {
        // When the terminal has the keyboard, Win32 focus is in the WebView2 child window: take it back first.
        SetFocus(new WindowInteropHelper(this).Handle);
        FilterBox.Focus();
    }

    private void OnTogglePanelSideClicked(object sender, RoutedEventArgs e) =>
        _viewModel.TogglePanelSide();

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

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
