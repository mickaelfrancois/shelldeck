using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShellDeck.TabLines;

/// <summary>
/// Reads what the tab command printed: one JSON object whose keys become fields (<c>{model}</c>) and whose values are
/// a text, or <c>{"text": "...", "style": "attention"}</c>. The output is not trusted: control characters (line breaks,
/// escape sequences) become spaces, texts and field counts are capped, and a key that is not a plain name is dropped.
/// </summary>
public static partial class TabCommandOutput
{
    public const int MaxFields = 32;
    public const int MaxTextLength = 200;
    public const int MaxOutputBytes = 64 * 1024;

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.-]{0,31}$")]
    private static partial Regex ValidName();

    /// <summary>The fields, or null when the output is not a JSON object (the caller keeps what it had).</summary>
    public static IReadOnlyDictionary<string, TabSegment>? Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output) || output.Length > MaxOutputBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 8 });
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            var fields = new Dictionary<string, TabSegment>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (fields.Count >= MaxFields)
                {
                    break;
                }

                if (ValidName().IsMatch(property.Name) && Read(property.Value) is { } segment)
                {
                    fields[property.Name] = segment;
                }
            }

            return fields;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TabSegment? Read(JsonElement value)
    {
        var (text, style) = value.ValueKind switch
        {
            JsonValueKind.String => (value.GetString(), SegmentStyle.Muted),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => (value.ToString(), SegmentStyle.Muted),
            JsonValueKind.Object when value.TryGetProperty("text", out var inner) && inner.ValueKind is JsonValueKind.String =>
                (inner.GetString(), value.TryGetProperty("style", out var named) && named.ValueKind is JsonValueKind.String ? Style(named.GetString()) : SegmentStyle.Muted),
            _ => ((string?)null, SegmentStyle.Muted),
        };

        var clean = Clean(text);
        return clean.Length == 0 ? null : new TabSegment(clean, style, 1);
    }

    private static SegmentStyle Style(string? name) => name?.ToLowerInvariant() switch
    {
        "normal" => SegmentStyle.Normal,
        "strong" => SegmentStyle.Strong,
        "attention" => SegmentStyle.Attention,
        "dirty" => SegmentStyle.GitDirty,
        "ahead" => SegmentStyle.GitAhead,
        "behind" => SegmentStyle.GitBehind,
        _ => SegmentStyle.Muted,
    };

    private static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var cleaned = new string([.. text.Select(character => char.IsControl(character) ? ' ' : character)]).Trim();
        return cleaned.Length > MaxTextLength ? cleaned[..MaxTextLength] : cleaned;
    }
}
