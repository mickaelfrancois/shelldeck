using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Pupitre.Shells;

namespace Pupitre.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int LastTabNumber = 9;

    private readonly ListCollectionView _shells;
    private readonly ListCollectionView _sshConnections;
    private readonly IReadOnlyList<ShellProfile> _allProfiles;
    private readonly Action<AppSettings> _saveSettings;
    private AppSettings _settings;
    private TerminalTab? _selectedTab;
    private string _filterText = string.Empty;
    private bool _isSshExpanded;

    public MainViewModel(
        IReadOnlyList<ShellProfile> shells,
        IReadOnlyList<ShellProfile> sshConnections,
        AppSettings settings,
        Action<AppSettings> saveSettings)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        _allProfiles = [.. shells, .. sshConnections];
        _shells = new ListCollectionView(shells.ToList()) { Filter = MatchesFilter };
        _sshConnections = new ListCollectionView(sshConnections.ToList()) { Filter = MatchesFilter };
        SshConnectionCount = sshConnections.Count;
    }

    /// <summary>Raised when a tab the user is not looking at needs them.</summary>
    public event EventHandler<TerminalTab>? AttentionRequested;

    public ObservableCollection<TerminalTab> Tabs { get; } = [];

    public ICollectionView Shells => _shells;

    public ICollectionView SshConnections => _sshConnections;

    public int SshConnectionCount { get; }

    public string SshHeader => IsFiltering
        ? $"SSH · {_sshConnections.Count}/{SshConnectionCount}"
        : $"SSH · {SshConnectionCount}";

    public bool IsSshExpanded
    {
        get => _isSshExpanded;
        set
        {
            if (SetField(ref _isSshExpanded, value))
            {
                OnPropertyChanged(nameof(IsSshListVisible));
            }
        }
    }

    /// <summary>Typing a filter reveals the matching connections even when the section is collapsed.</summary>
    public bool IsSshListVisible => IsSshExpanded || IsFiltering;

    public bool HasNoSshMatch => IsSshListVisible && _sshConnections.IsEmpty;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetField(ref _filterText, value))
            {
                return;
            }

            _shells.Refresh();
            _sshConnections.Refresh();
            OnPropertyChanged(nameof(SshHeader));
            OnPropertyChanged(nameof(IsSshListVisible));
            OnPropertyChanged(nameof(HasNoSshMatch));
        }
    }

    public PanelSide PanelSide
    {
        get => _settings.PanelSide;
        set
        {
            if (value == _settings.PanelSide)
            {
                return;
            }

            UpdateSettings(_settings with { PanelSide = value });
            OnPropertyChanged();
        }
    }

    public TerminalTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            var previous = _selectedTab;
            if (!SetField(ref _selectedTab, value))
            {
                return;
            }

            previous?.IsSelected = false;
            value?.IsSelected = true;
        }
    }

    /// <summary>Opened at startup: the chosen profile while it still exists, else the first shell found.</summary>
    public ShellProfile? DefaultProfile =>
        _allProfiles.FirstOrDefault(profile => profile.Name == _settings.DefaultProfileName)
        ?? _allProfiles.FirstOrDefault(profile => profile.Kind is not ShellKind.Ssh);

    public string? DefaultProfileName => DefaultProfile?.Name;

    private bool IsFiltering => FilterText.Trim().Length > 0;

    public WindowPlacement? WindowPlacement => _settings.Window;

    public void SaveWindowPlacement(WindowPlacement placement) =>
        UpdateSettings(_settings with { Window = placement });

    public void SetDefaultProfile(ShellProfile profile)
    {
        UpdateSettings(_settings with { DefaultProfileName = profile.Name });
        OnPropertyChanged(nameof(DefaultProfile));
        OnPropertyChanged(nameof(DefaultProfileName));
    }

    public void Open(ShellProfile profile)
    {
        var tab = new TerminalTab(profile);
        tab.AttentionRequested += (_, _) => AttentionRequested?.Invoke(this, tab);
        tab.Ended += (_, _) => Close(tab);
        Tabs.Add(tab);
        SelectedTab = tab;
        FilterText = string.Empty;
    }

    /// <summary>A new tab without choosing: the default profile, as Windows Terminal's Ctrl+Shift+T.</summary>
    public void OpenDefault()
    {
        if (DefaultProfile is { } defaultProfile)
        {
            Open(defaultProfile);
        }
    }

    /// <summary>Opens the first shell, else the first SSH connection, matching the filter; the default profile when there is no filter.</summary>
    public void OpenFirstMatch()
    {
        if (!IsFiltering)
        {
            OpenDefault();
            return;
        }

        var firstMatch = _shells.Cast<ShellProfile>().FirstOrDefault() ?? _sshConnections.Cast<ShellProfile>().FirstOrDefault();
        if (firstMatch is not null)
        {
            Open(firstMatch);
        }
    }

    public void Close(TerminalTab tab)
    {
        // Captured first: removing the selected item makes the tab list push a null selection back to us.
        var wasSelected = SelectedTab == tab || SelectedTab is null;
        var closedIndex = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (wasSelected)
        {
            SelectedTab = Tabs.ElementAtOrDefault(Math.Min(closedIndex, Tabs.Count - 1));
        }
    }

    public void SelectNextTab() => SelectTabAtOffset(1);

    public void SelectPreviousTab() => SelectTabAtOffset(-1);

    /// <summary>Selects tab 1 to 8 by position; 9 always means the last tab, as in browsers.</summary>
    public void SelectTabByNumber(int number)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        if (number == LastTabNumber)
        {
            SelectedTab = Tabs[^1];
            return;
        }

        if (number >= 1 && number <= Tabs.Count)
        {
            SelectedTab = Tabs[number - 1];
        }
    }

    public void TogglePanelSide() =>
        PanelSide = PanelSide is PanelSide.Left ? PanelSide.Right : PanelSide.Left;

    private void SelectTabAtOffset(int offset)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedTab is null ? -1 : Tabs.IndexOf(SelectedTab);
        if (currentIndex < 0)
        {
            SelectedTab = offset > 0 ? Tabs[0] : Tabs[^1];
            return;
        }

        SelectedTab = Tabs[(currentIndex + offset + Tabs.Count) % Tabs.Count];
    }

    /// <summary>Writes the settings as they are, e.g. to create the file before opening it in an editor.</summary>
    public void SaveCurrentSettings() => _saveSettings(_settings);

    /// <summary>The settings file was edited by hand: take its values, without writing them back.</summary>
    public void ReloadSettings(AppSettings settings)
    {
        if (settings == _settings)
        {
            return;
        }

        var previous = _settings;
        _settings = settings;
        if (settings.PanelSide != previous.PanelSide)
        {
            OnPropertyChanged(nameof(PanelSide));
        }

        OnPropertyChanged(nameof(DefaultProfile));
        OnPropertyChanged(nameof(DefaultProfileName));
    }

    private void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _saveSettings(settings);
    }

    private bool MatchesFilter(object item)
    {
        var filter = FilterText.Trim();
        return filter.Length == 0
            || item is ShellProfile profile
            && (profile.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || profile.Kind is ShellKind.Ssh && profile.Location.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }
}
