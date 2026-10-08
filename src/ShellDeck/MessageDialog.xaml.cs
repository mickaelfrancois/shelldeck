using System.Windows;
using System.Windows.Media;

namespace ShellDeck;

/// <summary>
/// Confirmations and error messages in the app's own chrome, where <see cref="MessageBox"/> would draw a light,
/// system-styled box. The button says what it does ("Fermer", "Coller"), not OK.
/// </summary>
public partial class MessageDialog : Window
{
    private MessageDialog(Window? owner, string heading, string detail, string confirmText, bool isError)
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
        DetailText.Text = detail;
        DetailText.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButton.Content = confirmText;

        var color = ((SolidColorBrush)FindResource(isError ? "ErrorBrush" : "AttentionBrush")).Color;
        Badge.Background = new SolidColorBrush(color) { Opacity = 0.18 };
        BadgeGlyph.Foreground = new SolidColorBrush(color);
        BadgeGlyph.Text = isError ? "✕" : "!";

        // An error has nothing to cancel: its one button closes it, with Enter or Escape alike.
        if (isError)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            ConfirmButton.IsCancel = true;
        }

        Loaded += (_, _) => ConfirmButton.Focus();
    }

    /// <summary>Asks before an action that loses something; true when the user confirms.</summary>
    public static bool Confirm(Window? owner, string heading, string detail, string confirmText) =>
        new MessageDialog(owner, heading, detail, confirmText, isError: false).ShowDialog() == true;

    public static void ShowError(Window? owner, string heading, string detail) =>
        new MessageDialog(owner, heading, detail, "Fermer", isError: true).ShowDialog();

    private void OnConfirmClicked(object sender, RoutedEventArgs e) => DialogResult = true;
}
