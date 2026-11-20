namespace CopilotLedger.Core.Pricing;

public static class AiCreditMath
{
    public const decimal UsdPerAiCredit = 0.01m;
    public const decimal TokensPerRateUnit = 1_000_000m;

    // tokens * ratePerMillion / TokensPerRateUnit. Throws ArgumentOutOfRangeException if either argument is negative.
    public static decimal CostUsd(long tokens, decimal ratePerMillion)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        ArgumentOutOfRangeException.ThrowIfNegative(ratePerMillion);

        return tokens * ratePerMillion / TokensPerRateUnit;
    }

    // usd / UsdPerAiCredit
    public static decimal UsdToAiCredits(decimal usd) => usd / UsdPerAiCredit;
}
