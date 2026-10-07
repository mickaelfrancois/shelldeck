using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ShellDeck.Shells;

namespace ShellDeck;

/// <summary>Adds a terminal, or edits one: every field of a terminal the user added, the starting directory of a discovered one.</summary>
public partial class ProfileDialog : Window
{
    /// <param name="profile">The terminal to edit; null to add one.</param>
    public ProfileDialog(ShellProfile? profile)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);

        var isCustom = profile is null || profile.IsCustom;
        var isWsl = profile?.Kind is ShellKind.Wsl;
        Title = profile is null ? "Ajouter un terminal" : $"Modifier « {profile.Name} »";
        NameBox.Text = profile?.Name ?? string.Empty;
        CommandLineBox.Text = profile?.CommandLine ?? string.Empty;
        DirectoryBox.Text = profile?.Location ?? string.Empty;
        NameBox.IsEnabled = isCustom;
        CommandLineBox.IsEnabled = isCustom;
        DiscoveredHint.Visibility = isCustom ? Visibility.Collapsed : Visibility.Visible;
        BrowseButton.Visibility = isWsl ? Visibility.Collapsed : Visibility.Visible;
        DirectoryHint.Text = isWsl
            ? "Chemin Linux, ~ par défaut."
            : "Vide : dossier de l'utilisateur. %VARIABLES% et ~ sont acceptés.";

        Loaded += (_, _) =>
        {
            var first = isCustom ? NameBox : DirectoryBox;
            first.Focus();
            first.SelectAll();
        };
        UpdateSaveButton();
    }

    public string ProfileName => NameBox.Text.Trim();

    public string ProfileCommandLine => CommandLineBox.Text.Trim();

    public string StartingDirectory => DirectoryBox.Text.Trim();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateSaveButton();

    private void UpdateSaveButton() =>
        SaveButton.IsEnabled = ProfileName.Length > 0 && ProfileCommandLine.Length > 0;

    private void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        var current = Environment.ExpandEnvironmentVariables(StartingDirectory);
        var dialog = new OpenFolderDialog
        {
            Title = "Répertoire de démarrage",
            InitialDirectory = Directory.Exists(current) ? current : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dialog.ShowDialog(this) == true)
        {
            DirectoryBox.Text = dialog.FolderName;
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e) => DialogResult = true;
}
