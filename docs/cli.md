# Using gh-ledger

## What gh-ledger does

CopilotLedger runs a local OTLP receiver at `http://localhost:4318`. It records spend from Copilot CLI, the GitHub Copilot app, and VS Code in `~/.copilot-ledger/ledger.db`. It needs no Docker and no cloud service.

## Build the binary

Publish one single-file, self-contained binary for your runtime identifier:

```shell
dotnet publish src/CopilotLedger.Cli/CopilotLedger.Cli.csproj -c Release -r <rid> -o artifacts/gh-ledger
```

Use `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`. Keep the binary in a permanent folder because `install` points the `gh ledger` alias at that binary.

## Install

Run `gh-ledger install`. It sets `COPILOT_OTEL_ENABLED=true` in the Windows user environment or in a marked block in your shell profile, sets the `gh ledger` alias, registers the daemon to start when you sign in, starts the daemon, and records the changes in `install.json`. It changes nothing else: in particular, it does not edit `PATH` or other OpenTelemetry variables.

Use `gh-ledger install --dry-run` to print the changes without making them. Restart the GitHub Copilot app, VS Code, and open terminals afterward.

The sign-in item is the `CopilotLedger` value under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` on Windows, `~/Library/LaunchAgents/io.github.swigerb.copilot-ledger.plist` on macOS, and `copilot-ledger.desktop` in `$XDG_CONFIG_HOME/autostart` (default `~/.config/autostart`) on Linux.

`install` and `gh-ledger status` show these warnings:

- `OTEL_EXPORTER_OTLP_ENDPOINT` set to an endpoint elsewhere means Copilot exports there instead of to CopilotLedger.
- `COPILOT_OTEL_FILE_EXPORTER_PATH` means Copilot writes telemetry to that file instead of exporting it to CopilotLedger.
- `COPILOT_OTEL_EXPORTER_TYPE` set to anything other than `otlp-http` means it should be removed or set to `otlp-http`.
- Any of `OTEL_EXPORTER_OTLP_CERTIFICATE`, `OTEL_EXPORTER_OTLP_CLIENT_CERTIFICATE`, `OTEL_EXPORTER_OTLP_CLIENT_KEY`, `OTEL_EXPORTER_OTLP_TRACES_CERTIFICATE`, `OTEL_EXPORTER_OTLP_TRACES_CLIENT_CERTIFICATE`, or `OTEL_EXPORTER_OTLP_TRACES_CLIENT_KEY` means Copilot turns off export to `http://` endpoints.
- `OTEL_EXPORTER_OTLP_PROTOCOL` or `OTEL_EXPORTER_OTLP_TRACES_PROTOCOL` set to `grpc` is unsupported because CopilotLedger accepts only `http/protobuf` and `http/json`.

On Windows, `gh ledger` needs `sh.exe` from Git for Windows. The `gh-ledger` executable works without it.

## The daemon

`gh-ledger daemon run` runs the receiver in the foreground. `gh-ledger daemon start` starts it as a detached process, and `gh-ledger daemon stop` stops it. `daemon start` gives the daemon the null device as its standard input, output, and error, so a script can capture its output, for example `started=$(gh-ledger daemon start)`, and go on while the daemon runs. The daemon uses `daemon.json` for state, `daemon.lock` for single-instance locking, and `daemon.log` for detached-process logs.

After `install`, the daemon starts when you sign in. Copilot drops telemetry while the daemon is not running.

## Reports

Use `gh-ledger status [--json]` for daemon state, rates, Copilot export configuration, warnings, and today/7-day/30-day totals.

Use `gh-ledger branch [--repo <key> | --all-repos] [--since <period>] [--json]` for repository branch spend. Without a repository option, it uses the current Git repository; it falls back to all repositories outside a repository.

Use `gh-ledger agents [--repo <key>] [--since <period>] [--json]` for spend by agent, and `gh-ledger top-prompts [--repo <key>] [--since <period>] [--limit <1-100>] [--json]` for the highest-cost prompt traces. `--limit` defaults to 10.

The repository key can be an `owner/repo` slug, a repository root, or `(none)`. Periods accept `today`, `all`, `1d` through `3650d`, or `yyyy-MM-dd`; the default is `30d`.

Tables round money to cents. JSON preserves exact decimal amounts.

`(none)` under Agent means Copilot reported no agent name. Copilot's default agent reports none, while built-in subagents such as `explore` report their own names. A custom agent reports the `name` in its `.agent.md` file both when started with `copilot --agent` and when called as a subagent. A custom agent without a `name` reports its file name without `.agent.md`.

## Circuit breaker

The daemon watches every prompt for two kinds of runaway loop and trips a circuit breaker when it sees one:

- **Credit velocity.** One prompt's billed spend (GitHub AI credits plus BYOK) exceeds 150 AI credits ($1.50) within 5 minutes.
- **Retry churn.** One prompt makes 5 failed tool calls in a row, with no successful command and no new commit between them. A failed call is a shell command that exits with a non-zero code, or a tool call that Copilot reports as an error.

Copilot exports neither the command a shell call ran nor the file an edit changed, so retry churn counts any failing command, and any successful command resets the count. Edits that never run a failing command don't count. VS Code's Local agent harness may not report exit codes, so retry churn may never trip there.

When the breaker trips, the daemon writes an `ALERT` line to `daemon.log` and shows a desktop notification: a toast on Windows, a notification on macOS, or `notify-send` on Linux when it's installed. The trip stays open until you reset it. Each prompt trips each rule at most once.

- `gh-ledger breaker status [--json]` shows the rules and the open trips.
- `gh-ledger breaker reset` closes the open trips on the current branch. Use `--id <id>` for one trip, `--repo <key> --branch <name>` for another branch, or `--all`.
- `gh-ledger breaker config` shows the rules. Change them with `--velocity-credits <credits>` (1 to 1000000), `--velocity-minutes <minutes>` (1 to 1440), and `--churn-failures <count>` (2 to 100). `--notifications off` keeps the log line but stops desktop notifications.
- `gh-ledger breaker config --flag-file on` also writes `.copilot-ledger/CIRCUIT_OPEN` in the repository root while a trip is open, so a wrapper script can stop an agent. The file holds JSON describing the open trips. A `.copilot-ledger/.gitignore` keeps it out of commits, and `breaker reset` removes it. Trips without a local repository root get no file. If the daemon logs that it couldn't update the file, run `gh-ledger breaker config --flag-file on` again to rewrite it.

## Budgets

A budget is a soft cap in AI credits for one branch. When the branch's billed spend exceeds it, the daemon alerts once; nothing is blocked. BYOK spend counts at $0.01 per AI credit.

- `gh-ledger budget set <credits>` sets the current branch's budget. Setting it again re-arms the alert.
- `gh-ledger budget clear` removes it, and `gh-ledger budget list [--json]` lists every budget with its spend.
- `budget set` and `budget clear` accept `--repo <key> --branch <name>` for another branch.

## Pull request receipts

`gh-ledger export-pr` writes the current branch's receipt for its pull request to `.github/copilot-ledger/receipts/<branch>-<hash>.md` in the repository root, where `<branch>` is the branch name with characters other than letters, digits, `.`, `_`, and `-` replaced by `-`. Commit that file on the branch and push it. The CopilotLedger action then posts it on the pull request; see [Posting PR receipts with the CopilotLedger action](action.md).

The receipt covers all spend recorded on the branch. It lists GitHub AI Credits by model, Foundry BYOK spend, local-model tokens and the cloud spend they avoided, requests that couldn't be priced, the number of circuit-breaker trips, and the most expensive prompt in AI credits. A collapsible section holds a table by billing channel and model, and the receipt's data as JSON. Its totals equal the branch's row in `gh-ledger branch --since all --json`.

- `--stdout` writes the Markdown to standard output instead of the file.
- `--json` writes the JSON data to standard output instead of the file.

A receipt can be at most 65,536 bytes, the most the CopilotLedger action posts; `export-pr` refuses a larger one, with or without `--stdout`. `export-pr` needs a branch with at least one commit and a GitHub remote named `origin`. A turn is one model request. Branch and model names appear in code spans, so they never become mentions or links.

## MCP server

`gh-ledger mcp` runs an MCP server on standard input and output, so Copilot can read the ledger and see the circuit breaker. Register it once with Copilot CLI:

```shell
copilot mcp add ledger -- /path/to/gh-ledger mcp
```

That adds `ledger` to `~/.copilot/mcp-config.json`. VS Code's Copilot agent harness reads the same file. The GitHub Copilot app runs the same agent runtime; if it doesn't list the server, add it in the app's MCP settings with the same command. For VS Code's Local agent harness, add the server to `mcp.json` under `servers`, with `"command": "/path/to/gh-ledger"` and `"args": ["mcp"]`.

The server offers four tools. Each uses the current repository and branch unless you pass `repository` and `branch`:

- `get_branch_ledger`: the branch's spend, top model, budget, and circuit-breaker status.
- `get_top_expensive_prompts`: the branch's most expensive prompt chains, with output tokens and failed tool calls; `limit` (1 to 20, default 5) and `orderBy` (`cost`, `outputTokens`, or `failedToolCalls`).
- `get_agent_breakdown`: the branch's spend by agent.
- `set_branch_budget`: sets the branch's budget in AI credits (`aiCredits`).

The first three are read-only. `set_branch_budget` changes only the budget, so Copilot asks before running it; in prompt mode, allow it with `--allow-tool 'ledger(set_branch_budget)'`. No tool can reset the circuit breaker. While the breaker is open for the branch or the session, or the branch is over its budget, every result starts with a `WARNING:` line.

## Rates

Run `gh-ledger rates init` to create a starter overlay and `gh-ledger rates validate` to validate it. See [configuration.md](configuration.md) for the rates format and pricing channels.

## Uninstall

`gh-ledger uninstall` stops the daemon if it is running, removes the sign-in item, and reverses the environment variable, profile block, and `gh` alias changes recorded by `install`, restoring the previous environment value when applicable. `gh-ledger uninstall --dry-run` prints the changes without applying them. Ledger data is kept.

## Privacy

CopilotLedger stores no prompt or response content, working directories, or remote URLs. Repositories are stored as `owner/repo` slugs or local roots, and credentials are stripped.

## Troubleshooting

- **Port 4318 in use.** Stop the other listener or the old daemon before starting CopilotLedger.
- **Nothing recorded.** Run `gh-ledger status`, address its warnings, and restart Copilot.
- **Desktop-launched apps may not read the profile.** Apps started outside a terminal, such as from the macOS Dock or a Linux desktop launcher, may not read shell profiles, so they may not see the `COPILOT_OTEL_ENABLED` that `install` put there. If no data is recorded after using such an app, start it from a terminal.
- **Move the data directory.** Set `COPILOT_LEDGER_HOME` before running `gh-ledger`.
- **A console window flashes at sign-in.** Windows 11 24H2 and later start `gh-ledger` without a window. Earlier versions of Windows show its window for about a second.
- **The daemon started at sign-in uses the default data directory.** If you set `COPILOT_LEDGER_HOME`, set it where your sign-in session sees it, such as a Windows user environment variable.
