using System.Runtime.InteropServices;
using System.Text;
using CopilotLedger.Core.Git;
using CopilotLedger.Core.Pricing;
using CopilotLedger.Otlp.Attribution;
using CopilotLedger.Otlp.Ingestion;
using CopilotLedger.Otlp.Receiver;
using CopilotLedger.Storage;
using Microsoft.AspNetCore.Builder;

namespace CopilotLedger.Cli.Daemon;

internal static class DaemonHost
{
    public static async Task<int> RunAsync(
        CliContext context,
        int port,
        string? logFile,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(context.DataDirectory);
        TextWriter writer = CreateLogWriter(context, logFile, out bool ownsWriter);
        FileStream lockStream;
        try
        {
            lockStream = DaemonFiles.AcquireLock(context);
        }
        catch (IOException)
        {
            try
            {
                WriteLog(writer, context, "gh-ledger daemon is already running.");
            }
            finally
            {
                if (ownsWriter)
                {
                    writer.Dispose();
                }
            }

            return 1;
        }

        WebApplication? app = null;
        bool started = false;
        PosixSignalRegistration? sighupRegistration = null;
        try
        {
            DaemonFiles.DeleteIfExists(DaemonFiles.StopPath(context));

            JsonRateCardCalculator calculator;
            try
            {
                calculator = File.Exists(context.RatesOverlayPath)
                    ? JsonRateCardCalculator.LoadEmbeddedWithOverlay(context.RatesOverlayPath)
                    : JsonRateCardCalculator.LoadEmbedded();
            }
            catch (InvalidDataException ex)
            {
                WriteLog(writer, context, $"Invalid rates overlay: {ex.Message}");
                return 1;
            }

            SqliteLedgerStore store = new(context.DatabasePath, context.TimeProvider);
            try
            {
                await store.InitializeAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                WriteLog(
                    writer,
                    context,
                    $"Cannot open the ledger at {context.DatabasePath}: {ex.Message}");
                return 1;
            }

            DateTimeOffset lastPrune = context.TimeProvider.GetUtcNow();
            await store.PruneSpanLinksAsync(
                lastPrune - TimeSpan.FromDays(7),
                cancellationToken);
            LedgerIngestor ingestor = new(
                store,
                calculator,
                new CachingGitContextResolver(
                    new FileSystemGitContextResolver(),
                    context.TimeProvider),
                new CopilotSessionWorkspaceLocator(context.CopilotHome, context.TimeProvider));
            app = OtlpReceiverApp.Build(
                new OtlpReceiverOptions { Port = port },
                ingestor,
                context.ConfigureWebHost);

            if (!OperatingSystem.IsWindows())
            {
                sighupRegistration = PosixSignalRegistration.Create(
                    PosixSignal.SIGHUP,
                    signal => signal.Cancel = true);
            }

            try
            {
                await app.StartAsync(cancellationToken);
                started = true;
            }
            catch (IOException)
            {
                WriteLog(
                    writer,
                    context,
                    $"Port {port} is in use. Stop the other program, or run gh-ledger daemon run --port {port}.");
                return 1;
            }

            DaemonState state = new()
            {
                SchemaVersion = 1,
                ProcessId = Environment.ProcessId,
                StartedAt = context.TimeProvider.GetUtcNow(),
                Endpoint = $"http://localhost:{port}",
                Version = GhLedgerCli.Version,
                DatabasePath = context.DatabasePath,
            };
            await DaemonFiles.WriteStateAsync(context, state, cancellationToken);
            WriteLog(
                writer,
                context,
                $"gh-ledger {GhLedgerCli.Version} daemon listening on http://localhost:{port} (pid {Environment.ProcessId}); ledger {context.DatabasePath}");

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                app.Lifetime.ApplicationStopping);
            while (!linked.IsCancellationRequested)
            {
                if (File.Exists(DaemonFiles.StopPath(context)))
                {
                    DaemonFiles.DeleteIfExists(DaemonFiles.StopPath(context));
                    break;
                }

                DateTimeOffset now = context.TimeProvider.GetUtcNow();
                if (now - lastPrune >= TimeSpan.FromHours(6))
                {
                    await store.PruneSpanLinksAsync(
                        now - TimeSpan.FromDays(7),
                        cancellationToken);
                    lastPrune = now;
                }

                try
                {
                    await Task.Delay(context.DaemonPollInterval, linked.Token);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    break;
                }
            }

            return 0;
        }
        finally
        {
            sighupRegistration?.Dispose();
            if (started && app is not null)
            {
                await app.StopAsync(CancellationToken.None);
            }

            DaemonFiles.DeleteIfExists(DaemonFiles.StatePath(context));
            if (app is not null)
            {
                await app.DisposeAsync();
            }

            lockStream.Dispose();
            if (started)
            {
                WriteLog(writer, context, "gh-ledger daemon stopped");
            }

            if (ownsWriter)
            {
                writer.Dispose();
            }
        }
    }

    private static TextWriter CreateLogWriter(
        CliContext context,
        string? logFile,
        out bool ownsWriter)
    {
        if (logFile is null)
        {
            ownsWriter = false;
            return context.Out;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(logFile))!;
        Directory.CreateDirectory(directory);
        ownsWriter = true;
        return new StreamWriter(
            new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
    }

    private static void WriteLog(TextWriter writer, CliContext context, string message) =>
        writer.WriteLine($"{context.TimeProvider.GetUtcNow():O} {message}");
}
