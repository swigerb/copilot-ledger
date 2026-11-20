namespace CopilotLedger.Tests.TestSupport;

public sealed class ManualTimeProvider : TimeProvider
{
    public static readonly TimeZoneInfo UtcMinus5 = TimeZoneInfo.CreateCustomTimeZone(
        "CopilotLedger-Test-0500",
        TimeSpan.FromHours(-5),
        "UTC-05:00 (test)",
        "UTC-05:00 (test)");

    private DateTimeOffset _utcNow;

    public ManualTimeProvider(
        DateTimeOffset utcNow,
        TimeZoneInfo? localTimeZone = null)
    {
        _utcNow = utcNow;
        LocalTimeZone = localTimeZone ?? TimeZoneInfo.Utc;
    }

    public override TimeZoneInfo LocalTimeZone { get; }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
}
