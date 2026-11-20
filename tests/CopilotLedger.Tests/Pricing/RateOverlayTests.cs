using CopilotLedger.Core.Pricing;
using CopilotLedger.Tests.TestSupport;

namespace CopilotLedger.Tests.Pricing;

public sealed class RateOverlayTests
{
    private const string OverlayA = """
        {
          "schemaVersion": 1,
          "ratesVersion": "contoso-2026-11",
          "notes": ["Azure prices from our November 2026 invoice."],
          "foundryByok": {
            "models": [
              {
                "id": "gpt-6.1-sol",
                "aliases": ["contoso-gpt61-prod"],
                "windows": [
                  { "tiers": [ { "name": "default", "inputPer1M": 1.75, "cachedInputPer1M": 0.175, "outputPer1M": 14.00 } ] }
                ]
              }
            ]
          }
        }
        """;

    public static TheoryData<string> InvalidOverlays => new()
    {
        """{ "schemaVersion": 2 }""",
        """{ "schemaVersion": 1, "foundryBYOK": { } }""",
        """{ "schemaVersion": 1, "localOffload": { "baselineModel": "gpt-missing" } }""",
        """{ "schemaVersion": 1, "foundryByok": { "models": [ { "id": "gpt-6.1-sol", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1.75 } ] } ] } ] } }""",
        """{ }""",
        """{ "schemaVersion": 1, "localOffload": { "models": [ { "id": "my-local", "aliases": ["qwen3.8:27b"] } ] } }""",
    };

    public static TheoryData<string, decimal, decimal> GitHubModelOverlays => new()
    {
        { "example-new-model", 5.00m, 500m },
        { "claude-opus-5.5", 18.00m, 1800m },
    };

    [Theory]
    [InlineData("gpt-6.1-sol")]
    [InlineData("contoso-gpt61-prod")]
    public void Overlay_AddsByokModelAndAlias(string model)
    {
        using TempDirectory temp = new();
        string path = WriteOverlay(temp, OverlayA);
        CreditCalculationResult result = JsonRateCardCalculator.LoadEmbeddedWithOverlay(path).Calculate(
            TestSpans.Chat(
                serverAddress: "contoso.openai.azure.com",
                requestModel: model,
                inputTokens: 100_000,
                outputTokens: 10_000));

        Assert.Equal(BillingChannel.FoundryByok, result.Channel);
        Assert.Equal(PricingStatus.Priced, result.Status);
        Assert.Equal("gpt-6.1-sol", result.ModelId);
        Assert.Equal(0.315m, result.UsdCost);
        Assert.Equal(0m, result.AiCredits);
        Assert.Equal("2026-11-20+contoso-2026-11", result.RatesVersion);
    }

    [Fact]
    public void Overlay_DoesNotChangeGitHubPrices()
    {
        using TempDirectory temp = new();
        JsonRateCardCalculator calculator = JsonRateCardCalculator.LoadEmbeddedWithOverlay(WriteOverlay(temp, OverlayA));
        CreditCalculationResult result = calculator.Calculate(
            TestSpans.Chat(
                requestModel: "claude-sonnet-5.5",
                inputTokens: 10_000,
                outputTokens: 2_000));
        Assert.Equal(PricingStatus.Priced, result.Status);
        Assert.Equal(0.04m, result.UsdCost);
    }

    [Fact]
    public void Overlay_CanReplaceLocalBaseline()
    {
        using TempDirectory temp = new();
        JsonRateCardCalculator calculator = JsonRateCardCalculator.LoadEmbeddedWithOverlay(
            WriteOverlay(temp, """{ "schemaVersion": 1, "localOffload": { "baselineModel": "claude-sonnet-5.5" } }"""));

        CreditCalculationResult result = calculator.Calculate(TestSpans.Chat(
            serverAddress: "127.0.0.1",
            requestModel: "qwen3.8:27b",
            inputTokens: 400_000,
            outputTokens: 12_000));
        Assert.Equal(PricingStatus.LocalOffload, result.Status);
        Assert.Equal(0.92m, result.EstimatedCloudSavedUsd);
        Assert.Equal("claude-sonnet-5.5", result.RateCardModelId);
        Assert.Equal("2026-11-20+user", result.RatesVersion);
    }

    [Fact]
    public void Overlay_CanAddLocalHost()
    {
        using TempDirectory temp = new();
        JsonRateCardCalculator calculator = JsonRateCardCalculator.LoadEmbeddedWithOverlay(
            WriteOverlay(temp, """{ "schemaVersion": 1, "localOffload": { "additionalHosts": ["gpu-box.lan"] } }"""));
        CreditCalculationResult result = calculator.Calculate(
            TestSpans.Chat(
                serverAddress: "gpu-box.lan",
                requestModel: "qwen3.8:27b",
                inputTokens: 400_000,
                outputTokens: 12_000));
        Assert.Equal(BillingChannel.Local, result.Channel);
        Assert.Equal(PricingStatus.LocalOffload, result.Status);
        Assert.Equal(0.23m, result.EstimatedCloudSavedUsd);
    }

    [Theory]
    [MemberData(nameof(GitHubModelOverlays))]
    public void Overlay_CanAddOrReplaceGitHubModel(string model, decimal usd, decimal credits)
    {
        string overlay = model == "example-new-model"
            ? """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "example-new-model", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1.00, "outputPer1M": 4.00 } ] } ] } ] } }"""
            : """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "claude-opus-5.5", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 3.00, "cachedInputPer1M": 0.30, "cacheWritePer1M": 3.75, "outputPer1M": 15.00 } ] } ] } ] } }""";
        using TempDirectory temp = new();
        CreditCalculationResult result = JsonRateCardCalculator.LoadEmbeddedWithOverlay(WriteOverlay(temp, overlay)).Calculate(
            TestSpans.Chat(
                requestModel: model,
                inputTokens: 1_000_000,
                outputTokens: 1_000_000));

        Assert.Equal(usd, result.UsdCost);
        Assert.Equal(credits, result.AiCredits);
    }

    [Fact]
    public void Overlay_ReplacesLocalModelAliases()
    {
        using TempDirectory temp = new();
        JsonRateCardCalculator calculator = JsonRateCardCalculator.LoadEmbeddedWithOverlay(
            WriteOverlay(temp, """{ "schemaVersion": 1, "localOffload": { "models": [ { "id": "qwen3.8-27b", "aliases": ["my-qwen-build"] } ] } }"""));
        CreditCalculationResult result = calculator.Calculate(
            TestSpans.Chat(
                requestModel: "my-qwen-build",
                inputTokens: 400_000,
                outputTokens: 12_000));

        Assert.Equal(BillingChannel.Local, result.Channel);
        Assert.Equal("qwen3.8-27b", result.ModelId);
        Assert.Equal(0.23m, result.EstimatedCloudSavedUsd);
    }

    [Theory]
    [MemberData(nameof(InvalidOverlays))]
    public void InvalidOverlay_ThrowsWithFilePath(string overlay)
    {
        using TempDirectory temp = new();
        string path = WriteOverlay(temp, overlay);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => JsonRateCardCalculator.LoadEmbeddedWithOverlay(path));
        Assert.Contains(path, exception.Message);
    }

    [Fact]
    public void MissingOverlay_ThrowsFileNotFoundException()
    {
        using TempDirectory temp = new();
        string path = System.IO.Path.Combine(temp.Path, "missing.json");
        Assert.Throws<FileNotFoundException>(() => JsonRateCardCalculator.LoadEmbeddedWithOverlay(path));
    }

    private static string WriteOverlay(TempDirectory temp, string overlay)
    {
        string path = System.IO.Path.Combine(temp.Path, "rates.json");
        File.WriteAllText(path, overlay);
        return path;
    }
}
