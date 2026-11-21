using System.Text;

namespace ShellDeck.TabLines;

/// <summary>What the last run of the tab command did for one tab, for the "Diagnostic de la commande" window.</summary>
public sealed record TabCommandDiagnostic(DateTimeOffset At, IReadOnlyList<string> Command, string Input, TabCommandRun Run, int? FieldCount)
{
    /// <summary>The report, in plain French; the output and the error are cut so a chatty script cannot flood the window.</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine($"Commande : {string.Join(' ', Command.Select(Quote))}");
        text.AppendLine($"Lancée à : {At.LocalDateTime:HH:mm:ss} (durée {Run.Elapsed.TotalMilliseconds:0} ms, délai maximum {TabFieldsRefresher.DefaultTimeout.TotalSeconds:0} s)");
        text.AppendLine($"Résultat : {Verdict()}");
        if (Run.ExitCode is { } code)
        {
            text.AppendLine($"Code de sortie : {code}");
        }

        text.AppendLine();
        text.AppendLine("Entrée envoyée :");
        text.AppendLine(Input);
        text.AppendLine();
        text.AppendLine("Sortie standard :");
        text.AppendLine(Section(Run.Output));
        text.AppendLine();
        text.AppendLine("Erreur standard :");
        text.AppendLine(Section(Run.Error));
        return text.ToString().TrimEnd();
    }

    public string Verdict() => Run.Failure switch
    {
        TabCommandFailure.NotStarted => "la commande n'a pas pu démarrer (programme introuvable ?). Sans shell, `~` et les variables ne sont pas développés : donnez un chemin complet.",
        TabCommandFailure.TimedOut => "trop lente, arrêtée. Les valeurs précédentes sont gardées.",
        TabCommandFailure.ExitCode => "la commande a échoué (code de sortie différent de 0). Les valeurs précédentes sont gardées.",
        TabCommandFailure.TooMuchOutput => $"trop de sortie (plus de {TabCommandOutput.MaxOutputBytes / 1024} Ko). Les valeurs précédentes sont gardées.",
        _ when FieldCount is null => "la sortie n'est pas un objet JSON. Les valeurs précédentes sont gardées.",
        _ when FieldCount == 0 => "objet JSON valide, mais sans aucun champ utilisable (valeurs vides, noms invalides ?).",
        _ => $"réussi, {FieldCount} champ(s) fourni(s).",
    };

    private static string Section(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(vide)";
        }

        const int Max = 2000;
        return text.Length > Max ? text[..Max] + "\n… (coupé)" : text.TrimEnd();
    }

    private static string Quote(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;
}
