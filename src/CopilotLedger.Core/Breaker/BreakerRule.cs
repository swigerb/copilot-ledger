namespace CopilotLedger.Core.Breaker;

public enum BreakerRule
{
    CreditVelocity,                                    // rule 1
    RetryChurn,                                        // rule 2
}

public static class BreakerRuleNames
{
    public const string CreditVelocity = "credit_velocity";
    public const string RetryChurn = "retry_churn";

    // Throws ArgumentOutOfRangeException for an undefined value.
    public static string ToName(BreakerRule rule) => rule switch
    {
        BreakerRule.CreditVelocity => CreditVelocity,
        BreakerRule.RetryChurn => RetryChurn,
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null),
    };

    // Ordinal. Returns false for any other text, including null.
    public static bool TryParse(string? name, out BreakerRule rule)
    {
        if (string.Equals(name, CreditVelocity, StringComparison.Ordinal))
        {
            rule = BreakerRule.CreditVelocity;
            return true;
        }

        if (string.Equals(name, RetryChurn, StringComparison.Ordinal))
        {
            rule = BreakerRule.RetryChurn;
            return true;
        }

        rule = default;
        return false;
    }
}
