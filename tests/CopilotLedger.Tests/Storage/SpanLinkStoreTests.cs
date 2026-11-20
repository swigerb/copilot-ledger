using CopilotLedger.Core.Git;
using CopilotLedger.Core.Pricing;
using CopilotLedger.Core.Telemetry;
using CopilotLedger.Storage;
using CopilotLedger.Tests.TestSupport;
using Microsoft.Data.Sqlite;

namespace CopilotLedger.Tests.Storage;

public sealed class SpanLinkStoreTests
{
    private const string Sha1 = "0123456789abcdef0123456789abcdef01234567";
    private static readonly JsonRateCardCalculator Calculator =
        JsonRateCardCalculator.LoadEmbedded();

    [Fact]
    public async Task SL1_Migration2_UpgradesVersion1AndKeepsRows()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore first = await StoreAsync(temp);
        await first.RecordAsync([Entry("mai-code-2", 1000, 100)]);
        await using (SqliteConnection connection = await OpenAsync(DatabasePath(temp)))
        {
            await ExecuteAsync(connection, """
                DROP INDEX ix_spans_trace;
                DROP TABLE span_links;
                PRAGMA user_version = 1;
                """);
        }

        await new SqliteLedgerStore(DatabasePath(temp)).InitializeAsync();
        await using SqliteConnection check = await OpenAsync(DatabasePath(temp));
        Assert.Equal(2L, await ScalarAsync(check, "PRAGMA user_version;"));
        Assert.Equal(
            "span_links",
            await ScalarAsync(
                check,
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'span_links';"));
        Assert.Equal(1L, await ScalarAsync(check, "SELECT COUNT(*) FROM spans;"));
    }

    [Fact]
    public async Task SL2_RecordSpanLinksAsync_InsertsOnce()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await StoreAsync(temp);
        GenAiSpanLink[] links =
        [
            Link("1", "1111111111111111", null),
            Link("2", "2222222222222222", "1111111111111111"),
            Link("3", "3333333333333333", "2222222222222222"),
        ];

        Assert.Equal(3, await store.RecordSpanLinksAsync(links));
        Assert.Equal(0, await store.RecordSpanLinksAsync(links));
    }

    [Fact]
    public async Task SL3_LateAgentLink_BackfillsOnce()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await StoreAsync(temp);
        const string trace = "0af7651916cd43dd8448eb211c80319c";
        NormalizedGenAiSpan span = TestSpans.Chat(
            requestModel: "claude-sonnet-5.5",
            inputTokens: 10_000,
            outputTokens: 2_000) with
        {
            TraceId = trace,
            SpanId = "b7ad6b7169203332",
            ParentSpanId = "b7ad6b7169203331",
        };
        await store.RecordAsync([new LedgerEntry(span, Calculator.Calculate(span), null)]);
        await store.RecordSpanLinksAsync([Link(
            trace,
            span.SpanId,
            span.ParentSpanId,
            "chat")]);
        Assert.Equal(0, await store.BackfillFromAgentSpansAsync([trace]));
        await store.RecordSpanLinksAsync([Link(
            trace,
            "b7ad6b7169203331",
            null,
            "invoke_agent",
            "ledger-builder",
            "conv-c")]);
        Assert.Equal(1, await store.BackfillFromAgentSpansAsync([trace]));
        Assert.Equal(0, await store.BackfillFromAgentSpansAsync([trace]));

        LedgerRollupRow row = Assert.Single(await store.GetRollupAsync(
            LedgerGrouping.Agent,
            LedgerFilter.All));
        Assert.Equal("ledger-builder", row.Agent);
        Assert.Equal(1, row.SpanCount);
        Assert.Equal(4m, row.AiCredits);
        Assert.Equal(0.04m, row.AiCreditsUsd);
    }

    [Fact]
    public async Task SL4_BackfillAgentAndGit_PreservesFilesystemContext()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await StoreAsync(temp);
        const string trace = "a3ce929d0e0e47364bf92f3577b34da6";
        NormalizedGenAiSpan chat1 = TestSpans.Chat(
            requestModel: "claude-opus-5.5",
            inputTokens: 100_000,
            outputTokens: 10_000) with
        {
            TraceId = trace,
            SpanId = "c0ffee0000000002",
            ParentSpanId = "c0ffee0000000001",
        };
        NormalizedGenAiSpan chat2 = chat1 with { SpanId = "c0ffee0000000003" };
        GitWorkspaceContext git = new(
            Path.Combine(temp.Path, "repo"),
            Path.Combine(temp.Path, "repo", ".git"),
            "main",
            Sha1,
            "https://github.com/swigerb/copilot-ledger.git",
            "swigerb/copilot-ledger");
        await store.RecordAsync(
        [
            new LedgerEntry(chat1, Calculator.Calculate(chat1), null),
            new LedgerEntry(chat2, Calculator.Calculate(chat2), git),
        ]);
        await store.RecordSpanLinksAsync(
        [
            Link(trace, chat1.SpanId, chat1.ParentSpanId, "chat"),
            Link(trace, chat2.SpanId, chat2.ParentSpanId, "chat"),
            Link(
                trace,
                "c0ffee0000000001",
                null,
                "invoke_agent",
                "GitHub Copilot Chat",
                "vscode-session-1",
                "contoso/webapp",
                "feature/login",
                Sha1),
        ]);

        Assert.Equal(3, await store.BackfillFromAgentSpansAsync([trace]));
        IReadOnlyList<LedgerRollupRow> branches = await store.GetRollupAsync(
            LedgerGrouping.Branch,
            LedgerFilter.All);
        Assert.Collection(
            branches,
            row =>
            {
                Assert.Equal("contoso/webapp", row.Repository);
                Assert.Equal("feature/login", row.Branch);
                Assert.Equal(60m, row.AiCredits);
            },
            row =>
            {
                Assert.Equal("swigerb/copilot-ledger", row.Repository);
                Assert.Equal("main", row.Branch);
                Assert.Equal(60m, row.AiCredits);
            });
        LedgerRollupRow agent = Assert.Single(await store.GetRollupAsync(
            LedgerGrouping.Agent,
            LedgerFilter.All));
        Assert.Equal("GitHub Copilot Chat", agent.Agent);
        Assert.Equal(2, agent.SpanCount);
        Assert.Equal(120m, agent.AiCredits);
        await using SqliteConnection connection = await OpenAsync(DatabasePath(temp));
        Assert.Equal(Sha1, await ScalarAsync(
            connection,
            $"SELECT head_commit_sha FROM spans WHERE span_id = '{chat1.SpanId}';"));
        Assert.True(Convert.IsDBNull(await ScalarAsync(
            connection,
            $"SELECT repo_root FROM spans WHERE span_id = '{chat1.SpanId}';")));
    }

    [Fact]
    public async Task SL5_RecursiveBackfill_StopsOnCycle()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await StoreAsync(temp);
        const string trace = "ffffffffffffffffffffffffffffffff";
        NormalizedGenAiSpan span = TestSpans.Chat(
            requestModel: "mai-code-2",
            inputTokens: 1_000) with
        {
            TraceId = trace,
            SpanId = "1111111111111111",
            ParentSpanId = "2222222222222222",
        };
        await store.RecordAsync([new LedgerEntry(span, Calculator.Calculate(span), null)]);
        await store.RecordSpanLinksAsync(
        [
            Link(trace, "1111111111111111", "2222222222222222", "chat"),
            Link(trace, "2222222222222222", "1111111111111111", "invoke_agent", "loop"),
        ]);

        Assert.Equal(1, await store.BackfillFromAgentSpansAsync([trace]));
    }

    [Fact]
    public async Task SL6_PruneSpanLinks_UsesStrictStartBoundary()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await StoreAsync(temp);
        await store.RecordSpanLinksAsync(
        [
            LinkAt("1", new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)),
            LinkAt("2", new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero)),
            LinkAt("3", new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero)),
        ]);

        Assert.Equal(1, await store.PruneSpanLinksAsync(
            new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(0, await store.PruneSpanLinksAsync(
            new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(2, await store.PruneSpanLinksAsync(
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task SL7_PromptChains_ReturnExactOrderAndFilters()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await StoreAsync(temp);
        Assert.Equal(
            5,
            await LedgerSeeds.SeedChainsAsync(
                store,
                Path.Combine(temp.Path, "repo"),
                Path.Combine(temp.Path, "api")));

        IReadOnlyList<PromptChainRow> all =
            await store.GetTopPromptChainsAsync(LedgerFilter.All, 10);
        Assert.Collection(
            all,
            row => AssertChain(
                row,
                new string('1', 32),
                "2026-11-21T14:00:00Z",
                "2026-11-21T14:00:02Z",
                "swigerb/copilot-ledger",
                "main",
                ["ledger-architect"],
                ["claude-opus-5.5"],
                1,
                0,
                1_200_000,
                50_000,
                800_000,
                100_000,
                286m,
                2.86m,
                0m,
                0m,
                "conv-1"),
            row => AssertChain(
                row,
                new string('2', 32),
                "2026-11-21T13:00:00Z",
                "2026-11-21T13:05:02Z",
                "swigerb/copilot-ledger",
                "feature/ledger",
                ["ledger-builder"],
                ["mai-code-2", "qwen3.8-27b"],
                2,
                0,
                1_400_000,
                112_000,
                400_000,
                0,
                56m,
                0.56m,
                0m,
                0.23m,
                "conv-2"),
            row => AssertChain(
                row,
                new string('3', 32),
                "2026-11-18T12:00:00Z",
                "2026-11-18T12:00:02Z",
                "contoso/api",
                "main",
                [],
                ["gpt-6.1-sol"],
                1,
                0,
                100_000,
                10_000,
                0,
                0,
                0m,
                0m,
                0.315m,
                0m,
                null),
            row => AssertChain(
                row,
                new string('4', 32),
                "2026-10-01T12:00:00Z",
                "2026-10-01T12:00:02Z",
                "(none)",
                "(none)",
                ["ledger-builder"],
                ["gpt-unknown"],
                1,
                1,
                1_000,
                100,
                0,
                0,
                0m,
                0m,
                0m,
                0m,
                "conv-4"));

        Assert.Equal(
            [new string('1', 32), new string('2', 32)],
            (await store.GetTopPromptChainsAsync(LedgerFilter.All, 2))
                .Select(row => row.TraceId));
        Assert.Equal(
            [new string('3', 32)],
            (await store.GetTopPromptChainsAsync(
                new LedgerFilter { Repository = "contoso/api" },
                10)).Select(row => row.TraceId));
        Assert.Equal(
            [new string('1', 32), new string('2', 32)],
            (await store.GetTopPromptChainsAsync(
                new LedgerFilter
                {
                    From = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero),
                },
                10)).Select(row => row.TraceId));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.GetTopPromptChainsAsync(LedgerFilter.All, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.GetTopPromptChainsAsync(LedgerFilter.All, 1001));
    }

    private static void AssertChain(
        PromptChainRow row,
        string trace,
        string start,
        string end,
        string repository,
        string branch,
        IReadOnlyList<string> agents,
        IReadOnlyList<string> models,
        long spans,
        long unpriced,
        long input,
        long output,
        long cacheRead,
        long cacheWrite,
        decimal credits,
        decimal githubUsd,
        decimal byokUsd,
        decimal savedUsd,
        string? conversation)
    {
        Assert.Equal(trace, row.TraceId);
        Assert.Equal(Parse(start), row.StartTime);
        Assert.Equal(Parse(end), row.EndTime);
        Assert.Equal(repository, row.Repository);
        Assert.Equal(branch, row.Branch);
        Assert.Equal(agents, row.Agents);
        Assert.Equal(models, row.Models);
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
        Assert.Equal(conversation, row.ConversationId);
    }

    private static LedgerEntry Entry(string model, long input, long output)
    {
        NormalizedGenAiSpan span = TestSpans.Chat(
            requestModel: model,
            inputTokens: input,
            outputTokens: output) with
        {
            TraceId = "5b8efff798038103d269b633813fc60c",
            SpanId = "eee19b7ec3c1b174",
            ParentSpanId = "eee19b7ec3c1b173",
        };
        return new LedgerEntry(span, Calculator.Calculate(span), null);
    }

    private static GenAiSpanLink Link(
        string trace,
        string span,
        string? parent,
        string? operation = null,
        string? agent = null,
        string? conversation = null,
        string? slug = null,
        string? branch = null,
        string? sha = null) => new()
    {
        TraceId = trace.Length == 32 ? trace : new string(trace[^1], 32),
        SpanId = span,
        ParentSpanId = parent,
        OperationName = operation,
        AgentName = agent,
        ConversationId = conversation,
        GitRepositorySlug = slug,
        GitBranch = branch,
        GitCommitSha = sha,
        StartTime = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero),
    };

    private static GenAiSpanLink LinkAt(string id, DateTimeOffset start) => new()
    {
        TraceId = new string(id[^1], 32),
        SpanId = id[^1..].PadLeft(16, id[^1]),
        StartTime = start,
    };

    private static async Task<SqliteLedgerStore> StoreAsync(TempDirectory temp)
    {
        SqliteLedgerStore store = new(DatabasePath(temp));
        await store.InitializeAsync();
        return store;
    }

    private static string DatabasePath(TempDirectory temp) =>
        Path.Combine(temp.Path, "ledger.db");

    private static async Task<SqliteConnection> OpenAsync(string path)
    {
        SqliteConnection connection = new($"Data Source={path}");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
