using CopilotLedger.Core.Breaker;
using CopilotLedger.Core.Git;
using CopilotLedger.Core.Pricing;
using CopilotLedger.Core.Telemetry;
using CopilotLedger.Storage;
using CopilotLedger.Tests.TestSupport;
using Microsoft.Data.Sqlite;

namespace CopilotLedger.Tests.Storage;

public sealed class BreakerStoreTests
{
    private const string TA = "b5000000000000000000000000000001";
    private const string TB = "b5000000000000000000000000000002";
    private const string TC = "b5000000000000000000000000000003";
    private const string Hash =
        "c3d1fe831856c77818bb8c1242d32d6efa6c37366eb902e56c5fd0054c1dcbc3/918a44e755cc5eee09b08d214831e8339a645158ef1f831a0fa02bd43999700f";
    private static readonly DateTimeOffset T0 =
        new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NOW =
        new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonRateCardCalculator Calculator =
        JsonRateCardCalculator.LoadEmbedded();

    [Fact]
    public async Task BS1_GetTraceActivityAsync_ReturnsAllBreakerInputs()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await StoreAsync(temp);
        GitWorkspaceContext feature = GitFixture.Feature(temp.Path);
        NormalizedGenAiSpan span1 = TestSpans.Chat(
            startTime: T0,
            endTime: T0.AddSeconds(2),
            requestModel: "mai-code-2",
            conversationId: "conv-b",
            inputTokens: 1_000,
            outputTokens: 100) with
        {
            TraceId = TA,
            SpanId = "b500000000000001",
        };
        NormalizedGenAiSpan span2 = TestSpans.Chat(
            startTime: T0.AddSeconds(60),
            endTime: T0.AddSeconds(62),
            requestModel: "claude-opus-5.5",
            conversationId: "conv-b",
            inputTokens: 100_000,
            outputTokens: 10_000) with
        {
            TraceId = TA,
            SpanId = "b500000000000002",
        };
        NormalizedGenAiSpan span3 = TestSpans.Chat(
            startTime: T0.AddSeconds(30),
            endTime: T0.AddSeconds(31),
            requestModel: "qwen3.8-27b-cuda-gpu:1",
            serverAddress: "127.0.0.1",
            serverPort: 11434,
            inputTokens: 1_000) with
        {
            TraceId = TA,
            SpanId = "b500000000000003",
        };
        await store.RecordAsync(
        [
            new LedgerEntry(span1, Calculator.Calculate(span1), feature),
            new LedgerEntry(
                span2,
                Calculator.Calculate(span2),
                feature with { HeadCommitSha = GitFixture.Sha2 }),
            new LedgerEntry(span3, Calculator.Calculate(span3), null),
        ]);
        await store.RecordSpanLinksAsync(
        [
            ToolLink(TA, "b5000000000000a1", T0.AddSeconds(20), "powershell", 1, 0),
            ToolLink(TA, "b5000000000000a2", T0.AddSeconds(40), "powershell", 0, 0),
            ToolLink(TA, "b5000000000000a3", T0.AddSeconds(40), Hash, null, 2),
            ToolLink(TA, "b500000000000001", T0, null, null, 0, operation: "chat"),
            ToolLink(
                TB,
                "b5000000000000b1",
                T0,
                "powershell",
                1,
                0,
                conversation: "conv-links"),
        ]);

        TraceActivity activity = await store.GetTraceActivityAsync(TA);
        Assert.Equal(TA, activity.TraceId);
        Assert.Equal("conv-b", activity.ConversationId);
        Assert.Equal("swigerb/copilot-ledger", activity.Repository);
        Assert.Equal("feature/ledger", activity.Branch);
        Assert.Equal(Path.Combine(temp.Path, "repo"), activity.RepositoryRoot);
        Assert.Equal(3, activity.SpanCount);
        Assert.Equal(
            [
                new CostEvent("b500000000000001", T0.AddSeconds(2), 0.00075m),
                new CostEvent("b500000000000002", T0.AddSeconds(62), 0.60m),
            ],
            activity.Costs);
        Assert.Equal(
            [
                new ToolCallEvent("b5000000000000a1", T0.AddSeconds(20), "powershell", 1, 0),
                new ToolCallEvent("b5000000000000a2", T0.AddSeconds(40), "powershell", 0, 0),
                new ToolCallEvent("b5000000000000a3", T0.AddSeconds(40), Hash, null, 2),
            ],
            activity.ToolCalls);
        Assert.Equal(
            [
                new CommitObservation("b500000000000001", T0, GitFixture.Sha1),
                new CommitObservation("b500000000000002", T0.AddSeconds(60), GitFixture.Sha2),
            ],
            activity.Commits);

        TraceActivity linksOnly = await store.GetTraceActivityAsync(TB);
        Assert.Equal("conv-links", linksOnly.ConversationId);
        Assert.Equal("(none)", linksOnly.Repository);
        Assert.Equal("(none)", linksOnly.Branch);
        Assert.Null(linksOnly.RepositoryRoot);
        Assert.Equal(0, linksOnly.SpanCount);
        Assert.Empty(linksOnly.Costs);
        Assert.Empty(linksOnly.Commits);
        Assert.Equal(
            [new ToolCallEvent("b5000000000000b1", T0, "powershell", 1, 0)],
            linksOnly.ToolCalls);

        TraceActivity missing = await store.GetTraceActivityAsync(TC);
        Assert.Null(missing.ConversationId);
        Assert.Equal("(none)", missing.Repository);
        Assert.Equal("(none)", missing.Branch);
        Assert.Null(missing.RepositoryRoot);
        Assert.Equal(0, missing.SpanCount);
        Assert.Empty(missing.Costs);
        Assert.Empty(missing.ToolCalls);
        Assert.Empty(missing.Commits);
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.GetTraceActivityAsync(" "));
    }

    [Fact]
    public async Task BS2_RecordBreakerTripAsync_InsertsOnceAndReadsFilters()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await StoreAsync(temp);
        NewBreakerTrip a = TripA(temp.Path);
        NewBreakerTrip b = TripB(temp.Path);
        NewBreakerTrip c = TripC();

        Assert.Equal(1, await store.RecordBreakerTripAsync(a));
        Assert.Null(await store.RecordBreakerTripAsync(a with { Detail = "x" }));
        Assert.Equal(2, await store.RecordBreakerTripAsync(b));
        Assert.Equal(3, await store.RecordBreakerTripAsync(c));

        IReadOnlyList<BreakerTrip> all = await store.GetOpenBreakerTripsAsync(
            BreakerTripFilter.All);
        Assert.Equal([1L, 2L, 3L], all.Select(trip => trip.Id));
        BreakerTrip trip1 = all[0];
        Assert.Equal(BreakerRule.CreditVelocity, trip1.Rule);
        Assert.Equal(TA, trip1.TraceId);
        Assert.Null(trip1.ConversationId);
        Assert.Equal("swigerb/copilot-ledger", trip1.Repository);
        Assert.Equal("feature/ledger", trip1.Branch);
        Assert.Equal(Path.Combine(temp.Path, "repo"), trip1.RepositoryRoot);
        Assert.Equal(T0.AddSeconds(180), trip1.TrippedAt);
        Assert.Equal("180.00 AI credits in 5 minutes", trip1.Detail);
        Assert.Equal(1.80m, trip1.WindowCostUsd);
        Assert.Equal(5, trip1.WindowMinutes);
        Assert.Null(trip1.FailureCount);
        Assert.Equal(NOW, trip1.RecordedAt);
        Assert.Null(trip1.NotifiedAt);
        Assert.Null(trip1.ResetAt);
        BreakerTrip trip2 = all[1];
        Assert.Equal("conv-b", trip2.ConversationId);
        Assert.Null(trip2.WindowCostUsd);
        Assert.Null(trip2.WindowMinutes);
        Assert.Equal(5, trip2.FailureCount);
        BreakerTrip trip3 = all[2];
        Assert.Null(trip3.RepositoryRoot);
        Assert.Equal(7, trip3.FailureCount);

        Assert.Equal(
            [3L],
            (await store.GetOpenBreakerTripsAsync(
                new BreakerTripFilter { Repository = "contoso/api" }))
                .Select(trip => trip.Id));
        Assert.Equal(
            [1L, 2L],
            (await store.GetOpenBreakerTripsAsync(
                new BreakerTripFilter { Branch = "feature/ledger" }))
                .Select(trip => trip.Id));
        Assert.Equal(
            [1L, 2L],
            (await store.GetOpenBreakerTripsAsync(
                new BreakerTripFilter { RepositoryRoot = Path.Combine(temp.Path, "repo") }))
                .Select(trip => trip.Id));
        Assert.Equal(
            [2L],
            (await store.GetOpenBreakerTripsAsync(new BreakerTripFilter { Id = 2 }))
                .Select(trip => trip.Id));
        Assert.Empty(await store.GetOpenBreakerTripsAsync(new BreakerTripFilter
        {
            Repository = "swigerb/copilot-ledger",
            Branch = "main",
        }));

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.RecordBreakerTripAsync(
                a with { TraceId = TC, Detail = " " }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.RecordBreakerTripAsync(
                a with { TraceId = TC, WindowCostUsd = -1m }));
    }

    [Fact]
    public async Task BS3_NotifyAndResetTrips_UseTheClockAndOpenState()
    {
        using TempDirectory temp = new();
        ManualTimeProvider clock = new(NOW);
        SqliteLedgerStore store = await StoreAsync(temp, clock);
        await store.RecordBreakerTripAsync(TripA(temp.Path));
        await store.RecordBreakerTripAsync(TripB(temp.Path));
        await store.RecordBreakerTripAsync(TripC());

        Assert.Equal(
            [1L, 2L, 3L],
            (await store.GetUnnotifiedBreakerTripsAsync(NOW.AddHours(-1)))
                .Select(trip => trip.Id));
        Assert.True(await store.MarkBreakerTripNotifiedAsync(1));
        Assert.False(await store.MarkBreakerTripNotifiedAsync(1));
        Assert.False(await store.MarkBreakerTripNotifiedAsync(99));
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(
            [2L, 3L],
            (await store.GetUnnotifiedBreakerTripsAsync(NOW.AddHours(-1)))
                .Select(trip => trip.Id));
        Assert.Empty(await store.GetUnnotifiedBreakerTripsAsync(NOW.AddMilliseconds(1)));

        IReadOnlyList<BreakerTrip> reset = await store.ResetBreakerTripsAsync(
            new BreakerTripFilter
            {
                Repository = "swigerb/copilot-ledger",
                Branch = "feature/ledger",
            });
        Assert.Equal([1L, 2L], reset.Select(trip => trip.Id));
        Assert.All(
            reset,
            trip => Assert.Equal(NOW.AddMinutes(30), trip.ResetAt));
        Assert.Empty(await store.ResetBreakerTripsAsync(new BreakerTripFilter
        {
            Repository = "swigerb/copilot-ledger",
            Branch = "feature/ledger",
        }));
        Assert.Equal(
            [3L],
            (await store.GetOpenBreakerTripsAsync(BreakerTripFilter.All))
                .Select(trip => trip.Id));
        Assert.Equal(
            [3L],
            (await store.GetUnnotifiedBreakerTripsAsync(NOW.AddHours(-1)))
                .Select(trip => trip.Id));

        Assert.Equal(
            [3L],
            (await store.ResetBreakerTripsAsync(new BreakerTripFilter { Id = 3 }))
                .Select(trip => trip.Id));
        Assert.Empty(await store.GetOpenBreakerTripsAsync(BreakerTripFilter.All));
        Assert.Empty(await store.ResetBreakerTripsAsync(BreakerTripFilter.All));
    }

    [Fact]
    public async Task BS4_BreakerSettings_StoreValidatedValues()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await StoreAsync(temp);
        Assert.Equal(BreakerSettings.Default, await store.GetBreakerSettingsAsync());
        BreakerSettings settings = new()
        {
            VelocityCredits = 12.34m,
            VelocityWindowMinutes = 1440,
            ChurnFailures = 2,
            FlagFile = true,
            Notifications = false,
        };

        await store.UpdateBreakerSettingsAsync(settings);

        Assert.Equal(settings, await store.GetBreakerSettingsAsync());
        await using (SqliteConnection connection = await OpenAsync(DatabasePath(temp)))
        {
            Assert.Equal(
                123400000000L,
                await ScalarAsync(
                    connection,
                    "SELECT velocity_threshold_usd_pico FROM breaker_settings WHERE id = 1;"));
            Assert.Equal(
                NOW.ToUnixTimeMilliseconds(),
                await ScalarAsync(
                    connection,
                    "SELECT updated_at_unix_ms FROM breaker_settings WHERE id = 1;"));
        }

        BreakerSettings upper = BreakerSettings.Default with
        {
            VelocityCredits = 1000000m,
        };
        await store.UpdateBreakerSettingsAsync(upper);
        Assert.Equal(upper, await store.GetBreakerSettingsAsync());
        await using SqliteConnection check = await OpenAsync(DatabasePath(temp));
        Assert.Equal(
            10000000000000000L,
            await ScalarAsync(
                check,
                "SELECT velocity_threshold_usd_pico FROM breaker_settings WHERE id = 1;"));
    }

    [Fact]
    public async Task BS5_BranchBudgets_TrackSpendAlertsAndBoundaries()
    {
        using TempDirectory temp = new();
        ManualTimeProvider clock = new(NOW);
        SqliteLedgerStore store = await StoreAsync(temp, clock);
        await LedgerSeeds.SeedChainsAsync(
            store,
            Path.Combine(temp.Path, "repo"),
            Path.Combine(temp.Path, "api"));
        await store.SetBranchBudgetAsync("swigerb/copilot-ledger", "feature/ledger", 50m);
        await store.SetBranchBudgetAsync("contoso/api", "main", 40.5m);
        await store.SetBranchBudgetAsync("(none)", "(none)", 1m);

        IReadOnlyList<BranchBudgetRow> budgets = await store.GetBranchBudgetsAsync();
        Assert.Collection(
            budgets,
            row => AssertBudget(row, "(none)", "(none)", 1m, 0m, false, NOW, null, null),
            row => AssertBudget(row, "contoso/api", "main", 40.5m, 31.5m, false, NOW, null, null),
            row => AssertBudget(
                row,
                "swigerb/copilot-ledger",
                "feature/ledger",
                50m,
                56m,
                true,
                NOW,
                null,
                null));
        Assert.Equal(
            "contoso/api",
            Assert.Single(await store.GetBranchBudgetsAsync("contoso/api")).Repository);
        Assert.Empty(await store.GetBranchBudgetsAsync(
            "swigerb/copilot-ledger",
            "main"));

        Assert.True(await store.MarkBranchBudgetAlertedAsync(
            "swigerb/copilot-ledger",
            "feature/ledger"));
        Assert.False(await store.MarkBranchBudgetAlertedAsync(
            "swigerb/copilot-ledger",
            "feature/ledger"));
        Assert.False(await store.MarkBranchBudgetAlertedAsync("x", "y"));
        BranchBudgetRow alerted = Assert.Single(
            await store.GetUnnotifiedBudgetAlertsAsync(NOW.AddHours(-1)));
        Assert.Equal(NOW, alerted.AlertedAt);
        Assert.True(await store.MarkBranchBudgetNotifiedAsync(
            "swigerb/copilot-ledger",
            "feature/ledger"));
        Assert.False(await store.MarkBranchBudgetNotifiedAsync(
            "swigerb/copilot-ledger",
            "feature/ledger"));
        Assert.False(await store.MarkBranchBudgetNotifiedAsync("contoso/api", "main"));
        Assert.Empty(await store.GetUnnotifiedBudgetAlertsAsync(NOW.AddHours(-1)));

        clock.Advance(TimeSpan.FromHours(1));
        await store.SetBranchBudgetAsync(
            "swigerb/copilot-ledger",
            "feature/ledger",
            60m);
        BranchBudgetRow reset = Assert.Single(await store.GetBranchBudgetsAsync(
            "swigerb/copilot-ledger",
            "feature/ledger"));
        AssertBudget(
            reset,
            "swigerb/copilot-ledger",
            "feature/ledger",
            60m,
            56m,
            false,
            NOW.AddHours(1),
            null,
            null);

        await store.SetBranchBudgetAsync(
            "swigerb/copilot-ledger",
            "feature/ledger",
            56m);
        BranchBudgetRow boundary = Assert.Single(await store.GetBranchBudgetsAsync(
            "swigerb/copilot-ledger",
            "feature/ledger"));
        Assert.Equal(56m, boundary.BudgetAiCredits);
        Assert.Equal(56m, boundary.SpentAiCredits);
        Assert.False(boundary.Exceeded);
        Assert.False(await store.MarkBranchBudgetAlertedAsync(
            "swigerb/copilot-ledger",
            "feature/ledger"));
        Assert.Null((await store.GetBranchBudgetsAsync(
            "swigerb/copilot-ledger",
            "feature/ledger")).Single().AlertedAt);

        await store.SetBranchBudgetAsync(
            "swigerb/copilot-ledger",
            "feature/ledger",
            55.99m);
        BranchBudgetRow exceeded = Assert.Single(await store.GetBranchBudgetsAsync(
            "swigerb/copilot-ledger",
            "feature/ledger"));
        Assert.True(exceeded.Exceeded);
        Assert.True(await store.MarkBranchBudgetAlertedAsync(
            "swigerb/copilot-ledger",
            "feature/ledger"));
        Assert.Equal(
            NOW.AddHours(1),
            Assert.Single(await store.GetBranchBudgetsAsync(
                "swigerb/copilot-ledger",
                "feature/ledger")).AlertedAt);

        Assert.True(await store.ClearBranchBudgetAsync("contoso/api", "main"));
        Assert.False(await store.ClearBranchBudgetAsync("contoso/api", "main"));
        Assert.Equal(2, (await store.GetBranchBudgetsAsync()).Count);
        foreach (decimal value in new[] { 0m, -1m, 1000000.01m, 0.001m })
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => store.SetBranchBudgetAsync("a", "b", value));
        }

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SetBranchBudgetAsync(" ", "b", 1m));
        Assert.Equal(2, (await store.GetBranchBudgetsAsync()).Count);
    }

    [Fact]
    public async Task BS6_GetFailedToolCallCountsAsync_CountsDistinctFailedLinks()
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await StoreAsync(temp);
        const string x = "77777777777777777777777777777777";
        const string y = "88888888888888888888888888888888";
        const string z = "99999999999999999999999999999999";
        await store.RecordSpanLinksAsync(
        [
            ToolLink(x, "7777777777777771", T0, "powershell", 1, 0),
            ToolLink(x, "7777777777777772", T0.AddSeconds(1), "powershell", -1, 0),
            ToolLink(x, "7777777777777773", T0.AddSeconds(2), Hash, null, 2),
            ToolLink(x, "7777777777777774", T0.AddSeconds(3), "powershell", 0, 0),
            ToolLink(x, "7777777777777775", T0.AddSeconds(4), "powershell", 0, 2),
            ToolLink(x, "7777777777777776", T0.AddSeconds(5), "apply_patch", null, 0),
            ToolLink(y, "8888888888888881", T0, "bash", 2, 0),
            ToolLink(z, "9999999999999991", T0, "powershell", 0, 0),
        ]);

        IReadOnlyDictionary<string, long> counts =
            await store.GetFailedToolCallCountsAsync(
                [x, y, z, new string('6', 32)]);

        Assert.Equal(2, counts.Count);
        Assert.Equal(4, counts[x]);
        Assert.Equal(1, counts[y]);
        Assert.Empty(await store.GetFailedToolCallCountsAsync([]));
    }

    public static TheoryData<decimal, int, int> InvalidSettings => new()
    {
        { 0.99m, 5, 5 },
        { 1000000.01m, 5, 5 },
        { 12.345m, 5, 5 },
        { 150m, 0, 5 },
        { 150m, 1441, 5 },
        { 150m, 5, 1 },
        { 150m, 5, 101 },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public async Task BS7_InvalidSettings_AreRejectedWithoutWriting(
        decimal credits,
        int minutes,
        int failures)
    {
        using TempDirectory temp = new();
        SqliteLedgerStore store = await StoreAsync(temp);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.UpdateBreakerSettingsAsync(BreakerSettings.Default with
            {
                VelocityCredits = credits,
                VelocityWindowMinutes = minutes,
                ChurnFailures = failures,
            }));
        Assert.Equal(BreakerSettings.Default, await store.GetBreakerSettingsAsync());
    }

    private static NewBreakerTrip TripA(string root) => new()
    {
        Rule = BreakerRule.CreditVelocity,
        TraceId = TA,
        Repository = "swigerb/copilot-ledger",
        Branch = "feature/ledger",
        RepositoryRoot = Path.Combine(root, "repo"),
        TrippedAt = T0.AddSeconds(180),
        Detail = "180.00 AI credits in 5 minutes",
        WindowCostUsd = 1.80m,
        WindowMinutes = 5,
    };

    private static NewBreakerTrip TripB(string root) => new()
    {
        Rule = BreakerRule.RetryChurn,
        TraceId = TA,
        ConversationId = "conv-b",
        Repository = "swigerb/copilot-ledger",
        Branch = "feature/ledger",
        RepositoryRoot = Path.Combine(root, "repo"),
        TrippedAt = T0.AddSeconds(120),
        Detail = "5 failed tool calls in a row",
        FailureCount = 5,
    };

    private static NewBreakerTrip TripC() => new()
    {
        Rule = BreakerRule.RetryChurn,
        TraceId = TB,
        Repository = "contoso/api",
        Branch = "main",
        TrippedAt = T0.AddHours(-1),
        Detail = "7 failed tool calls in a row",
        FailureCount = 7,
    };

    private static GenAiSpanLink ToolLink(
        string trace,
        string span,
        DateTimeOffset start,
        string? tool,
        long? exit,
        int status,
        string operation = "execute_tool",
        string? conversation = null) => new()
    {
        TraceId = trace,
        SpanId = span,
        OperationName = operation,
        ConversationId = conversation,
        StartTime = start,
        ToolName = tool,
        ToolExitCode = exit,
        StatusCode = status,
    };

    private static void AssertBudget(
        BranchBudgetRow row,
        string repository,
        string branch,
        decimal budget,
        decimal spent,
        bool exceeded,
        DateTimeOffset setAt,
        DateTimeOffset? alertedAt,
        DateTimeOffset? notifiedAt)
    {
        Assert.Equal(repository, row.Repository);
        Assert.Equal(branch, row.Branch);
        Assert.Equal(budget, row.BudgetAiCredits);
        Assert.Equal(spent, row.SpentAiCredits);
        Assert.Equal(exceeded, row.Exceeded);
        Assert.Equal(setAt, row.SetAt);
        Assert.Equal(alertedAt, row.AlertedAt);
        Assert.Equal(notifiedAt, row.NotifiedAt);
    }

    private static async Task<SqliteLedgerStore> StoreAsync(
        TempDirectory temp,
        ManualTimeProvider? clock = null)
    {
        SqliteLedgerStore store = new(DatabasePath(temp), clock ?? new ManualTimeProvider(NOW));
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

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
