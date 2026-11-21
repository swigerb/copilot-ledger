using System.Globalization;
using CopilotLedger.Core.Breaker;
using CopilotLedger.Core.Git;
using CopilotLedger.Core.Pricing;
using CopilotLedger.Core.Telemetry;
using CopilotLedger.Storage;

namespace CopilotLedger.Tests.TestSupport;

public static class ReceiptSeeds
{
    // Records eight spans and three breaker trips. Six spans in three traces are on swigerb/copilot-ledger feature/ledger
    // (GitFixture.Feature(root)); one is on its main branch, and one is on contoso/api feature/ledger. Two trips are on
    // feature/ledger (one of them reset) and one is on main. Returns the number of spans recorded (8).
    public static async Task<int> SeedAsync(SqliteLedgerStore store, string root)
    {
        JsonRateCardCalculator embedded = JsonRateCardCalculator.LoadEmbedded();
        JsonRateCardCalculator byok = JsonRateCardCalculator.Parse(RateFixtures.ByokTestRatesJson);
        GitWorkspaceContext feature = GitFixture.Feature(root);
        GitWorkspaceContext main = feature with { BranchName = "main" };
        GitWorkspaceContext api = new(
            Path.Combine(root, "api"),
            Path.Combine(root, "api", ".git"),
            "feature/ledger",
            GitFixture.Sha1,
            "https://github.com/contoso/api.git",
            "contoso/api");

        NormalizedGenAiSpan opus = Span('a', 1, "13:00", "claude-opus-5.5", null, 1_200_000, 50_000, 800_000, 100_000);
        NormalizedGenAiSpan foundry = Span('a', 2, "13:01", "gpt-6.1-sol", "contoso.openai.azure.com", 100_000, 10_000);
        NormalizedGenAiSpan mai = Span('b', 1, "13:10", "mai-code-2", null, 1_000_000, 100_000, 400_000);
        NormalizedGenAiSpan qwen = Span('b', 2, "13:11", "qwen3.8-27b-cuda-gpu:1", "127.0.0.1", 400_000, 12_000);
        NormalizedGenAiSpan unknown = Span('c', 1, "13:20", "gpt-unknown", null, 1_000, 100);
        NormalizedGenAiSpan mystery = Span('c', 2, "13:21", "mystery-1", "llm.example.com", 2_000, 200);
        NormalizedGenAiSpan onMain = Span('d', 1, "13:30", "claude-opus-5.5", null, 10_000, 1_000);
        NormalizedGenAiSpan onApi = Span('e', 1, "13:40", "mai-code-2", null, 10_000, 1_000);

        int recorded = await store.RecordAsync(
        [
            new LedgerEntry(opus, embedded.Calculate(opus), feature),
            new LedgerEntry(foundry, byok.Calculate(foundry), feature),
            new LedgerEntry(mai, embedded.Calculate(mai), feature),
            new LedgerEntry(qwen, embedded.Calculate(qwen), feature),
            new LedgerEntry(unknown, embedded.Calculate(unknown), feature),
            new LedgerEntry(mystery, embedded.Calculate(mystery), feature),
            new LedgerEntry(onMain, embedded.Calculate(onMain), main),
            new LedgerEntry(onApi, embedded.Calculate(onApi), api),
        ]);

        await store.RecordBreakerTripAsync(Trip(new string('a', 32), "feature/ledger"));
        long? reset = await store.RecordBreakerTripAsync(Trip(new string('b', 32), "feature/ledger"));
        await store.ResetBreakerTripsAsync(new BreakerTripFilter { Id = reset });
        await store.RecordBreakerTripAsync(Trip(new string('d', 32), "main"));
        return recorded;
    }

    private static NormalizedGenAiSpan Span(
        char trace,
        int index,
        string time,
        string model,
        string? serverAddress,
        long inputTokens,
        long outputTokens,
        long cacheReadInputTokens = 0,
        long cacheWriteInputTokens = 0) =>
        TestSpans.Chat(
            startTime: DateTimeOffset.Parse($"2026-11-21T{time}:00Z", CultureInfo.InvariantCulture),
            requestModel: model,
            serverAddress: serverAddress,
            inputTokens: inputTokens,
            outputTokens: outputTokens,
            cacheReadInputTokens: cacheReadInputTokens,
            cacheWriteInputTokens: cacheWriteInputTokens) with
        {
            TraceId = new string(trace, 32),
            SpanId = new string(trace, 15) + index.ToString(CultureInfo.InvariantCulture),
        };

    private static NewBreakerTrip Trip(string traceId, string branch) => new()
    {
        Rule = BreakerRule.CreditVelocity,
        TraceId = traceId,
        Repository = "swigerb/copilot-ledger",
        Branch = branch,
        TrippedAt = DateTimeOffset.Parse("2026-11-21T14:00:00Z", CultureInfo.InvariantCulture),
        Detail = "180.00 AI credits in 5 minutes",
        WindowCostUsd = 1.8m,
        WindowMinutes = 5,
    };
}
