namespace CopilotLedger.Core.Breaker;

public sealed record VelocityTrip(
    DateTimeOffset TrippedAt,                          // end of the span whose window first exceeded the threshold
    decimal WindowCostUsd);                            // the window's total at that span

public sealed record ChurnTrip(
    DateTimeOffset TrippedAt,                          // start of the failing call that reached the threshold
    int FailureCount);                                 // always the threshold

public static class BreakerRules
{
    // Rule 1. Throws ArgumentNullException for null costs; ArgumentOutOfRangeException unless thresholdUsd > 0 and window > TimeSpan.Zero.
    public static VelocityTrip? FindVelocityTrip(
        IReadOnlyList<CostEvent> costs,
        decimal thresholdUsd,
        TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(costs);
        if (thresholdUsd <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdUsd));
        }

        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        CostEvent[] events = costs
            .Where(cost => cost.CostUsd > 0m)
            .OrderBy(cost => cost.EndTime)
            .ThenBy(cost => cost.SpanId, StringComparer.Ordinal)
            .ToArray();
        decimal sum = 0m;
        int first = 0;
        for (int index = 0; index < events.Length; index++)
        {
            sum += events[index].CostUsd;
            DateTimeOffset cutoff = events[index].EndTime - window;
            while (events[first].EndTime <= cutoff)
            {
                sum -= events[first].CostUsd;
                first++;
            }

            if (sum > thresholdUsd)
            {
                return new VelocityTrip(events[index].EndTime, sum);
            }
        }

        return null;
    }

    // Rule 2. Throws ArgumentNullException for a null list; ArgumentOutOfRangeException unless failures >= 2.
    public static ChurnTrip? FindChurnTrip(
        IReadOnlyList<ToolCallEvent> toolCalls,
        IReadOnlyList<CommitObservation> commits,
        int failures)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);
        ArgumentNullException.ThrowIfNull(commits);
        if (failures < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(failures));
        }

        List<ChurnEvent> events = [];
        events.AddRange(commits.Select(commit =>
            new ChurnEvent(commit.StartTime, 0, commit.SpanId, null, commit)));
        events.AddRange(toolCalls.Select(call =>
            new ChurnEvent(call.StartTime, 1, call.SpanId, call, null)));
        events.Sort(static (left, right) =>
        {
            int byTime = left.StartTime.CompareTo(right.StartTime);
            if (byTime != 0)
            {
                return byTime;
            }

            int byKind = left.Kind.CompareTo(right.Kind);
            return byKind != 0
                ? byKind
                : StringComparer.Ordinal.Compare(left.SpanId, right.SpanId);
        });

        string? lastSha = null;
        int streak = 0;
        foreach (ChurnEvent item in events)
        {
            if (item.Commit is not null)
            {
                if (lastSha is not null
                    && !string.Equals(lastSha, item.Commit.CommitSha, StringComparison.Ordinal))
                {
                    streak = 0;
                }

                lastSha = item.Commit.CommitSha;
                continue;
            }

            ToolCallEvent call = item.ToolCall!;
            if (call.IsFailure)
            {
                streak++;
                if (streak == failures)
                {
                    return new ChurnTrip(call.StartTime, failures);
                }
            }
            else if (call.IsSuccessfulCommand)
            {
                streak = 0;
            }
        }

        return null;
    }

    private sealed record ChurnEvent(
        DateTimeOffset StartTime,
        int Kind,
        string SpanId,
        ToolCallEvent? ToolCall,
        CommitObservation? Commit);
}
