using CopilotLedger.Cli;

return await GhLedgerCli.InvokeAsync(args, CliContext.CreateDefault(Console.Out, Console.Error));
