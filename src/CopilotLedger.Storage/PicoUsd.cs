namespace CopilotLedger.Storage;

public static class PicoUsd
{
    public const long PerUsd = 1_000_000_000_000L;

    public static long FromUsd(decimal usd)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(usd);

        decimal picoUsd;
        try
        {
            picoUsd = usd * PerUsd;
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usd),
                usd,
                "The amount exceeds long.MaxValue pico-USD.");
        }

        if (decimal.Truncate(picoUsd) != picoUsd || picoUsd > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(usd), usd, "The amount must be exactly representable in pico-USD and must not exceed long.MaxValue.");
        }

        return (long)picoUsd;
    }

    public static decimal ToUsd(long picoUsd) => picoUsd / 1_000_000_000_000m;
}
