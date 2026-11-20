namespace CopilotLedger.Core.Git;

public sealed class CachingGitContextResolver : IGitContextResolver
{
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, CacheEntry> _cache;
    private readonly Lock _lock = new();
    private readonly IGitContextResolver _inner;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeToLive;

    // null timeProvider uses TimeProvider.System; null timeToLive uses DefaultTimeToLive.
    // Throws ArgumentNullException for a null inner, and ArgumentOutOfRangeException when timeToLive <= TimeSpan.Zero.
    public CachingGitContextResolver(
        IGitContextResolver inner,
        TimeProvider? timeProvider = null,
        TimeSpan? timeToLive = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _timeToLive = timeToLive ?? DefaultTimeToLive;
        if (_timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive));
        }

        _inner = inner;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cache = new(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
    }

    public GitWorkspaceContext? Resolve(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return _inner.Resolve(workingDirectory);
        }

        lock (_lock)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (_cache.TryGetValue(workingDirectory, out CacheEntry? entry) &&
                now < entry.ExpiresAt)
            {
                return entry.Result;
            }

            GitWorkspaceContext? result = _inner.Resolve(workingDirectory);
            _cache[workingDirectory] = new CacheEntry(
                result,
                now.Add(_timeToLive));
            return result;
        }
    }

    private sealed record CacheEntry(
        GitWorkspaceContext? Result,
        DateTimeOffset ExpiresAt);
}
