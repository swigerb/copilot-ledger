namespace CopilotLedger.Storage;

public sealed record BranchBudgetRow
{
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required decimal BudgetAiCredits { get; init; }
    public required decimal SpentAiCredits { get; init; }                // all-time billed spend on the key: GitHub AI credits plus BYOK USD / 0.01
    public required DateTimeOffset SetAt { get; init; }
    public DateTimeOffset? AlertedAt { get; init; }                      // when the monitor first saw spend exceed the budget
    public DateTimeOffset? NotifiedAt { get; init; }                     // when the daemon finished that alert
    public bool Exceeded => SpentAiCredits > BudgetAiCredits;            // strict: spend exactly at the budget isn't exceeded
}
