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
- Pull request receipts: commit a branch's spend receipt, and a GitHub Action posts it on the pull request
- A circuit breaker for spend velocity and retry churn
- Soft AI-credit budgets for each branch
- An MCP server that lets Copilot agents inspect the ledger
- Attribution for Foundry BYOK and local models
- Local SQLite storage and JSON output for scripts

## Requirements

- GitHub CLI (`gh`) to install CopilotLedger as an extension, or the .NET 10 SDK to build it from source
- Windows (x64 or Arm64), Linux (x64 or Arm64), or macOS (Intel or Apple silicon)
- Projects in Git repositories, for per-branch attribution. CopilotLedger reads
  `.git` directly and doesn't run `git`.
- Copilot CLI, the GitHub Copilot app, or VS Code with Copilot

## How it works

CopilotLedger runs an OpenTelemetry HTTP receiver on your machine. Copilot sends
usage spans to that receiver, and CopilotLedger stores usage and cost in a
local SQLite ledger.

Spend is available through text reports, JSON reports, pull request receipts,
and the MCP server. Prompt and response content are not stored.

## Quick start

Install the GitHub CLI extension, then turn on Copilot's telemetry export and
start the daemon:

```shell
gh extension install swigerb/gh-ledger
gh ledger install
```

`install` also registers the daemon to start when you sign in: a Windows Run
value, a macOS LaunchAgent, or an XDG autostart entry. The extension doesn't
need Git for Windows.

Restart the GitHub Copilot app, VS Code, and your open terminals. Send one
Copilot prompt, then check the receiver and export configuration:

```shell
gh ledger status
```

See branch spend:

```shell
gh ledger branch
```

Run `gh ledger --help` for every command. The daemon runs from the extension's
binary, so stop it before you upgrade:

```shell
gh ledger daemon stop
gh extension upgrade ledger
gh ledger daemon start
```

### Show spend on pull requests

On a pull request's branch, write the branch's receipt and commit it:

```shell
gh ledger export-pr
git add .github/copilot-ledger/receipts
git commit -m "Add CopilotLedger receipt"
git push
```

Then add `.github/workflows/copilot-ledger-receipt.yml` to the repository:

```yaml
name: CopilotLedger receipt

on:
  pull_request:
    types: [opened, synchronize, reopened]

permissions:
  contents: read
  pull-requests: write

concurrency:
  group: copilot-ledger-receipt-${{ github.event.pull_request.number }}
  cancel-in-progress: true

jobs:
  receipt:
    runs-on: ubuntu-latest
    steps:
      - uses: swigerb/gh-ledger/action@v0.4.0
```

The action posts the receipt as one comment and updates it on later pushes.
See [Posting PR receipts](docs/action.md).

### Build from source

Build a single-file, self-contained binary for your platform:

```shell
dotnet publish src/CopilotLedger.Cli/CopilotLedger.Cli.csproj -c Release \
  -r <rid> -o artifacts/gh-ledger
```

Use `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, or
`osx-arm64`. Move the binary to a permanent folder, then run
`/path/to/gh-ledger install`, which records that location and sets a
`gh ledger` alias. On Windows, the alias needs Git for Windows (`sh.exe`);
without it, run `gh-ledger` directly.

## Documentation

- [Getting the most from CopilotLedger](docs/guide.md)
- [CLI command reference](docs/cli.md)
- [Pricing and rates configuration](docs/configuration.md)
- [Posting PR receipts with the CopilotLedger action](docs/action.md)
- [Releasing gh-ledger](docs/releasing.md)

## License

MIT. See [LICENSE](LICENSE).
