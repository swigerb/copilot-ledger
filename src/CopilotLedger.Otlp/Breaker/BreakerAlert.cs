namespace CopilotLedger.Otlp.Breaker;

public enum BreakerAlertKind
{
    Trip,
    Budget,
}

public sealed record BreakerAlert
{
    public required BreakerAlertKind Kind { get; init; }
    public long? TripId { get; init; }                 // Trip alerts only
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required bool ShowNotification { get; init; } // BreakerSettings.Notifications when the alert was raised
}

public interface IBreakerAlertSink
{
    void Publish(BreakerAlert alert);                  // must return quickly and not throw
    void Warn(string message);                         // a problem the monitor handled, such as a flag file it couldn't write; must not throw
}
