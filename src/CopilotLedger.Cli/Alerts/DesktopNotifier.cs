using System.ComponentModel;
using System.Security;
using System.Text;

namespace CopilotLedger.Cli.Alerts;

internal static class DesktopNotifier
{
    public static async Task ShowAsync(
        CliContext context,
        string title,
        string body,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        string fileName;
        IReadOnlyList<string> arguments;
        if (context.Platform.IsWindows)
        {
            string? configuredRoot = context.Platform.GetEnvironmentVariable("SystemRoot");
            string systemRoot = string.IsNullOrEmpty(configuredRoot)
                ? @"C:\Windows"
                : configuredRoot.TrimEnd('\\');
            fileName =
                systemRoot + @"\System32\WindowsPowerShell\v1.0\powershell.exe";
            string xmlPath = Path.Combine(context.DataDirectory, "alert-toast.xml");
            string xml =
                $"<toast><visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text></binding></visual></toast>";
            try
            {
                await File.WriteAllTextAsync(
                    xmlPath,
                    xml,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);
            }
            catch (Exception ex) when (
                ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                log($"Notification failed: {ex.Message}");
                return;
            }

            string escapedPath = xmlPath
                .Replace("'", "''", StringComparison.Ordinal)
                .Replace("\u2018", "\u2018\u2018", StringComparison.Ordinal)
                .Replace("\u2019", "\u2019\u2019", StringComparison.Ordinal)
                .Replace("\u201A", "\u201A\u201A", StringComparison.Ordinal)
                .Replace("\u201B", "\u201B\u201B", StringComparison.Ordinal);
            string script =
                """$ErrorActionPreference='Stop'; [void][Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]; [void][Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom,ContentType=WindowsRuntime]; $x=New-Object Windows.Data.Xml.Dom.XmlDocument; $x.LoadXml([IO.File]::ReadAllText('XMLPATH')); $t=[Windows.UI.Notifications.ToastNotification]::new($x); $t.Tag='copilot-ledger'; $t.Group='alerts'; [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe').Show($t)"""
                    .Replace("XMLPATH", escapedPath, StringComparison.Ordinal);
            arguments = ["-NoProfile", "-NonInteractive", "-Command", script];
        }
        else if (context.Platform.IsMacOS)
        {
            fileName = "/usr/bin/osascript";
            arguments =
            [
                "-e",
                "on run argv",
                "-e",
                "display notification (item 2 of argv) with title (item 1 of argv)",
                "-e",
                "end run",
                title,
                body,
            ];
        }
        else
        {
            string? notifySend = context.Platform.FindOnPath("notify-send");
            if (notifySend is null)
            {
                log("Notification skipped: notify-send was not found.");
                return;
            }

            fileName = notifySend;
            arguments = ["--app-name=CopilotLedger", title, body];
        }

        try
        {
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            ProcessRunResult result = await context.Platform.RunAsync(
                fileName,
                arguments,
                timeout.Token);
            if (result.ExitCode != 0)
            {
                string error = result.StandardError.Trim();
                log(error.Length == 0
                    ? $"Notification failed: {fileName} exited with code {result.ExitCode}."
                    : $"Notification failed: {fileName} exited with code {result.ExitCode}: {error}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log("Notification timed out after 15 seconds.");
        }
        catch (Exception ex) when (
            ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            log($"Notification failed: {ex.Message}");
        }
    }
}
