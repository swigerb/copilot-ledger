namespace CopilotLedger.Core.Git;

public sealed class FileSystemGitContextResolver : IGitContextResolver
{
    private readonly string? _ceilingDirectory;
    private readonly StringComparison _pathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public FileSystemGitContextResolver(string? ceilingDirectory = null)
    {
        _ceilingDirectory = string.IsNullOrWhiteSpace(ceilingDirectory)
            ? null
            : TrimSeparators(Path.GetFullPath(ceilingDirectory));
    }

    public GitWorkspaceContext? Resolve(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return null;
        }

        DirectoryInfo? directory = new(TrimSeparators(Path.GetFullPath(workingDirectory)));
        while (directory is not null)
        {
            string repoRoot = TrimSeparators(directory.FullName);
            string gitEntry = Path.Combine(repoRoot, ".git");
            string? gitDirectory = ResolveGitDirectory(repoRoot, gitEntry);
            if (gitDirectory?.Length == 0)
            {
                return null;
            }

            if (gitDirectory is not null)
            {
                gitDirectory = Path.GetFullPath(gitDirectory);
                if (!File.Exists(Path.Combine(gitDirectory, "HEAD")))
                {
                    return null;
                }

                string commonDirectory = ResolveCommonDirectory(gitDirectory);
                string? branch = null;
                string? commit = null;
                string? head = ReadFirstLine(Path.Combine(gitDirectory, "HEAD"));
                if (head is null)
                {
                    return null;
                }

                if (head.Length > 0)
                {
                    if (head.StartsWith("ref: ", StringComparison.Ordinal))
                    {
                        string reference = head[5..].Trim();
                        branch = reference.StartsWith("refs/heads/", StringComparison.Ordinal)
                            ? reference["refs/heads/".Length..]
                            : reference;
                        commit = ResolveRef(gitDirectory, commonDirectory, reference, 0);
                    }
                    else if (IsCommitSha(head))
                    {
                        commit = head.ToLowerInvariant();
                    }
                }

                string? remoteUrl = null;
                string configPath = Path.Combine(commonDirectory, "config");
                if (File.Exists(configPath))
                {
                    remoteUrl = ReadOriginUrl(configPath);
                }

                return new GitWorkspaceContext(
                    repoRoot,
                    gitDirectory,
                    branch,
                    commit,
                    remoteUrl,
                    remoteUrl is null ? null : GitRemoteUrl.GetRepositorySlug(remoteUrl));
            }

            if (_ceilingDirectory is not null &&
                repoRoot.Equals(_ceilingDirectory, _pathComparison))
            {
                break;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? ResolveGitDirectory(string repoRoot, string gitEntry)
    {
        if (Directory.Exists(gitEntry))
        {
            return gitEntry;
        }

        if (!File.Exists(gitEntry))
        {
            return null;
        }

        if (!TryReadFirstLine(gitEntry, trim: false, out string line))
        {
            return null;
        }

        if (!line.StartsWith("gitdir:", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string gitDirectory = line["gitdir:".Length..].Trim();
        if (gitDirectory.Length == 0)
        {
            return string.Empty;
        }

        return Path.GetFullPath(
            Path.IsPathRooted(gitDirectory)
                ? gitDirectory
                : Path.Combine(repoRoot, gitDirectory));
    }

    private static string ResolveCommonDirectory(string gitDirectory)
    {
        string commonDirectoryFile = Path.Combine(gitDirectory, "commondir");
        if (!File.Exists(commonDirectoryFile))
        {
            return gitDirectory;
        }

        string? commonDirectory = ReadFirstLine(commonDirectoryFile);
        if (string.IsNullOrWhiteSpace(commonDirectory))
        {
            return gitDirectory;
        }

        return Path.GetFullPath(
            Path.IsPathRooted(commonDirectory)
                ? commonDirectory
                : Path.Combine(gitDirectory, commonDirectory));
    }

    private static string? ResolveRef(
        string gitDirectory,
        string commonDirectory,
        string reference,
        int depth)
    {
        if (depth >= 5)
        {
            return null;
        }

        string relativePath = reference.Replace('/', Path.DirectorySeparatorChar);
        string[] looseReferences =
        [
            Path.Combine(gitDirectory, relativePath),
            Path.Combine(commonDirectory, relativePath),
        ];
        foreach (string looseReference in looseReferences.Distinct(OperatingSystem.IsWindows()
                     ? StringComparer.OrdinalIgnoreCase
                     : StringComparer.Ordinal))
        {
            if (!File.Exists(looseReference) ||
                !TryReadFirstLine(looseReference, trim: true, out string value))
            {
                continue;
            }

            if (value.StartsWith("ref: ", StringComparison.Ordinal))
            {
                return ResolveRef(
                    gitDirectory,
                    commonDirectory,
                    value[5..].Trim(),
                    depth + 1);
            }

            return IsCommitSha(value) ? value.ToLowerInvariant() : null;
        }

        string? packedRefs = ReadAllLines(Path.Combine(commonDirectory, "packed-refs"));
        if (packedRefs is null)
        {
            return null;
        }

        foreach (string rawLine in packedRefs.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line[0] is '#' or '^')
            {
                continue;
            }

            int space = line.IndexOf(' ');
            if (space < 0)
            {
                continue;
            }

            string sha = line[..space];
            string name = line[(space + 1)..];
            if (name.Equals(reference, StringComparison.Ordinal) && IsCommitSha(sha))
            {
                return sha.ToLowerInvariant();
            }
        }

        return null;
    }

    private static string? ReadOriginUrl(string configPath)
    {
        string? contents = ReadAllLines(configPath);
        if (contents is null)
        {
            return null;
        }

        bool originSection = false;
        foreach (string rawLine in contents.Split('\n'))
        {
            string line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            if (TryReadSection(line, out string name, out string? subsection))
            {
                originSection =
                    name.Equals("remote", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(subsection, "origin", StringComparison.Ordinal);
                continue;
            }

            if (!originSection)
            {
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals < 0 || !line[..equals].Trim().Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? value = ReadConfigValue(line[(equals + 1)..]);
            return value is null ? null : GitRemoteUrl.Sanitize(value);
        }

        return null;
    }

    private static bool TryReadSection(string line, out string name, out string? subsection)
    {
        name = string.Empty;
        subsection = null;
        if (line.Length < 3 || line[0] != '[' || line[^1] != ']')
        {
            return false;
        }

        string contents = line[1..^1];
        int quote = contents.IndexOf('"');
        if (quote < 0)
        {
            name = contents.Trim();
            return true;
        }

        name = contents[..quote].Trim();
        string quoted = contents[quote..].Trim();
        if (quoted.Length < 2 || quoted[0] != '"' || quoted[^1] != '"')
        {
            return false;
        }

        subsection = UnescapeConfigSubsection(quoted[1..^1]);
        return true;
    }

    private static string UnescapeConfigSubsection(string value)
    {
        return value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string? ReadConfigValue(string value)
    {
        string trimmed = value.Trim();
        if (!trimmed.StartsWith('"'))
        {
            int comment = IndexOfFirst(trimmed, '#', ';');
            return (comment >= 0 ? trimmed[..comment] : trimmed).Trim();
        }

        bool escaped = false;
        for (int index = 1; index < trimmed.Length; index++)
        {
            char character = trimmed[index];
            if (character == '"' && !escaped)
            {
                return trimmed[1..index];
            }

            escaped = character == '\\' && !escaped;
            if (character != '\\')
            {
                escaped = false;
            }
        }

        return null;
    }

    private static int IndexOfFirst(string value, char first, char second)
    {
        int firstIndex = value.IndexOf(first);
        int secondIndex = value.IndexOf(second);
        return firstIndex < 0 ? secondIndex :
            secondIndex < 0 ? firstIndex :
            Math.Min(firstIndex, secondIndex);
    }

    private static string? ReadFirstLine(string path)
    {
        return TryReadFirstLine(path, trim: true, out string line) ? line : null;
    }

    private static bool TryReadFirstLine(string path, bool trim, out string line)
    {
        try
        {
            using StreamReader reader = File.OpenText(path);
            string? firstLine = reader.ReadLine();
            line = firstLine is null
                ? string.Empty
                : trim
                    ? firstLine.Trim()
                    : firstLine;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            line = string.Empty;
            return false;
        }
    }

    private static string? ReadAllLines(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsCommitSha(string value)
    {
        return value.Length is 40 or 64 &&
            value.All(Uri.IsHexDigit);
    }

    private static string TrimSeparators(string path) =>
        Path.TrimEndingDirectorySeparator(path);
}
