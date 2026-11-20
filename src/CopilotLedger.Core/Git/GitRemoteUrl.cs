namespace CopilotLedger.Core.Git;

public static class GitRemoteUrl
{
    public static string Sanitize(string remoteUrl)
    {
        ArgumentNullException.ThrowIfNull(remoteUrl);
        string value = remoteUrl.Trim();
        int schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return value;
        }

        string scheme = value[..schemeEnd];
        if (!scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
            !scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        int authorityStart = schemeEnd + 3;
        int pathStart = value.IndexOf('/', authorityStart);
        int authorityEnd = pathStart < 0 ? value.Length : pathStart;
        int lastAt = value.LastIndexOf('@', authorityEnd - 1);
        return lastAt >= authorityStart
            ? value[..authorityStart] + value[(lastAt + 1)..]
            : value;
    }

    public static string? GetRepositorySlug(string remoteUrl)
    {
        ArgumentNullException.ThrowIfNull(remoteUrl);
        string value = remoteUrl.Trim();
        if (value.EndsWith('/'))
        {
            value = value[..^1];
        }

        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        string path;
        int schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            if (value[..schemeEnd].Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            int pathStart = value.IndexOf('/', schemeEnd + 3);
            if (pathStart < 0)
            {
                return null;
            }

            path = value[(pathStart + 1)..];
        }
        else
        {
            int colon = value.IndexOf(':');
            int slash = value.IndexOf('/');
            if (colon < 0 ||
                (slash >= 0 && colon > slash) ||
                IsWindowsDrivePath(value, colon))
            {
                return null;
            }

            path = value[(colon + 1)..];
        }

        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2 ? $"{segments[0]}/{segments[1]}" : null;
    }

    private static bool IsWindowsDrivePath(string value, int colon) =>
        colon == 1 &&
        char.IsLetter(value[0]) &&
        value.Length > 2 &&
        (value[2] is '/' or '\\');
}
