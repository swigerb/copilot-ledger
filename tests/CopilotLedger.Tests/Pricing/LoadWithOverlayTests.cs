using CopilotLedger.Core.Pricing;
using CopilotLedger.Tests.TestSupport;

namespace CopilotLedger.Tests.Pricing;

public sealed class LoadWithOverlayTests
{
    [Fact]
    public void LW1_BaseWithoutOverlayKeepsItsVersion()
    {
        JsonRateCardCalculator calculator =
            JsonRateCardCalculator.LoadWithOverlay(PublishedRates.WithVersion("2026-12-01"), null);

        Assert.Equal("2026-12-01", calculator.RatesVersion);
    }

    [Fact]
    public async Task LW2_OverlayIsMergedOnTheGivenBase()
    {
        using TempDirectory temp = new();
        string overlay = Path.Combine(temp.Path, "rates.json");
        File.WriteAllText(overlay, PublishedRates.GptPreviewOverlay);
        await FileRetry.ReadAllTextAsync(overlay);

        JsonRateCardCalculator calculator =
            JsonRateCardCalculator.LoadWithOverlay(PublishedRates.WithVersion("2026-12-01"), overlay);

        Assert.Equal("2026-12-01+user", calculator.RatesVersion);
    }

    [Fact]
    public async Task LW3_OverlayErrorsStartWithTheOverlayPath()
    {
        using TempDirectory temp = new();
        string overlay = Path.Combine(temp.Path, "rates.json");
        File.WriteAllText(overlay, "{");
        await FileRetry.ReadAllTextAsync(overlay);

        InvalidDataException invalid = Assert.Throws<InvalidDataException>(() =>
            JsonRateCardCalculator.LoadWithOverlay(PublishedRates.WithVersion("2026-12-01"), overlay));
        InvalidDataException missing = Assert.Throws<InvalidDataException>(() =>
            JsonRateCardCalculator.LoadWithOverlay(
                PublishedRates.WithVersion("2026-12-01"),
                Path.Combine(temp.Path, "missing.json")));

        Assert.Equal($"{overlay}: line 1, column 2: invalid JSON", invalid.Message);
        Assert.Equal(
            $"{Path.Combine(temp.Path, "missing.json")}: could not read rates overlay.",
            missing.Message);
    }

    [Fact]
    public void LW4_BaseErrorsHaveNoPrefix()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            JsonRateCardCalculator.LoadWithOverlay("{", null));

        Assert.Equal("line 1, column 2: invalid JSON", exception.Message);
    }
}
