# CopilotLedger

CopilotLedger records GitHub AI Credit spend from Copilot CLI, the GitHub
Copilot app, and VS Code through OpenTelemetry. It attributes usage to a Git
repository, branch, model, and agent, including Microsoft Foundry BYOK and
local-model usage.

It runs locally, with no Docker or cloud service. A circuit breaker warns when
one prompt spends too quickly or an agent repeats failing tool calls, helping
you catch runaway loops.

## Features

- Reports for branches, agents, and prompt traces
- A circuit breaker for spend velocity and retry churn
- Soft AI-credit budgets for each branch
- An MCP server that lets Copilot agents inspect the ledger
- Attribution for Foundry BYOK and local models
- Local SQLite storage and JSON output for scripts

## Requirements

- .NET 10 SDK to build from source
- Git for repository attribution
- One of the supported runtime identifiers: `win-x64`, `win-arm64`,
  `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`
- GitHub CLI and Git for Windows if you want to use the `gh ledger` alias on
  Windows

## How it works

CopilotLedger runs an OpenTelemetry HTTP receiver on your machine. Copilot sends
usage spans to that receiver, and CopilotLedger stores usage and cost in a
local SQLite ledger.

Spend is available through text reports, JSON reports, and the MCP server.
Prompt and response content are not stored.

## Quick start

Build a single-file, self-contained binary for your platform:

```shell
dotnet publish src/CopilotLedger.Cli/CopilotLedger.Cli.csproj -c Release \
  -r <rid> -o artifacts/gh-ledger
```

Use one of the runtime identifiers listed above. Move the binary to a permanent
folder. `install` records that location.

Install Copilot export, the `gh ledger` alias, and the daemon:

```shell
/path/to/gh-ledger install
```

`install` also registers the daemon to start when you sign in: a Windows Run
value, a macOS LaunchAgent, or an XDG autostart entry.

Restart the GitHub Copilot app, VS Code, and your open terminals. Send one
Copilot prompt, then check the receiver and export configuration:

```shell
gh-ledger status
```

The `gh ledger` form also works after `install`.
You can use the `gh-ledger` binary directly if you do not use the alias.
Run `gh-ledger --help` for the current command list.

See branch spend:

```shell
gh-ledger branch
```

## Documentation

- [Getting the most from CopilotLedger](docs/guide.md)
- [CLI command reference](docs/cli.md)
- [Pricing and rates configuration](docs/configuration.md)

## License

MIT. See [LICENSE](LICENSE).
