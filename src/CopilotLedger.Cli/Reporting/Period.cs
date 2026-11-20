using System.Globalization;

namespace CopilotLedger.Cli.Reporting;

internal sealed record Period(string Label, DateTimeOffset? From)
{
    public static bool TryParse(
        string? value,
        CliContext context,
        out Period period)
    {
        string text = value?.Trim() ?? string.Empty;
        DateTimeOffset now = context.TimeProvider.GetUtcNow();
        TimeZoneInfo zone = context.TimeProvider.LocalTimeZone;
        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, zone).DateTime);

        if (text.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            period = new Period("today", Midnight(today, zone));
            return true;
        }

        if (text.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            period = new Period("all time", null);
            return true;
        }

        if (text.EndsWith("d", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(
                text[..^1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int days) &&
            days is >= 1 and <= 3650)
        {
            period = new Period(
                days == 1 ? "today" : $"last {days} days",
                Midnight(today.AddDays(-(days - 1)), zone));
            return true;
        }

        if (DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly date))
        {
            period = new Period($"since {text}", Midnight(date, zone));
            return true;
        }

        period = null!;
        return false;
    }

    private static DateTimeOffset Midnight(DateOnly date, TimeZoneInfo zone)
    {
        DateTime local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
