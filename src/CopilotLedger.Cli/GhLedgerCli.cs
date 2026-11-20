using System.CommandLine;
using System.Reflection;
using CopilotLedger.Cli.Daemon;
using CopilotLedger.Cli.Rates;

namespace CopilotLedger.Cli;

public static class GhLedgerCli
{
    public static string Version { get; } =
        typeof(GhLedgerCli).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.2.0";

    // Builds a new RootCommand, parses args, and invokes it with
    // new InvocationConfiguration { Output = context.Out, Error = context.Error, ProcessTerminationTimeout = null }.
    public static Task<int> InvokeAsync(
        string[] args,
        CliContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);
        RootCommand root = new(
            "CopilotLedger: local AI spend ledger for GitHub Copilot");
        root.Add(DaemonCommands.Create(context));
        root.Add(RatesCommands.Create(context));
        return root.Parse(args).InvokeAsync(
            new InvocationConfiguration
            {
                Output = context.Out,
                Error = context.Error,
                ProcessTerminationTimeout = null,
            },
            cancellationToken);
    }
}
