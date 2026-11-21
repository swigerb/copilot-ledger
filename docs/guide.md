# Getting the most from CopilotLedger

Use CopilotLedger to see where AI spend goes, budget a branch, and notice runaway agent loops. This guide covers common tasks. See [Using gh-ledger](cli.md) for the full options and [Configuring CopilotLedger](configuration.md) for pricing. Commands are shown as `gh-ledger <command>`. With the GitHub CLI extension, or after `install` sets the alias, run them as `gh ledger <command>`.

## Enable it

### Install it

Install the GitHub CLI extension and run its `install` command:

```shell
gh extension install swigerb/gh-ledger
gh ledger install
```

To build from source instead, publish a single-file binary for your platform, move it to a permanent folder, and run its `install`:

```shell
dotnet publish src/CopilotLedger.Cli/CopilotLedger.Cli.csproj -c Release \
  -r <rid> -o artifacts/gh-ledger
/path/to/gh-ledger install
```

Replace `<rid>` with `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`.

`install` sets `COPILOT_OTEL_ENABLED=true` for your user, starts the daemon, and records its changes in `install.json`. For a binary you built, it also sets the `gh ledger` alias; the extension doesn't need one. Use `--dry-run` to print the changes first.

It also registers the daemon to start when you sign in:

- Windows uses `CopilotLedger` under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`.
- macOS uses `~/Library/LaunchAgents/io.github.swigerb.copilot-ledger.plist`.
- Linux uses `copilot-ledger.desktop` in `$XDG_CONFIG_HOME/autostart`, or `~/.config/autostart` by default.

Restart the GitHub Copilot app, VS Code, and open terminals after the install.

### Verify it

Send one Copilot prompt, then run:

```shell
gh-ledger status
```

Check the daemon state, rates, Copilot export configuration, warnings, and recent totals. A prompt must finish before its span can appear.

Copilot drops telemetry while the daemon is not running. Start it manually if needed:

```shell
gh-ledger daemon start
```

After `install`, the normal path is to let the sign-in item start it. See [cli.md](cli.md) for install and daemon troubleshooting.

## See where your credits go

Run reports from the repository you want to inspect.

### Spend by branch

```shell
gh-ledger branch
```

The table groups spend by branch. `Spans` counts inference spans, and `Unpriced` counts spans without a cost. `AI credits` and `GitHub USD` show GitHub-billed use. `BYOK USD` shows Foundry-billed use, and `Local saved` estimates cloud cost avoided by local models.

Use `--repo <key>` for another repository, `--all-repos` for every repository, and `--since <period>` to change the period.

### Spend by agent

```shell
gh-ledger agents
```

This report uses the same money columns, grouped by agent name. Copilot's default agent appears as `(none)`. Use it to find an agent that consumes more than expected.

### Expensive prompt chains

```shell
gh-ledger top-prompts --limit 10
```

This report ranks prompt chains by cost. It shows start time, repository, agents, models, span count, spend, savings, and trace ID. Use `--repo <key>` and `--since <period>` to narrow it. The limit range is 1 to 100.

### Use JSON in scripts

Reports support `--json`:

```shell
gh-ledger branch --all-repos --since 7d --json
```

Text tables round money to cents. JSON preserves exact decimal amounts.

## Show spend on pull requests

Give reviewers the AI spend behind a pull request. On the pull request's branch, write its receipt and commit it:

```shell
gh-ledger export-pr
git add .github/copilot-ledger/receipts
git commit -m "Add CopilotLedger receipt"
git push
```

The receipt lists GitHub AI Credits by model, Foundry BYOK spend, local-model tokens with the cloud spend they avoided, circuit-breaker trips, and the most expensive prompt, with the data as JSON. Its totals match `gh-ledger branch --since all`. Run `export-pr` again before you push more work, so the receipt stays current.

Add the CopilotLedger action to the repository once, and it posts the receipt on the pull request and updates that comment on later pushes. See [Posting PR receipts](action.md) for the workflow.

## Stop runaway loops

The daemon checks every prompt against two circuit-breaker rules:

- Credit velocity: more than 150 AI credits, or $1.50, in 5 minutes on one prompt.
- Retry churn: 5 failed tool calls in a row, with no successful command or new commit between them.

The trip stays open until you reset it. The daemon writes an `ALERT` line to `daemon.log` and shows a desktop notification when supported. A trip does not stop the agent.

Inspect open trips:

```shell
gh-ledger breaker status
```

Close trips for the current branch:

```shell
gh-ledger breaker reset
```

Use `--id <id>` for one trip, `--repo <key> --branch <name>` for another branch, or `--all` to close every trip.

Tune the rules with `breaker config`. For example:

```shell
gh-ledger breaker config --velocity-credits 75 --velocity-minutes 5
```

Run it without change options to show the current rules. `--notifications off` keeps log alerts but suppresses desktop notifications.

### Add a flag-file stop

The flag file is opt-in:

```shell
gh-ledger breaker config --flag-file on
```

During an open trip, CopilotLedger writes `.copilot-ledger/CIRCUIT_OPEN` in the local repository root. It contains JSON describing the trips. Resetting the last open trip removes it, and the directory's `.gitignore` keeps it out of commits.

This wrapper is for unattended prompt-mode runs started from the repository root, for example:

```shell
./copilot-guard.sh -p "fix the failing tests"
```

It cannot use an interactive terminal because Copilot runs as a background job. The script checks the flag file relative to the current directory, so start it from the repository root. The wrapper is:

```shell
#!/usr/bin/env bash
copilot "$@" &
pid=$!
while kill -0 "$pid" 2>/dev/null; do
  if test -e .copilot-ledger/CIRCUIT_OPEN; then
    kill "$pid"
    wait "$pid" 2>/dev/null
    exit 1
  fi
  sleep 1
done
wait "$pid"
```

Trips without a known local repository root do not get a flag file.

## Budget a branch

A branch budget is a soft cap in AI credits. It does not block work.
```shell
gh-ledger budget set 50
gh-ledger budget list
gh-ledger budget clear
```
The daemon alerts once when all-time branch spend exceeds the budget. Setting it again re-arms the alert. BYOK USD counts toward the budget at $0.01 per AI credit.

Use `--repo <key> --branch <name>` with `set` or `clear` to target another branch. Add `--json` to `budget list` for scripts.

## Let agents see the ledger with MCP

Register the server with Copilot CLI:

```shell
copilot mcp add ledger -- /path/to/gh-ledger mcp
```

VS Code's Copilot agent harness reads the same Copilot MCP configuration. The GitHub Copilot app uses the same agent runtime. If the app does not list the server, add the same command in its MCP settings. VS Code's Local agent harness uses an `mcp.json` `servers` entry with the executable and `mcp` argument.

The server provides `get_branch_ledger`, `get_top_expensive_prompts`, `get_agent_breakdown`, and `set_branch_budget`.

Ask an agent:
- "How much have I spent on this branch today?"
- "Which agent and prompt have cost the most here?"
- "Is the circuit breaker open? Do not continue if it is."
The read tools need no prompt-mode approval. Allow the budget-writing tool in prompt mode with:

```shell
copilot --allow-tool 'ledger(set_branch_budget)'
```

The warning reaches an agent only when it calls a ledger tool. Add this to a repository's `.github/copilot-instructions.md`:

```text
Before long or repeated work, call get_branch_ledger. If the result starts with WARNING, stop and ask the user before continuing.
```

No MCP tool can reset the breaker.

## Use BYOK and local models

Foundry BYOK requests are attributed but unpriced by default because Foundry bills you directly and CopilotLedger cannot know your agreement's rates.

Create and check an optional overlay:
```shell
gh-ledger rates init
gh-ledger rates validate
```
Add Foundry model and deployment prices to `~/.copilot-ledger/rates.json`. See [configuration.md](configuration.md) for the schema, matching rules, custom hosts, and examples.

Recognized local models cost $0.00 billed. CopilotLedger estimates their cloud cost at the configured GitHub baseline and reports it as local savings, not billed spend.

## Tips
- Use one branch per feature because spend and budgets are per branch.
- Keep the daemon running; spans exported while it is down are not recovered.
- Review `top-prompts` regularly for the prompts driving most spend.

CopilotLedger stores no prompt or response content, working directories, or remote URLs. Repositories are stored as `owner/repo` slugs or local roots, and credentials are stripped from Git remote URLs. Check [cli.md](cli.md) and [configuration.md](configuration.md) for current privacy and storage details.

## Uninstall

Stop the daemon, remove the sign-in item, and undo the install changes:

```shell
gh-ledger uninstall
```
Use `--dry-run` to print the changes first. Your ledger data remains in `~/.copilot-ledger/`.

If you installed the GitHub CLI extension, remove it after `uninstall`, with `gh extension remove ledger`. On Windows, the daemon must be stopped first, because Windows can't replace or delete a running program; `uninstall` stops it.
