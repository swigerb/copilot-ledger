using CopilotLedger.Otlp.Breaker;

namespace CopilotLedger.Tests.TestSupport;

public sealed class RecordingAlertSink : IBreakerAlertSink
{
    public List<BreakerAlert> Alerts { get; } = [];
    public List<string> Warnings { get; } = [];

    public void Publish(BreakerAlert alert) => Alerts.Add(alert);

    public void Warn(string message) => Warnings.Add(message);
}
