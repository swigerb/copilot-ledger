using CopilotLedger.Storage;

namespace CopilotLedger.Tests.Storage;

public sealed class PicoUsdTests
{
    public static TheoryData<decimal, long> ExactValues => new()
    {
        { 0m, 0L },
        { 0.000000025m, 25_000L },
        { 2.86m, 2_860_000_000_000L },
        { 1.238004m, 1_238_004_000_000L },
    };

    [Theory]
    [MemberData(nameof(ExactValues))]
    public void FromUsdAndToUsd_RoundTripExactly(decimal usd, long picoUsd)
    {
        Assert.Equal(picoUsd, PicoUsd.FromUsd(usd));
        Assert.Equal(usd, PicoUsd.ToUsd(picoUsd));
    }

    public static TheoryData<decimal> InvalidValues => new()
    {
        -0.01m,
        0.0000000000001m,
        10_000_000m,
    };

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public void FromUsd_InvalidValue_ThrowsArgumentOutOfRangeException(decimal usd)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PicoUsd.FromUsd(usd));
    }
}
