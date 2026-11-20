using System.Text.Json.Serialization;

namespace CopilotLedger.Core.Pricing;

internal sealed class RateCardDocument
{
    [JsonPropertyName("schemaVersion")]
    public int? SchemaVersion { get; init; }

    [JsonPropertyName("ratesVersion")]
    public string? RatesVersion { get; init; }

    [JsonPropertyName("source")]
    public RateCardSource? Source { get; init; }

    [JsonPropertyName("notes")]
    public IReadOnlyList<string>? Notes { get; init; }

    [JsonPropertyName("githubAiCredits")]
    public PricedRateSection? GithubAiCredits { get; init; }

    [JsonPropertyName("foundryByok")]
    public FoundryRateSection? FoundryByok { get; init; }

    [JsonPropertyName("localOffload")]
    public LocalRateSection? LocalOffload { get; init; }
}

internal sealed class RateCardSource
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("retrievedOn")]
    public string? RetrievedOn { get; init; }
}

internal sealed class PricedRateSection
{
    [JsonPropertyName("hostSuffixes")]
    public IReadOnlyList<string>? HostSuffixes { get; init; }

    [JsonPropertyName("models")]
    public IReadOnlyList<PricedModelDocument>? Models { get; init; }
}

internal sealed class FoundryRateSection
{
    [JsonPropertyName("hostSuffixes")]
    public IReadOnlyList<string>? HostSuffixes { get; init; }

    [JsonPropertyName("providerNames")]
    public IReadOnlyList<string>? ProviderNames { get; init; }

    [JsonPropertyName("models")]
    public IReadOnlyList<PricedModelDocument>? Models { get; init; }
}

internal sealed class LocalRateSection
{
    [JsonPropertyName("baselineModel")]
    public string? BaselineModel { get; init; }

    [JsonPropertyName("models")]
    public IReadOnlyList<LocalModelDocument>? Models { get; init; }

    [JsonPropertyName("additionalHosts")]
    public IReadOnlyList<string>? AdditionalHosts { get; init; }

    [JsonPropertyName("providerNames")]
    public IReadOnlyList<string>? ProviderNames { get; init; }
}

internal sealed class PricedModelDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("aliases")]
    public IReadOnlyList<string> Aliases { get; init; } = [];

    [JsonPropertyName("windows")]
    public required IReadOnlyList<RateWindowDocument> Windows { get; init; }
}

internal sealed class RateWindowDocument
{
    [JsonPropertyName("effectiveFrom")]
    public DateTimeOffset? EffectiveFrom { get; init; }

    [JsonPropertyName("effectiveUntil")]
    public DateTimeOffset? EffectiveUntil { get; init; }

    [JsonPropertyName("tiers")]
    public required IReadOnlyList<RateTierDocument> Tiers { get; init; }
}

internal sealed class RateTierDocument
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("maxInputTokens")]
    public long? MaxInputTokens { get; init; }

    [JsonPropertyName("inputPer1M")]
    public required decimal InputPer1M { get; init; }

    [JsonPropertyName("cachedInputPer1M")]
    public decimal? CachedInputPer1M { get; init; }

    [JsonPropertyName("cacheWritePer1M")]
    public decimal? CacheWritePer1M { get; init; }

    [JsonPropertyName("outputPer1M")]
    public required decimal OutputPer1M { get; init; }
}

internal sealed class LocalModelDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("aliases")]
    public IReadOnlyList<string> Aliases { get; init; } = [];
}
