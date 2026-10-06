using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Pupitre.Terminal;

/// <summary>
/// Opens links found in terminal output. Only http(s): the output comes from programs and remote servers,
/// and other schemes (file:, ms-msdt:, custom protocol handlers) could launch local programs.
/// </summary>
public static class WebLink
{
    public static bool TryParse(string text, [NotNullWhen(true)] out Uri? uri) =>
        Uri.TryCreate(text, UriKind.Absolute, out uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public static void OpenInBrowser(Uri uri) =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
}
