using System.Globalization;
using CopilotLedger.Core.Pricing;
using CopilotLedger.Storage;
using CopilotLedger.Tests.TestSupport;

namespace CopilotLedger.Tests.Storage;

public sealed class ReceiptQueryTests
{
    [Fact]
    public async Task RQ1_ChannelModelRollupGroupsByChannelAndModel()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await SeededStoreAsync(temp.Path);

        IReadOnlyList<LedgerRollupRow> rows = await store.GetRollupAsync(
            LedgerGrouping.ChannelModel,
            LedgerFilter.All);

        Assert.Equal(6, rows.Count);
        AssertRow(rows[0], BillingChannel.GitHubAiCredits, "claude-opus-5.5", 2, 0, 1_210_000, 51_000, 800_000, 100_000, 292m, 2.92m, 0m, 0m);
        AssertRow(rows[1], BillingChannel.GitHubAiCredits, "mai-code-2", 2, 0, 1_010_000, 101_000, 400_000, 0, 56.75m, 0.5675m, 0m, 0m);
        AssertRow(rows[2], BillingChannel.FoundryByok, "gpt-6.1-sol", 1, 0, 100_000, 10_000, 0, 0, 0m, 0m, 0.315m, 0m);
        AssertRow(rows[3], BillingChannel.Local, "qwen3.8-27b", 1, 0, 400_000, 12_000, 0, 0, 0m, 0m, 0m, 0.23m);
        AssertRow(rows[4], BillingChannel.GitHubAiCredits, "gpt-unknown", 1, 1, 1_000, 100, 0, 0, 0m, 0m, 0m, 0m);
        AssertRow(rows[5], BillingChannel.Unpriced, "mystery-1", 1, 1, 2_000, 200, 0, 0, 0m, 0m, 0m, 0m);
        Assert.All(rows, row =>
        {
            Assert.Null(row.Repository);
            Assert.Null(row.Branch);
            Assert.Null(row.Agent);
        });
    }

    [Fact]
    public async Task RQ2_ChannelIsSetOnlyForChannelModelRows()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await SeededStoreAsync(temp.Path);

        foreach (LedgerGrouping grouping in new[] { LedgerGrouping.Repository, LedgerGrouping.Branch, LedgerGrouping.Model, LedgerGrouping.Agent })
        {
            IReadOnlyList<LedgerRollupRow> rows = await store.GetRollupAsync(grouping, LedgerFilter.All);
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.Null(row.Channel));
        }
    }

    [Fact]
    public async Task RQ3_PromptCountsDistinctTracesThatMatchTheFilter()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await SeededStoreAsync(temp.Path);

        Assert.Equal(5L, await store.GetPromptCountAsync(LedgerFilter.All));
        Assert.Equal(3L, await store.GetPromptCountAsync(new LedgerFilter { Repository = "swigerb/copilot-ledger", Branch = "feature/ledger" }));
        Assert.Equal(1L, await store.GetPromptCountAsync(new LedgerFilter { Repository = "contoso/api" }));
        Assert.Equal(2L, await store.GetPromptCountAsync(new LedgerFilter { From = DateTimeOffset.Parse("2026-11-21T13:10:00Z", CultureInfo.InvariantCulture), To = DateTimeOffset.Parse("2026-11-21T13:30:00Z", CultureInfo.InvariantCulture) }));
        Assert.Equal(0L, await store.GetPromptCountAsync(new LedgerFilter { Branch = "nope" }));
    }

    [Fact]
    public async Task RQ4_CountBreakerTripsCountsOpenAndResetTripsForOneBranch()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await SeededStoreAsync(temp.Path);

        Assert.Equal(2L, await store.CountBreakerTripsAsync("swigerb/copilot-ledger", "feature/ledger"));
        Assert.Equal(1L, await store.CountBreakerTripsAsync("swigerb/copilot-ledger", "main"));
        Assert.Equal(0L, await store.CountBreakerTripsAsync("contoso/api", "feature/ledger"));
        Assert.Equal(0L, await store.CountBreakerTripsAsync("swigerb/copilot-ledger", "Feature/Ledger"));
    }

    private static async Task<SqliteLedgerStore> SeededStoreAsync(string root)
    {
        SqliteLedgerStore store = new(Path.Combine(root, "ledger.db"));
        await store.InitializeAsync();
        Assert.Equal(8, await ReceiptSeeds.SeedAsync(store, root));
        return store;
    }

    private static void AssertRow(
        LedgerRollupRow row,
        BillingChannel channel,
        string model,
        long spans,
        long unpriced,
        long input,
        long output,
        long cacheRead,
        long cacheWrite,
        decimal credits,
        decimal githubUsd,
        decimal byokUsd,
        decimal savedUsd)
    {
        Assert.Equal(channel, row.Channel);
        Assert.Equal(model, row.Model);
        Assert.Equal(spans, row.SpanCount);
        Assert.Equal(unpriced, row.UnpricedSpanCount);
        Assert.Equal(input, row.InputTokens);
        Assert.Equal(output, row.OutputTokens);
        Assert.Equal(cacheRead, row.CacheReadInputTokens);
        Assert.Equal(cacheWrite, row.CacheWriteInputTokens);
        Assert.Equal(credits, row.AiCredits);
        Assert.Equal(githubUsd, row.AiCreditsUsd);
        Assert.Equal(byokUsd, row.FoundryByokUsd);
        Assert.Equal(savedUsd, row.LocalSavedUsd);
    }
}
