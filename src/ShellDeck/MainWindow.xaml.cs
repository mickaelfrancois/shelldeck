using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ShellDeck.Shells;
using ShellDeck.Terminal;
using ShellDeck.ViewModels;

namespace ShellDeck;

public partial class MainWindow : Window
{
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

    // Where the left button went down on a tab: a drag starts once the mouse has moved far enough from it.
    private Point? _tabDragStart;

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
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);

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
        Closed += (_, _) =>
        {
            _settingsWatcher.Dispose();
            _viewModel.EndAllSessions();
        };
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

    private void OnMoveProfileUpClicked(object sender, RoutedEventArgs e) =>
        _viewModel.MoveProfile(DataContextOf<ShellProfile>(sender), -1);

    private void OnMoveProfileDownClicked(object sender, RoutedEventArgs e) =>
        _viewModel.MoveProfile(DataContextOf<ShellProfile>(sender), 1);

    private void OnHideProfileClicked(object sender, RoutedEventArgs e) =>
        _viewModel.HideProfile(DataContextOf<ShellProfile>(sender));

    private void OnShowProfileClicked(object sender, RoutedEventArgs e) =>
        _viewModel.ShowProfile(DataContextOf<ShellProfile>(sender));

    private void OnAddProfileClicked(object sender, RoutedEventArgs e) => EditProfile(null);

    private void OnEditProfileClicked(object sender, RoutedEventArgs e) => EditProfile(DataContextOf<ShellProfile>(sender));

    /// <summary>Adds a terminal (<paramref name="profile"/> null), or edits one; tabs already open keep their settings.</summary>
    private void EditProfile(ShellProfile? profile)
    {
        var dialog = new ProfileDialog(profile) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (profile is { IsCustom: false })
        {
            _viewModel.SetStartingDirectory(profile, dialog.StartingDirectory);
            return;
        }

        _viewModel.SaveCustomProfile(new CustomProfile(
            profile?.Id ?? Guid.NewGuid().ToString(), dialog.ProfileName, dialog.ProfileCommandLine, dialog.StartingDirectory));
    }

    private void OnDeleteProfileClicked(object sender, RoutedEventArgs e)
    {
        var profile = DataContextOf<ShellProfile>(sender);
        if (Confirm($"Supprimer le terminal « {profile.Name} » ?", "Les onglets déjà ouverts restent ouverts.", "Supprimer"))
        {
            _viewModel.DeleteProfile(profile);
        }
    }

    /// <summary>SSH connections come from the user's OpenSSH config: they are edited there, never rewritten by the app.</summary>
    private void OnEditSshConfigClicked(object sender, RoutedEventArgs e) => OpenInEditor(SshConfig.UserConfigPath);

    private void OnCloseTabClicked(object sender, RoutedEventArgs e) =>
        CloseTab(DataContextOf<TerminalTab>(sender));

    private void OnDuplicateTabClicked(object sender, RoutedEventArgs e) =>
        _viewModel.Duplicate(DataContextOf<TerminalTab>(sender));

    private void OnSplitTabRightClicked(object sender, RoutedEventArgs e) => SplitTab(sender, SplitDirection.Right);

    private void OnSplitTabDownClicked(object sender, RoutedEventArgs e) => SplitTab(sender, SplitDirection.Down);

    /// <summary>The menu may come from a tab in the background: it is shown first, so the new pane is seen.</summary>
    private void SplitTab(object sender, SplitDirection direction) =>
        _viewModel.Split(DataContextOf<TerminalTab>(sender), direction);

    private void OnCloseTabsAboveClicked(object sender, RoutedEventArgs e) =>
        CloseTabs(_viewModel.TabsAbove(DataContextOf<TerminalTab>(sender)));

    private void OnCloseTabsBelowClicked(object sender, RoutedEventArgs e) =>
        CloseTabs(_viewModel.TabsBelow(DataContextOf<TerminalTab>(sender)));

    private void OnCloseOtherTabsClicked(object sender, RoutedEventArgs e) =>
        CloseTabs(_viewModel.OtherTabs(DataContextOf<TerminalTab>(sender)));

    /// <summary>
    /// Greys out the bulk closes that would close nothing, e.g. "above" on the first tab, and fills the
    /// "split with…" submenus with the launcher's current profiles.
    /// </summary>
    private void OnTabContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        var tab = (TerminalTab)menu.DataContext;
        foreach (var item in menu.Items.OfType<MenuItem>().Where(item => item.Tag is string))
        {
            item.IsEnabled = item.Tag switch
            {
                "Above" => _viewModel.TabsAbove(tab).Count > 0,
                "Below" => _viewModel.TabsBelow(tab).Count > 0,
                _ => _viewModel.OtherTabs(tab).Count > 0,
            };
        }

        foreach (var item in menu.Items.OfType<MenuItem>().Where(item => item.Tag is SplitDirection))
        {
            FillSplitMenu(item, tab, (SplitDirection)item.Tag);
        }
    }

    /// <summary>One entry per visible profile, shells then SSH connections, read anew at each opening.</summary>
    private void FillSplitMenu(MenuItem menu, TerminalTab tab, SplitDirection direction)
    {
        var shells = _viewModel.SplitShells;
        var connections = _viewModel.SplitSshConnections;
        menu.Items.Clear();
        AddSplitEntries(menu, tab, direction, shells);
        if (shells.Count > 0 && connections.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        AddSplitEntries(menu, tab, direction, connections);
        menu.IsEnabled = menu.Items.Count > 0;
    }

    private void AddSplitEntries(MenuItem menu, TerminalTab tab, SplitDirection direction, IEnumerable<ShellProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            // A TextBlock shows the name as is: a string header could read "_" as an access key.
            var entry = new MenuItem { Header = new TextBlock { Text = profile.Name } };
            entry.Click += (_, _) => _viewModel.Split(tab, direction, profile);
            menu.Items.Add(entry);
        }
    }

    private void OnEditTabColorClicked(object sender, RoutedEventArgs e)
    {
        TabColorFlyout.DataContext = DataContextOf<TerminalTab>(sender);
        TabColorFlyout.IsOpen = true;
    }

    private void OnTabColorClicked(object sender, RoutedEventArgs e) => SetTabColor(DataContextOf<TabColor>(sender));

    private void OnResetTabColorClicked(object sender, RoutedEventArgs e) => SetTabColor(TabColor.None);

    private void OnCustomTabColorClicked(object sender, RoutedEventArgs e)
    {
        var current = (TabColorFlyout.DataContext as TerminalTab)?.Color.Hex;
        TabColorFlyout.IsOpen = false;
        if (ColorPicker.Pick(this, current) is { } hex)
        {
            SetTabColor(TabColor.Custom(hex));
        }
    }

    /// <summary>Gives the color to the tab the flyout was opened on, and closes it.</summary>
    private void SetTabColor(TabColor color)
    {
        TabColorFlyout.IsOpen = false;
        if (TabColorFlyout.DataContext is TerminalTab tab)
        {
            tab.Color = color;
        }
    }

    private void OnTabMouseDown(object sender, MouseButtonEventArgs e) => _tabDragStart = e.GetPosition(this);

    private void OnTabMouseMove(object sender, MouseEventArgs e)
    {
        if (_tabDragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var offset = e.GetPosition(this) - start;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _tabDragStart = null;
        var tab = DataContextOf<TerminalTab>(sender);
        var originalIndex = _viewModel.Tabs.IndexOf(tab);
        tab.IsDragged = true;
        try
        {
            // The tab moves while it is dragged (OnTabListDragOver): dropped outside the list, or Escape, puts it back.
            if (DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(TerminalTab), tab), DragDropEffects.Move) == DragDropEffects.None)
            {
                _viewModel.MoveTab(tab, originalIndex);
            }
        }
        finally
        {
            tab.IsDragged = false;
        }
    }

    private void OnTabListDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(typeof(TerminalTab)) is not TerminalTab dragged)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;
        if (ItemsControl.ContainerFromElement(TabList, (DependencyObject)e.OriginalSource) is ListBoxItem { DataContext: TerminalTab target })
        {
            _viewModel.MoveTab(dragged, _viewModel.Tabs.IndexOf(target));
        }
    }

    // The order is already in place: the drop only tells the drag it ended in the list.
    private void OnTabListDrop(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TerminalTab)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

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
            case ShortcutAction.OpenSearch:
                _viewModel.SelectedTab?.ActivePane.RequestSearch();
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
        if (pane.HasActivity && !Confirm("Fermer ce volet ?", interruption, "Fermer"))
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
        if (tab.HasActivity && !Confirm($"Fermer « {tab.Title} » ?", InterruptionWarning(tab), "Fermer"))
        {
            return;
        }

        _viewModel.Close(tab);
    }

    /// <summary>One confirmation for all the tabs, listing those with something running.</summary>
    private void CloseTabs(IReadOnlyList<TerminalTab> tabs)
    {
        var heading = tabs.Count == 1 ? $"Fermer « {tabs[0].Title} » ?" : $"Fermer {tabs.Count} onglets ?";
        if (!ConfirmActivity(tabs, heading, "Fermer"))
        {
            return;
        }

        foreach (var tab in tabs)
        {
            _viewModel.Close(tab);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmActivity(_viewModel.Tabs, $"Quitter {App.ProductName} ?", "Quitter"))
        {
            e.Cancel = true;
            return;
        }

        SaveWindowPlacement();
    }

    /// <summary>True when none of <paramref name="tabs"/> has activity, or the user accepts to interrupt it.</summary>
    private bool ConfirmActivity(IEnumerable<TerminalTab> tabs, string heading, string confirmText)
    {
        var activeTabs = tabs.Where(tab => tab.HasActivity).ToList();
        if (activeTabs.Count == 0)
        {
            return true;
        }

        var list = string.Join("\n", activeTabs.Select(tab => $"  • {tab.Title} : {ActivityName(tab)}"));
        return Confirm(heading, $"Ces onglets ont une activité en cours :\n{list}", confirmText);
    }

    private static string ActivityName(TerminalTab tab) =>
        tab.IsClaudeSession ? "Claude Code" : "commande en cours";

    private static string InterruptionWarning(TerminalTab tab) =>
        tab.IsClaudeSession ? "Claude Code sera interrompu." : "La commande en cours sera interrompue.";

    private bool Confirm(string heading, string detail, string confirmText) =>
        MessageDialog.Confirm(this, heading, detail, confirmText);

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

    /// <summary>Opens settings.json, creating it first so the editor has something to show.</summary>
    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(AppSettings.FilePath))
        {
            _viewModel.SaveCurrentSettings();
        }

        OpenInEditor(AppSettings.FilePath);
    }

    /// <summary>The editor associated with the file type, Notepad when there is none (~/.ssh/config has no extension).</summary>
    private static void OpenInEditor(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
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

    private static T DataContextOf<T>(object sender) => (T)((FrameworkElement)sender).DataContext;
}
