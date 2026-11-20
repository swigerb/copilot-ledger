using CopilotLedger.Core.Git;
using CopilotLedger.Core.Pricing;
using CopilotLedger.Core.Telemetry;

namespace CopilotLedger.Storage;

public sealed record LedgerEntry(
    NormalizedGenAiSpan Span,
    CreditCalculationResult Calculation,
    GitWorkspaceContext? Git);
