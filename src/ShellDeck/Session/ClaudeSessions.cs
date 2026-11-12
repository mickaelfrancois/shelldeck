using System.IO;
using System.Text;
using System.Text.Json;

namespace ShellDeck.Session;

/// <summary>What a Claude Code transcript says about its conversation.</summary>
public sealed record ClaudeSessionInfo(string SessionId, string? Cwd);

/// <summary>Finds the Claude Code conversation a pane is running. Behind an interface: a hook-based source can come first later.</summary>
public interface IClaudeSessionLocator
{
    /// <summary>
    /// The conversation written to since <paramref name="sinceUtc"/> and not already <paramref name="claimed"/> by another pane;
    /// null when nothing is found or anything goes wrong. Reads the disk: call it off the UI thread.
    /// </summary>
    /// <remarks>
    /// <paramref name="knownId"/> is the conversation the pane already has: it is kept when its transcript was written since
    /// <paramref name="knownSinceUtc"/> (the previous resolution, else <paramref name="sinceUtc"/>), so two panes in one
    /// directory do not swap conversations when one finishes a turn while the other writes.
    /// </remarks>
    ClaudeSessionInfo? Locate(string? cwd, DateTime sinceUtc, IReadOnlySet<string> claimed, string? knownId = null, DateTime? knownSinceUtc = null);
}

/// <summary>The command typed in a restored pane to bring its Claude Code conversation back.</summary>
public static class ResumeCommand
{
    public const string Continue = "claude --continue\r";

    /// <summary>
    /// The id ends up typed in a shell, and it comes from a file anyone can edit or from a file name: only a strict GUID
    /// goes into the command, anything else gives <c>claude --continue</c>.
    /// </summary>
    public static string For(string? sessionId) =>
        sessionId is { Length: 36 } && Guid.TryParseExact(sessionId, "D", out _)
            ? $"claude --resume {sessionId}\r"
            : Continue;
}

/// <summary>
/// Reads <c>~/.claude/projects/&lt;encoded cwd&gt;/&lt;uuid&gt;.jsonl</c>. The layout and the <c>sessionId</c>/<c>cwd</c> fields are
/// internal to Claude Code and not documented: every doubt gives null, and the pane falls back to <c>claude --continue</c>.
/// </summary>
public sealed class TranscriptSessionLocator : IClaudeSessionLocator
{
    private const int MaxLinesRead = 20;
    private const int MaxBytesRead = 64 * 1024;

    private readonly string _projectsRoot;

    public TranscriptSessionLocator(string projectsRoot) => _projectsRoot = projectsRoot;

    public static TranscriptSessionLocator ForCurrentUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects"));

    /// <summary>Claude names the project folder after the cwd, every character that is not a letter or a digit becoming '-' (observed).</summary>
    public static string EncodeDirectory(string cwd) =>
        string.Create(cwd.Length, cwd, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.IsAsciiLetterOrDigit(source[i]) ? source[i] : '-';
            }
        });

    public ClaudeSessionInfo? Locate(string? cwd, DateTime sinceUtc, IReadOnlySet<string> claimed, string? knownId = null, DateTime? knownSinceUtc = null)
    {
        try
        {
            if (!Directory.Exists(_projectsRoot))
            {
                return null;
            }

            var folders = FoldersToSearch(cwd, sinceUtc).ToList();
            if (knownId is { Length: > 0 } && !claimed.Contains(knownId))
            {
                var knownSince = knownSinceUtc ?? sinceUtc;
                var known = folders
                    .Select(folder => Path.Combine(folder, knownId + ".jsonl"))
                    .FirstOrDefault(path => File.Exists(path) && File.GetLastWriteTimeUtc(path) >= knownSince);
                if (known is not null && ReadInfo(known) is { } knownInfo && string.Equals(knownInfo.SessionId, knownId, StringComparison.OrdinalIgnoreCase))
                {
                    return knownInfo;
                }
            }

            var candidates = folders
                .SelectMany(folder => Directory.EnumerateFiles(folder, "*.jsonl"))
                .Select(path => (Path: path, Written: File.GetLastWriteTimeUtc(path)))
                .Where(file => file.Written >= sinceUtc && !claimed.Contains(Path.GetFileNameWithoutExtension(file.Path)))
                .OrderByDescending(file => file.Written);

            foreach (var file in candidates)
            {
                if (ReadInfo(file.Path) is { } info && !claimed.Contains(info.SessionId))
                {
                    return info;
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private IEnumerable<string> FoldersToSearch(string? cwd, DateTime sinceUtc)
    {
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            var folder = Path.Combine(_projectsRoot, EncodeDirectory(cwd));
            if (Directory.Exists(folder))
            {
                return [folder];
            }
        }

        // cwd unknown (pwsh and cmd report none by default): the projects touched since Claude was seen.
        return Directory.EnumerateDirectories(_projectsRoot).Where(folder => Directory.GetLastWriteTimeUtc(folder) >= sinceUtc);
    }

    private static ClaudeSessionInfo? ReadInfo(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(new LimitedStream(stream, MaxBytesRead), Encoding.UTF8);
            string? sessionId = null;
            string? cwd = null;
            for (var lineNumber = 0; lineNumber < MaxLinesRead && reader.ReadLine() is { } line; lineNumber++)
            {
                if (!TryReadFields(line, out var lineSessionId, out var lineCwd))
                {
                    continue;
                }

                sessionId ??= lineSessionId;
                cwd ??= lineCwd;
                if (sessionId is not null && cwd is not null)
                {
                    break;
                }
            }

            return sessionId is null ? null : new ClaudeSessionInfo(sessionId, cwd);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryReadFields(string line, out string? sessionId, out string? cwd)
    {
        sessionId = cwd = null;
        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return false;
            }

            sessionId = StringProperty(document.RootElement, "sessionId");
            cwd = StringProperty(document.RootElement, "cwd");
            return sessionId is not null || cwd is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    /// <summary>Stops reading after a fixed number of bytes: a transcript can be huge and its first lines are enough.</summary>
    private sealed class LimitedStream(Stream inner, int limit) : Stream
    {
        private int _remaining = limit;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var read = inner.Read(buffer, offset, Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }
    }
}
