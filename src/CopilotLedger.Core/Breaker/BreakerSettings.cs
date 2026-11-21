using CopilotLedger.Core.Pricing;

namespace CopilotLedger.Core.Breaker;

public sealed record BreakerSettings
{
    public const decimal MinVelocityCredits = 1m;
    public const decimal MaxVelocityCredits = 1_000_000m;
    public const int MinVelocityWindowMinutes = 1;
    public const int MaxVelocityWindowMinutes = 1440;
    public const int MinChurnFailures = 2;
    public const int MaxChurnFailures = 100;

    public static BreakerSettings Default { get; } = new();

    public decimal VelocityCredits { get; init; } = 150m;          // rule 1 threshold in AI credits
    public int VelocityWindowMinutes { get; init; } = 5;           // rule 1 window
    public int ChurnFailures { get; init; } = 5;                   // rule 2 threshold
    public bool FlagFile { get; init; }                            // keep <repo root>/.copilot-ledger/CIRCUIT_OPEN while a trip is open
    public bool Notifications { get; init; } = true;               // show desktop notifications; alerts are always logged

    public decimal VelocityThresholdUsd => VelocityCredits * AiCreditMath.UsdPerAiCredit;
    public TimeSpan VelocityWindow => TimeSpan.FromMinutes(VelocityWindowMinutes);

    // Limits are inclusive. "At most two decimal places" is value % 0.01m == 0m throughout this plan; it doesn't round.
    public bool IsValid =>
        VelocityCredits >= MinVelocityCredits
        && VelocityCredits <= MaxVelocityCredits
        && VelocityCredits % 0.01m == 0m
        && VelocityWindowMinutes is >= MinVelocityWindowMinutes and <= MaxVelocityWindowMinutes
        && ChurnFailures is >= MinChurnFailures and <= MaxChurnFailures;
}
