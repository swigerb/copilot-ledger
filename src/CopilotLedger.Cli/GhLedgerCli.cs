using System.CommandLine;
using System.Reflection;
using CopilotLedger.Cli.Daemon;
using CopilotLedger.Cli.Install;
using CopilotLedger.Cli.Rates;
using CopilotLedger.Cli.Reporting;

namespace CopilotLedger.Cli;

public static class GhLedgerCli
{
    // AssemblyInformationalVersionAttribute: "0.2.0"
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
        foreach (Command command in ReportCommands.Create(context))
        {
            root.Add(command);
        }

        foreach (Command command in InstallCommands.Create(context))
        {
            root.Add(command);
        }

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
