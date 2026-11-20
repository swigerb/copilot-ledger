using CopilotLedger.Core.Pricing;

namespace CopilotLedger.Tests.Pricing;

public sealed class AiCreditMathTests
{
    public static TheoryData<long, decimal, decimal> CostUsdCases => new()
    {
        { 1, 0.025m, 0.000000025m },
        { 272_001, 4.00m, 1.088004m },
        { 1_000_000, 20.00m, 20m },
        { 0, 4.00m, 0m },
    };

    [Theory]
    [MemberData(nameof(CostUsdCases))]
    public void CostUsd_ValidInputs_ReturnsExpectedCost(long tokens, decimal ratePerMillion, decimal expected)
    {
        Assert.Equal(expected, AiCreditMath.CostUsd(tokens, ratePerMillion));
    }

    [Fact]
    public void CostUsd_NegativeTokens_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AiCreditMath.CostUsd(-1, 4.00m));
    }

    [Fact]
    public void CostUsd_NegativeRate_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AiCreditMath.CostUsd(1, -0.01m));
    }

    public static TheoryData<decimal, decimal> UsdToAiCreditsCases => new()
    {
        { 2.86m, 286m },
        { 1.238004m, 123.8004m },
        { 0.000000025m, 0.0000025m },
        { 0m, 0m },
    };

    [Theory]
    [MemberData(nameof(UsdToAiCreditsCases))]
    public void UsdToAiCredits_Usd_ReturnsExpectedCredits(decimal usd, decimal expected)
    {
        Assert.Equal(expected, AiCreditMath.UsdToAiCredits(usd));
    }

    [Fact]
    public void TotalUsd_SumsAllComponents()
    {
        var breakdown = new TokenCostBreakdown(1.20m, 0.16m, 0.50m, 1.00m);

        Assert.Equal(2.86m, breakdown.TotalUsd);
    }
}
