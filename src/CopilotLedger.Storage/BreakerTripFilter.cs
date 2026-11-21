namespace CopilotLedger.Storage;

public sealed record BreakerTripFilter
{
    public static BreakerTripFilter All { get; } = new();
    public long? Id { get; init; }
    public string? Repository { get; init; }
    public string? Branch { get; init; }
    public string? RepositoryRoot { get; init; }                         // ordinal match on repo_root
}
