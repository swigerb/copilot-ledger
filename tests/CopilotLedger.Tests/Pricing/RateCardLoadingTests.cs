using CopilotLedger.Core.Pricing;
using CopilotLedger.Tests.TestSupport;

namespace CopilotLedger.Tests.Pricing;

public sealed class RateCardLoadingTests
{
    public static TheoryData<string, string> InvalidDocuments => new()
    {
        { "\"schemaVersion\": 1,", "\"schemaVersion\": 2," },
        { "\"inputPer1M\": 2.00,", "\"inputPer1M\": 2.0000001," },
        { "\"baselineModel\": \"claude-sonnet-5.5\"", "\"baselineModel\": \"gpt-missing\"" },
        { "\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"currency\": \"USD\"," },
        { "\"maxInputTokens\": null, \"inputPer1M\": 2.00", "\"maxInputTokens\": 1000, \"inputPer1M\": 2.00" },
        { "\"inputPer1M\": 2.00,", "\"inputPer1M\": -2.00," },
        { "\"ratesVersion\": \"test-fixture\",", string.Empty },
    };

    [Fact]
    public void Parse_Fixture_Succeeds()
    {
        Assert.Equal(
            "test-fixture",
            JsonRateCardCalculator.Parse(RateFixtures.ByokTestRatesJson).RatesVersion);
    }

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public void Parse_InvalidDocument_Throws(string find, string replacement)
    {
        string json = RateFixtures.ByokTestRatesJson;
        Assert.Contains(find, json);
        json = json.Replace(find, replacement, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => JsonRateCardCalculator.Parse(json));
    }

    [Fact]
    public void Parse_MalformedJson_Throws()
    {
        string json = RateFixtures.ByokTestRatesJson[..^1];
        Assert.Throws<InvalidDataException>(() => JsonRateCardCalculator.Parse(json));
    }

    [Fact]
    public void Parse_NullCollectionElement_ThrowsWithExactPath()
    {
        string json = RateFixtures.ByokTestRatesJson.Replace(
            "\"hostSuffixes\": [\"githubcopilot.com\"]",
            "\"hostSuffixes\": [\"githubcopilot.com\", null]",
            StringComparison.Ordinal);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => JsonRateCardCalculator.Parse(json));

        Assert.Contains("githubAiCredits.hostSuffixes[1]", exception.Message);
        Assert.DoesNotContain("$.githubAiCredits", exception.Message);
    }
}
