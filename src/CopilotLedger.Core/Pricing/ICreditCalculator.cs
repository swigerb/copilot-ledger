using CopilotLedger.Core.Telemetry;

namespace CopilotLedger.Core.Pricing;

public interface ICreditCalculator
{
    string RatesVersion { get; }

    // Pure and thread-safe. Throws only ArgumentNullException; every span problem is reported through PricingStatus.
    CreditCalculationResult Calculate(NormalizedGenAiSpan span);
}
