using System.Net;
using System.Text;
using CopilotLedger.Cli.Daemon;
using CopilotLedger.Cli.Install;
using CopilotLedger.Core.Pricing;

namespace CopilotLedger.Cli.Rates;

internal static class RatesUpdate
{
    public const string DownloadUrl =
        "https://github.com/swigerb/gh-ledger/releases/latest/download/rates.json";

    private const int MaximumBytes = 1_048_576;

    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

    // Uses the system proxy (HttpClient.DefaultProxy) and signs in to it with the current user's credentials.
    public static HttpMessageHandler CreateDefaultHandler() =>
        new SocketsHttpHandler
        {
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };

    public static async Task<int> RunAsync(CliContext context, CancellationToken cancellationToken)
    {
        byte[] bytes;
        using (HttpClient client = new(
            context.CreateHttpHandler?.Invoke() ?? CreateDefaultHandler(),
            disposeHandler: true))
        {
            client.Timeout = DownloadTimeout;
            client.MaxResponseContentBufferSize = MaximumBytes;
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"gh-ledger/{GhLedgerCli.Version}");
            try
            {
                using HttpResponseMessage response = await client.GetAsync(
                    DownloadUrl,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    context.Error.WriteLine(
                        $"Could not download rates from {DownloadUrl}: HTTP {(int)response.StatusCode}.");
                    return 1;
                }

                bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                context.Error.WriteLine($"Could not download rates from {DownloadUrl}: {ex.Message}");
                return 1;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                context.Error.WriteLine(
                    $"Could not download rates from {DownloadUrl}: no response within 30 seconds.");
                return 1;
            }
        }

        string json;
        try
        {
            json = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            context.Error.WriteLine("The downloaded rates are invalid: the file isn't UTF-8.");
            return 1;
        }

        string version;
        try
        {
            version = JsonRateCardCalculator.Parse(json).RatesVersion;
        }
        catch (InvalidDataException ex)
        {
            context.Error.WriteLine($"The downloaded rates are invalid: {ex.Message}");
            return 1;
        }

        if (!RateCardFiles.TryParseVersion(version, out DateOnly published))
        {
            context.Error.WriteLine(
                $"The downloaded rates are invalid: ratesVersion {version} isn't a yyyy-MM-dd date.");
            return 1;
        }

        RatesBase current = RateCardFiles.LoadBase(context);
        if (current.Warning is not null)
        {
            context.Out.WriteLine($"Warning: {current.Warning}");
        }

        DateOnly inUse = RateCardFiles.ParseVersion(current.Version);
        if (published < inUse)
        {
            context.Out.WriteLine(
                $"The published rates ({version}) are older than the rates in use ({current.Version}); nothing changed.");
            return 0;
        }

        if (published == inUse)
        {
            context.Out.WriteLine($"Rates are up to date ({current.Version}).");
            return 0;
        }

        string? overlay = RateCardFiles.OverlayPath(context);
        if (overlay is not null)
        {
            try
            {
                JsonRateCardCalculator.LoadWithOverlay(json, overlay);
            }
            catch (InvalidDataException ex)
            {
                context.Error.WriteLine(
                    $"The downloaded rates ({version}) don't work with your rates overlay: {ex.Message}");
                return 1;
            }
        }

        string path = RateCardFiles.DownloadedPath(context);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(context.DataDirectory);
            await File.WriteAllTextAsync(temp, json, new UTF8Encoding(false), cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DaemonFiles.DeleteIfExists(temp);
            context.Error.WriteLine($"Could not write {path}: {ex.Message}");
            return 1;
        }

        context.Out.WriteLine($"Updated rates from {current.Version} to {version}.");
        if (DaemonFiles.IsHeld(context))
        {
            string command = GhExtension.CommandName(context.Platform);
            context.Out.WriteLine(
                $"Restart the daemon to price new spans with them: {command} daemon stop, then {command} daemon start.");
        }

        return 0;
    }
}
