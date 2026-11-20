using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotLedger.Cli.Daemon;

internal sealed record DaemonState
{
    public required int SchemaVersion { get; init; }
    public required int ProcessId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required string Endpoint { get; init; }
    public required string Version { get; init; }
    public required string DatabasePath { get; init; }
}

internal static class DaemonFiles
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string LockPath(CliContext context) =>
        Path.Combine(context.DataDirectory, "daemon.lock");

    public static string StatePath(CliContext context) =>
        Path.Combine(context.DataDirectory, "daemon.json");

    public static string StopPath(CliContext context) =>
        Path.Combine(context.DataDirectory, "daemon.stop");

    public static string LogPath(CliContext context) =>
        Path.Combine(context.DataDirectory, "daemon.log");

    public static FileStream AcquireLock(CliContext context) =>
        new(
            LockPath(context),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

    public static bool IsHeld(CliContext context)
    {
        string lockPath = LockPath(context);
        if (!File.Exists(lockPath))
        {
            return false;
        }

        try
        {
            using FileStream lockStream = AcquireLock(context);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public static async Task WriteStateAsync(
        CliContext context,
        DaemonState state,
        CancellationToken cancellationToken)
    {
        string statePath = StatePath(context);
        string temporaryPath = statePath + ".tmp";
        await using FileStream stream = new(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Dispose();
        File.Move(temporaryPath, statePath, overwrite: true);
    }

    public static DaemonState? TryReadState(CliContext context)
    {
        try
        {
            string json = File.ReadAllText(StatePath(context));
            return JsonSerializer.Deserialize<DaemonState>(json, SerializerOptions);
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            return null;
        }
    }

    public static void DeleteIfExists(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
