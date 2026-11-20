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

Run `gh-ledger install`. It sets `COPILOT_OTEL_ENABLED=true` in the Windows user environment or in a marked block in your shell profile, sets the `gh ledger` alias, starts the daemon, and records the changes in `install.json`. It changes nothing else: in particular, it does not edit `PATH` or other OpenTelemetry variables.

Use `gh-ledger install --dry-run` to print the changes without making them. Restart the GitHub Copilot app, VS Code, and open terminals afterward.

`install` warns about endpoint overrides, the file exporter, a non-`otlp-http` exporter type, TLS variables that turn off export to HTTP endpoints, and gRPC protocol settings. These warnings describe why Copilot might send telemetry somewhere other than CopilotLedger.

On Windows, `gh ledger` needs `sh.exe` from Git for Windows. The `gh-ledger` executable works without it.

## The daemon

`gh-ledger daemon run` runs the receiver in the foreground. `gh-ledger daemon start` starts it as a detached process, and `gh-ledger daemon stop` stops it. The daemon uses `daemon.json` for state, `daemon.lock` for single-instance locking, and `daemon.log` for detached-process logs.

The daemon does not start at login yet. Run `gh-ledger daemon start` after you sign in. Copilot drops telemetry while the daemon is not running.

## Reports

Use `gh-ledger status [--json]` for daemon state, rates, Copilot export configuration, warnings, and today/7-day/30-day totals.

Use `gh-ledger branch [--repo <key> | --all-repos] [--since <period>] [--json]` for repository branch spend. Without a repository option, it uses the current Git repository; it falls back to all repositories outside a repository.

Use `gh-ledger agents [--repo <key>] [--since <period>] [--json]` for spend by agent, and `gh-ledger top-prompts [--repo <key>] [--since <period>] [--limit <1-100>] [--json]` for the highest-cost prompt traces. `--limit` defaults to 10.

The repository key can be an `owner/repo` slug, a repository root, or `(none)`. Periods accept `today`, `all`, `1d` through `3650d`, or `yyyy-MM-dd`; the default is `30d`.

Tables round money to cents. JSON preserves exact decimal amounts.

`(none)` under Agent means Copilot reported no agent name. Copilot's default agent reports none, while built-in subagents such as `explore` report their own names. A custom agent reports the `name` in its `.agent.md` file both when started with `copilot --agent` and when called as a subagent. A custom agent without a `name` reports its file name without `.agent.md`.

## Rates

Run `gh-ledger rates init` to create a starter overlay and `gh-ledger rates validate` to validate it. See [configuration.md](configuration.md) for the rates format and pricing channels.

## Uninstall

`gh-ledger uninstall` reverses exactly the environment variable, profile block, `gh` alias, and daemon changes recorded by `install`, restoring the previous environment value when applicable. `gh-ledger uninstall --dry-run` prints the changes without applying them. Ledger data is kept.

## Privacy

CopilotLedger stores no prompt or response content, working directories, or remote URLs. Repositories are stored as `owner/repo` slugs or local roots, and credentials are stripped.

## Troubleshooting

- **Port 4318 in use.** Stop the other listener or the old daemon before starting CopilotLedger.
- **Nothing recorded.** Run `gh-ledger status`, address its warnings, and restart Copilot.
- **Desktop-launched apps do not use the profile.** Apps started outside a terminal, such as from the macOS Dock or a Linux desktop launcher, may not read shell profiles. If no data is recorded after using such an app, start it from a terminal.
- **Move the data directory.** Set `COPILOT_LEDGER_HOME` before running `gh-ledger`.
