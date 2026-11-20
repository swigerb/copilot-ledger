using CopilotLedger.Core.Git;
using CopilotLedger.Core.Pricing;
using CopilotLedger.Core.Telemetry;
using CopilotLedger.Storage;

namespace CopilotLedger.Tests.TestSupport;

public static class LedgerSeeds
{
    private const string Sha1 = "0123456789abcdef0123456789abcdef01234567";

    public static async Task<int> SeedChainsAsync(
        SqliteLedgerStore store,
        string repoRoot,
        string apiRoot)
    {
        JsonRateCardCalculator embedded = JsonRateCardCalculator.LoadEmbedded();
        JsonRateCardCalculator byok =
            JsonRateCardCalculator.Parse(RateFixtures.ByokTestRatesJson);
        GitWorkspaceContext repository = new(
            repoRoot,
            Path.Combine(repoRoot, ".git"),
            "main",
            Sha1,
            "https://github.com/swigerb/copilot-ledger.git",
            "swigerb/copilot-ledger");
        GitWorkspaceContext feature = repository with
        {
            BranchName = "feature/ledger",
        };
        GitWorkspaceContext api = new(
            apiRoot,
            Path.Combine(apiRoot, ".git"),
            "main",
            Sha1,
            "https://github.com/contoso/api.git",
            "contoso/api");

        NormalizedGenAiSpan k1 = TestSpans.Chat(
            startTime: Parse("2026-11-21T14:00:00Z"),
            requestModel: "claude-opus-5.5",
            agentName: "ledger-architect",
            conversationId: "conv-1",
            inputTokens: 1_200_000,
            outputTokens: 50_000,
            cacheReadInputTokens: 800_000,
            cacheWriteInputTokens: 100_000) with
        {
            TraceId = new string('1', 32),
            SpanId = "1111111111111111",
        };
        NormalizedGenAiSpan k2 = TestSpans.Chat(
            startTime: Parse("2026-11-21T13:00:00Z"),
            requestModel: "mai-code-2",
            agentName: "ledger-builder",
            conversationId: "conv-2",
            inputTokens: 1_000_000,
            outputTokens: 100_000,
            cacheReadInputTokens: 400_000) with
        {
            TraceId = new string('2', 32),
            SpanId = "2222222222222221",
        };
        NormalizedGenAiSpan k3 = TestSpans.Chat(
            startTime: Parse("2026-11-21T13:05:00Z"),
            requestModel: "qwen3.8-27b-cuda-gpu:1",
            agentName: "ledger-builder",
            conversationId: "conv-2",
            serverAddress: "127.0.0.1",
            serverPort: 11434,
            inputTokens: 400_000,
            outputTokens: 12_000) with
        {
            TraceId = new string('2', 32),
            SpanId = "2222222222222222",
        };
        NormalizedGenAiSpan k4 = TestSpans.Chat(
            startTime: Parse("2026-11-18T12:00:00Z"),
            requestModel: "gpt-6.1-sol",
            serverAddress: "contoso.openai.azure.com",
            inputTokens: 100_000,
            outputTokens: 10_000) with
        {
            TraceId = new string('3', 32),
            SpanId = "3333333333333333",
        };
        NormalizedGenAiSpan k5 = TestSpans.Chat(
            startTime: Parse("2026-10-01T12:00:00Z"),
            requestModel: "gpt-unknown",
            agentName: "ledger-builder",
            conversationId: "conv-4",
            inputTokens: 1_000,
            outputTokens: 100) with
        {
            TraceId = new string('4', 32),
            SpanId = "4444444444444444",
        };

        return await store.RecordAsync(
        [
            new LedgerEntry(k1, embedded.Calculate(k1), repository),
            new LedgerEntry(k2, embedded.Calculate(k2), feature),
            new LedgerEntry(k3, embedded.Calculate(k3), feature),
            new LedgerEntry(k4, byok.Calculate(k4), api),
            new LedgerEntry(k5, embedded.Calculate(k5), null),
        ]);
    }

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
