using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text;
using CopilotLedger.Core.Pricing;

namespace CopilotLedger.Cli.Rates;

internal static class RatesCommands
{
    private const string Scaffold = """
        {
          "schemaVersion": 1,
          "notes": [
            "CopilotLedger rates overlay. See docs/configuration.md in the CopilotLedger repository.",
            "Prices are USD per 1,000,000 tokens, from 0 to 1,000,000, with at most 6 decimal places."
          ],
          "foundryByok": {
            "hostSuffixes": [],
            "models": []
          }
        }
        """;

    public static Command Create(CliContext context)
    {
        Command rates = new("rates", "Create and validate rates overlays.");
        Command init = new("init", "Create a starter rates overlay.");
        Option<bool> force = new("--force")
        {
            Description = "Replace an existing rates overlay.",
        };
        init.Add(force);
        init.SetAction(
            (ParseResult result, CancellationToken cancellationToken) =>
                InitializeAsync(context, result.GetValue(force), cancellationToken));
        rates.Add(init);

        Command validate = new("validate", "Validate a rates overlay.");
        Option<string?> file = new("--file")
        {
            Description = "Path to the rates overlay.",
        };
        validate.Add(file);
        validate.SetAction(
            (ParseResult result, CancellationToken _) =>
                Task.FromResult(Validate(context, result.GetValue(file))));
        rates.Add(validate);
        return rates;
    }

    private static async Task<int> InitializeAsync(
        CliContext context,
        bool force,
        CancellationToken cancellationToken)
    {
        string path = context.RatesOverlayPath;
        if (!force && File.Exists(path))
        {
            context.Error.WriteLine($"{path} already exists. Use --force to replace it.");
            return 1;
        }

        Directory.CreateDirectory(context.DataDirectory);
        await File.WriteAllTextAsync(
            path,
            Scaffold + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        context.Out.WriteLine(
            $"Created {path}. Add your prices, then run: gh-ledger rates validate");
        return 0;
    }

    private static int Validate(CliContext context, string? file)
    {
        bool explicitFile = file is not null;
        string path = explicitFile
            ? Path.GetFullPath(file!, context.WorkingDirectory)
            : context.RatesOverlayPath;
        if (!File.Exists(path))
        {
            if (explicitFile)
            {
                context.Error.WriteLine($"{path} does not exist.");
                return 1;
            }

            context.Out.WriteLine(
                $"No rates overlay at {path}; using the built-in rates ({JsonRateCardCalculator.LoadEmbedded().RatesVersion}).");
            return 0;
        }

        try
        {
            JsonRateCardCalculator calculator =
                JsonRateCardCalculator.LoadEmbeddedWithOverlay(path);
            context.Out.WriteLine(
                $"{path} is valid. Rates version: {calculator.RatesVersion}.");
            return 0;
        }
        catch (InvalidDataException ex)
        {
            context.Error.WriteLine($"Invalid rates overlay: {ex.Message}");
            return 1;
        }
    }
}
