# Sprint 3: Runaway-Loop Circuit Breaker & MCP Server

| | |
|---|---|
| Architect | `ledger-architect` (Claude Opus 5.5) |
| Builder | `ledger-builder` (MAI Code 2) |
| Target | .NET 10 (`net10.0`), SDK 10.0.x, zero Docker |
| Plan written | 2026-11-21, against merged Sprint 2 (`main` at `40843c9`) |
| Status | Current sprint. Sprint 2 is complete and archived at `.github/plans/archive/sprint-2.md`. |

## How to use this plan

`ledger-builder` implements one task per run: the first unchecked `Task N:` line below. It follows that task's specification section, runs the verification commands, changes the line to `- [x]`, and stops. Tasks are sequential, and each builds on the ones before it.

Every task ships with its tests, listed under "Test specifications". A task is done only when `dotnet test` runs the whole suite, including its new cases, with 0 failed and 0 skipped.

Each test case lists every assertion it makes. Write all of them, in the order given; don't drop one because it looks redundant. Where a case says "exactly", compare whole values, not substrings.

## Tasks

- [x] Task 1: Tool-Call Signals & Breaker Storage (CopilotLedger.Otlp, Storage & Core). Decode OTLP span status in both encodings, keep `gen_ai.tool.name` and `process.exit.code` on `execute_tool` links, and add Migration 3: tool columns on `span_links` plus the `breaker_trips`, `branch_budgets`, and `breaker_settings` tables. Add the Core breaker contracts and the store methods that the breaker, the CLI, and the MCP server use. Tests decode status, normalize Copilot's real tool spans, and exercise every new store method.
- [x] Task 2: Circuit Breaker Engine & Flag File (CopilotLedger.Core & Otlp). Add the two rules, credit velocity and retry churn, as pure functions, and `CircuitBreakerMonitor`, which runs inside the ingestor's gate after back-fill, records each trip once, raises budget alerts, and keeps the opt-in `.copilot-ledger/CIRCUIT_OPEN` flag file in step with the ledger. Tests replay live run A, a five-failure retry loop, batch by batch, plus velocity, commit-reset, budget, and flag-file cases.
- [ ] Task 3: Breaker Alerts and the Breaker & Budget Commands (CopilotLedger.Cli). The daemon logs each alert and shows a desktop notification: a Windows toast, a macOS notification, or `notify-send`. It re-sends alerts it missed while stopped. Add `breaker status|reset|config` and `budget set|clear|list`. Tests drive an in-process daemon over TestServer with the fake platform.
- [ ] Task 4: MCP Server (CopilotLedger.Mcp & Cli). Create `src/CopilotLedger.Mcp` on ModelContextProtocol 2.4.0 with `get_branch_ledger`, `get_top_expensive_prompts`, `get_agent_breakdown`, and `set_branch_budget`, closed input schemas, and breaker and budget warnings in every result; add `gh-ledger mcp`. Tests connect the SDK's client to the server over in-memory pipes.
- [ ] Task 5: Start at Sign-in, Version 0.3.0 & CI (CopilotLedger.Cli). `install` registers a per-user sign-in item (a Windows Run value, a macOS LaunchAgent, or an XDG autostart entry), and `uninstall` removes it. An application manifest stops Windows from opening a console window at sign-in. Bump the version to 0.3.0, extend the CI smoke test with the breaker, budget, and MCP commands, and finish `docs/cli.md`.

## Starting point

- Sprint 2 is complete. Tasks 1–5 merged in PRs #13, #14, #15, #18, and #19, with fix #17 (`main` at `40843c9`). `dotnet test CopilotLedger.slnx` passes 440 tests (0 failed, 0 skipped), and CI is green on `ubuntu-latest` and `windows-latest`, including the published single-file smoke test. Sprint 2's plan, with its amendments, is archived at `.github/plans/archive/sprint-2.md`.
- Merged Sprint 2 behavior that this plan depends on (read from the code at `40843c9`):
  - `GenAiSpanNormalizer` emits a `GenAiSpanLink` for every valid span that has `gen_ai.operation.name` or comes from a Copilot resource (`service.name` `github-copilot` or `copilot-chat`). So Copilot's `execute_tool` spans already reach `span_links`, with their conversation ID. Its private `Text()` helper ignores attributes that aren't non-blank strings.
  - `OtlpProtobufTraceReader.ReadSpan` reads `Span` fields 1, 2, 4, 5, 7, 8, and 9 and skips the rest. `OtlpJsonTraceReader` ignores unknown properties and finds properties with its non-throwing `TryFindProperty` helper (last match wins). The JSON decoding tests have a `SingleSpan(json)` helper that returns the one `OtlpSpan`, and the protobuf tests have `DecodeSingleSpan(OtlpProtoWriter)`, which returns the whole `OtlpTraceRequest`.
  - `LedgerIngestor.IngestAsync` normalizes outside its `SemaphoreSlim(1, 1)` gate. Inside the gate it records billable entries, records links, and back-fills every trace ID in the batch. Store exceptions propagate, and the receiver answers `503` with `Retry-After: 5`, so the exporter retries; inserts are idempotent.
  - `span_links` stores `start_time_unix_ms` only (no end time). `spans` stores start and end in milliseconds.
  - `PromptChainRow`'s keys are `MAX(COALESCE(repo_slug, repo_root, '(none)'))` and `MAX(COALESCE(branch_name, '(none)'))` over the trace's `spans` rows, the same keys as the Branch rollup.
  - `SqliteLedgerStore` opens a new pooled connection per call (WAL, `busy_timeout = 5000`). Every public method except `InitializeAsync` throws `InvalidOperationException` until `InitializeAsync` has run. It stamps `recorded_at_unix_ms` from its `TimeProvider`. `InitializeAsync` reads `PRAGMA user_version` once, before its loop, and runs each pending migration in its own transaction; `BeginTransactionAsync` starts `BEGIN IMMEDIATE`.
  - `DaemonHost.RunAsync` logs `{UtcNow:O} {message}` lines to `--log-file` or to `context.Out`, polls for `daemon.stop` every `DaemonPollInterval`, and prunes `span_links` older than 7 days every 6 hours.
  - `ReportCommands` writes JSON with `JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }`, money with `LedgerFormat.WriteMoney` (format `0.############`), and times with `LedgerFormat.WriteTime` (`ToUniversalTime().ToString("O")`, for example `2026-11-21T00:17:17.2600000+00:00`). `LedgerFormat.Credits` rounds to 2 places with `MidpointRounding.AwayFromZero` and formats `N2` (`1,234.50`). `LedgerFormat.LocalTime(value, zone)` formats `yyyy-MM-dd HH:mm` in `zone`, and the reports pass `context.TimeProvider.LocalTimeZone`.
  - `CliHarness` runs `GhLedgerCli.InvokeAsync` in-process with `FakeCliPlatform`, `UseTestServer()`, `DataDirectory = <root>/data`, `CopilotHome = <root>/copilot-home`, and `WorkingDirectory = <root>`. Its `StartInProcessDaemonAsync` waits for `daemon.json`. `FakeCliPlatform.RunAsync` records every call in `ProcessRuns` and returns exit code 0 unless `RunHandler` is set.
  - Every `InstallManifest` property is `required`. `install` prints its warnings, the variable or profile line, the alias lines, the `sh.exe` warning, the daemon line, and then the final line.
  - `GhLedgerCli.Version` is the informational version, `0.2.0`. `DaemonState.Version` and the first line of `status` show it.
- Sprint 2's public contracts are fixed. Sprint 3 adds types and members, and it changes only these Sprint 2 artifacts, each called out in its task:
  - Task 1: `OtlpSpan` (`StatusCode`), `GenAiSpanLink` (three properties), `GenAiAttributeNames` (two constants), both trace readers, the normalizer's link construction, `LedgerMigrations` (Migration 3), `SqliteLedgerStore.InitializeAsync` (its migration loop) and `RecordSpanLinksAsync` (three more columns), and the tests SL1 and S1, whose expected `PRAGMA user_version` changes from 2 to 3;
  - Task 2: `LedgerIngestor` (a second constructor; the existing one is unchanged);
  - Task 3: `DaemonHost`, `GhLedgerCli` (registration), `FakeCliPlatform.RunAsync` (a lock), `CliHarness` (a TestServer capture, `CreateDaemonClient`, and `DaemonOutput`), and `docs/cli.md`;
  - Task 4: `CliContext` (two optional streams), `GhLedgerCli`, `CopilotLedger.slnx`, the Cli and test project files, and `docs/cli.md`;
  - Task 5: `ICliPlatform` and `SystemCliPlatform` (two members), `FakeCliPlatform`, `InstallManifest` (one optional property), `InstallCommands`, the Cli project file, `GhLedgerCli` (version fallback), `.github/workflows/ci.yml`, `docs/cli.md`, and the tests IN1, IN2, IN3, IN9, IN10, DM1, and the `status` text test in `ReportCommandTests`.
- The test project uses xUnit v2 (`xunit` 2.9.3), and tests use only public APIs, with no `InternalsVisibleTo`.
- When an acceptance criterion says `src/` contains no `double` or `float`, it means the C# keywords and the `GetDouble` and `TryGetDouble` calls. The enum member `OtlpValueKind.Double` is allowed.

## Verified facts (retrieved 2026-11-21)

### Live capture: Copilot tool calls

On 2026-11-21 (UTC) the architect ran the scratch loopback receiver from Sprint 2 and Copilot CLI 1.0.112 on Windows 11 ARM64 (build 28000) with `mai-code-2-flash`, in a scratch Git repository outside this one. Only `COPILOT_OTEL_ENABLED=true` was set, and only for those processes; no user-level Copilot configuration was changed. The scratch repository held `calc.txt` and `test.ps1`, a script that exits 1 on its first five runs and 0 afterward. The four runs below cost 0.644379 AI credits: `--usage-output-file` totals of 372,181,000, 118,518,000, 139,029,000, and 14,651,000 nano-AIU.

1. **Run A, a retry loop.** The prompt asked the agent to edit `calc.txt` until `test.ps1` passed, then commit. One trace (`89f2977b97c7d0a8d05fc3580e3e167e`, conversation `982f73ad-18f4-49ef-a1f9-70748fa013fb`) held one root `invoke_agent`, 14 `chat` spans, and 13 `execute_tool` spans, all children of the root. They arrived in 7 export requests (spans shown in time order):

   | Request | Spans |
   |---|---|
   | 1 | chat 1, `apply_patch` |
   | 2 | chat 2, `powershell` (exit 1), chat 3, `apply_patch` |
   | 3 | chat 4, `powershell` (exit 1), chat 5, `apply_patch` |
   | 4 | chat 6, `powershell` (exit 1), chat 7, `apply_patch` |
   | 5 | chat 8, `powershell` (exit 1), chat 9, `apply_patch` |
   | 6 | chat 10, `powershell` (exit 1), chat 11, `apply_patch` |
   | 7 | chat 12, `powershell` (exit 0, the test passed), chat 13, `powershell` (exit 0, `git commit`), chat 14, the root |

   - A shell call's span is named `execute_tool powershell pwsh.exe`. Its attributes are `gen_ai.operation.name` = `execute_tool`, `gen_ai.conversation.id`, `gen_ai.tool.name` = `powershell`, `gen_ai.tool.call.id`, `gen_ai.tool.type` = `function`, `gen_ai.provider.name` = `github`, `process.executable.name` = `pwsh.exe` (the shell, not the command it ran), and `process.exit.code`, an `intValue` (JSON string `"1"`).
   - The span `status` stayed `{"message": "", "code": 0}` (unset) even when the command exited 1.
   - The edit tool's span is `execute_tool apply_patch`, with `gen_ai.tool.name` = `apply_patch` and no file path. With content capture off, no tool arguments or results are exported.
   - Each `chat` span's `github.copilot.nano_aiu` equaled Sprint 1's formula at `mai-code-2-flash`'s promotional price. The 14 sum to 372,181,000 nano-AIU, the usage file's total.
2. **Run B, an MCP server without approval.** A scratch stdio MCP server, `ledgerprobe`, was configured for that run only, through a scratch MCP configuration file named on the command line. It offered `probe_read` (`readOnlyHint: true`) and `probe_write` (`readOnlyHint: false`). In prompt mode (`-p`), `probe_read` ran without approval and `probe_write` was denied.
   - An MCP tool's `gen_ai.tool.name` is `<SHA-256 of the server name>/<SHA-256 of the tool name>`, both lowercase hex of the UTF-8 name: `ledgerprobe` gives `c3d1fe831856c77818bb8c1242d32d6efa6c37366eb902e56c5fd0054c1dcbc3`, and `probe_write` gives `918a44e755cc5eee09b08d214831e8339a645158ef1f831a0fa02bd43999700f`.
   - The denied call's span had status `{"code": 2, "message": "Tool failed"}`, `error.type` = `denied`, and the event `gen_ai.client.operation.exception`. A child `permission` span (no `gen_ai.operation.name`) carried `github.copilot.permission.result` = `denied-no-approval-rule-and-could-not-request-from-user`.
3. **Run C, an MCP tool error.** With `--allow-tool 'ledgerprobe(probe_write)'`, the tool ran and returned `isError: true`. Its span had status code 2 and `error.type` = `tool_error`. Copilot showed the result's text to the model.
4. **The MCP client.** Copilot CLI spoke protocol 2026-07-28 (`server/discover`, then `tools/list` and `tools/call`) with `clientInfo` `copilot-cli` `1.0.112`. `tools/call` carried `_meta` `com.github.copilot/toolCallId`, equal to the span's `gen_ai.tool.call.id`. The server process ran in the session's working directory, and its environment held `COPILOT_AGENT_SESSION_ID` (the conversation ID) and `COPILOT_SESSION_CWD`.
5. **Run D, a `sessionStart` hook.** A repository hook (`.github/hooks/probe.json`, `type: command`, `exec` and `args`) ran with the repository root as its working directory, received `{"sessionId": …, "timestamp": …, "cwd": …, "source": "new", "initialPrompt": …}` on stdin, and had `COPILOT_PROJECT_DIR` and `COPILOT_HOOK_DIR` set. It started a detached child the same way `SystemCliPlatform.StartDetached` does (`UseShellExecute = false`, `CreateNoWindow = true`). The child wrote a heartbeat every second. It stopped about 4 seconds before Copilot CLI exited at the end of the prompt, and its process was gone. **A hook can't start a long-lived daemon on Windows.**

### Copilot MCP configuration and hooks

Sources:

- GitHub Docs, [Adding MCP servers for GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers);
- VS Code, [MCP configuration reference](https://code.visualstudio.com/docs/agents/reference/mcp-configuration);
- GitHub Docs, [Using hooks with GitHub Copilot CLI](https://docs.github.com/copilot/how-tos/copilot-cli/use-hooks) and the [hooks reference](https://docs.github.com/copilot/reference/hooks-configuration).

Facts:

- `copilot mcp add SERVER-NAME -- COMMAND [ARGS...]` adds a local (stdio) server to the user configuration, `~/.copilot/mcp-config.json`, under a top-level `mcpServers` object. The file can also be edited by hand. Project files `.mcp.json` and `.github/mcp.json` take precedence over it.
- VS Code's Copilot harness reads the same user file, `$COPILOT_HOME/mcp-config.json` (default `~/.copilot/mcp-config.json`), labeled "Copilot Global". VS Code's own `mcp.json` uses a top-level `servers` object instead.
- Copilot CLI permission rules name an MCP tool as `SERVER(TOOL)`, for example `--allow-tool 'ledger(set_branch_budget)'` (confirmed by run C).
- User hooks live in `~/.copilot/hooks/*.json` (`$COPILOT_HOME/hooks`). `sessionStart` hooks run when a session starts and are bounded by `timeoutSec`. Hooks can be disabled with `disableAllHooks` or restricted by enterprise managed settings.

### MCP protocol and the C# SDK

Sources: [MCP specification 2026-07-28, key changes](https://modelcontextprotocol.io/specification/2026-07-28/changelog); [Tools](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/docs/specification/2025-11-25/server/tools.mdx) (2025-11-25, unchanged in 2026-07-28 for the points used here); NuGet `ModelContextProtocol` 2.4.0, checked by reflection and an in-memory server-and-client probe.

- `ModelContextProtocol` 2.4.0 is on the configured feed. It depends on `ModelContextProtocol.Core` [2.4.0], `Microsoft.Extensions.Caching.Abstractions` 10.0.14, and `Microsoft.Extensions.Hosting.Abstractions` 10.0.14; Core depends on `Microsoft.Extensions.AI.Abstractions` 10.11.0 and `Microsoft.Extensions.Logging.Abstractions` 10.0.14. A `net10.0` project with `TreatWarningsAsErrors` restores it with no `NU190x` warning.
- The low-level server API (namespace `ModelContextProtocol.Server`, with protocol types in `ModelContextProtocol.Protocol`):
  - `McpServer.Create(ITransport transport, McpServerOptions options, ILoggerFactory? loggerFactory = null, IServiceProvider? serviceProvider = null)`, then `RunAsync(CancellationToken)`. `McpServer` is `IAsyncDisposable`.
  - `McpServerOptions { ServerInfo = new Implementation { Name, Version }, ServerInstructions, Handlers = new McpServerHandlers { ListToolsHandler, CallToolHandler } }`. Setting `ListToolsHandler` advertises the `tools` capability.
  - Handlers are `(RequestContext<TParams> request, CancellationToken cancellationToken) => ValueTask<TResult>`. `request.Params` is `ListToolsRequestParams?` or `CallToolRequestParams?` (`Name`, and `Arguments` as `IDictionary<string, JsonElement>?`).
  - `Tool { Name, Title, Description, InputSchema (JsonElement), Annotations = new ToolAnnotations { ReadOnlyHint, DestructiveHint, IdempotentHint, OpenWorldHint } }`, `ListToolsResult { Tools }`, `CallToolResult { Content, StructuredContent (JsonElement?), IsError (bool?) }`, and `TextContentBlock { Text }`.
  - `StreamServerTransport(Stream inputStream, Stream outputStream, string? serverName = null, ILoggerFactory? loggerFactory = null)`.
  - The client (namespace `ModelContextProtocol.Client`): `McpClient.CreateAsync(new StreamClientTransport(Stream serverInput, Stream serverOutput), McpClientOptions?)`, `ListToolsAsync()` (items expose `ProtocolTool`), and `CallToolAsync(string name, IReadOnlyDictionary<string, object?>? arguments)`. `McpClient.ServerInfo` is `Implementation?`, `ServerInstructions` is `string?`, and `Tool.Annotations` is `ToolAnnotations?`, so tests check them with `Assert.NotNull` before reading their members (see .NET below).
- Behavior observed in the probe:
  - The SDK doesn't validate arguments against `inputSchema`; extra arguments reach the handler.
  - A handler that throws any other exception produces `isError: true` with the text `An error occurred invoking '<name>'.`; the exception's message isn't sent.
  - `throw new McpProtocolException("Unknown tool: x", McpErrorCode.InvalidParams)` produces JSON-RPC error `-32602` with that message. The client throws `McpProtocolException` whose `ErrorCode` is `McpErrorCode.InvalidParams` and whose `Message` is `Request failed (remote): Unknown tool: x`.
  - Escaped lone surrogates never reach the handler. The orchestrator's probe found that the SDK client can't send one: `CallToolAsync` throws `JsonException` on the client side. Its raw-call result was reproduced on 2026-11-21: a stateless `tools/call` (with `_meta` protocol version `2026-07-28`) whose argument name or value contains an escaped lone surrogate, such as `"\ud800"`, short or long, gets JSON-RPC error `-32603` `An error occurred.` without calling the handler. The same call with ordinary arguments, or with a valid surrogate pair such as `"\ud83d\ude00"`, reaches the handler.
  - The SDK's serializer escapes non-ASCII characters, `+`, `<`, and `'` on the wire, and clients unescape them, so `TextContentBlock.Text` arrives unchanged. `StructuredContent.GetRawText()` on the client keeps the escapes, so tests compare structured content as parsed JSON, not raw text.
  - The server negotiates 2026-07-28 (`server/discover`; results carry `resultType`, and list results carry `ttlMs` and `cacheScope`) and answers a legacy `initialize` with 2025-06-18. A `tools/list` request whose `_meta` declares `io.modelcontextprotocol/protocolVersion` `2026-07-28` and `io.modelcontextprotocol/clientCapabilities` `{}` is answered directly. When standard input closes, `RunAsync` returns.
- The spec: input validation errors are tool execution errors (`isError: true`), so the model can correct itself, and an unknown tool is protocol error `-32602`. Annotations are hints, and clients treat them as untrusted. 2026-07-28 deprecates Roots (SEP-2577) and suggests passing directories as tool parameters instead. The SDK reports any use of `ClientCapabilities.Roots` as diagnostic `MCP9005`.

### Desktop notifications

- **Windows** (verified 2026-11-21, without administrator rights, from a `net10.0` program): `powershell.exe -NoProfile -NonInteractive -Command <script>`. The script loads `Windows.UI.Notifications.ToastNotificationManager` and `Windows.Data.Xml.Dom.XmlDocument` as WinRT types, reads toast XML from a UTF-8 file, and shows it with Windows PowerShell's application ID, `{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe`. It exited 0 in about 1.5–1.9 seconds, and the toast appeared in the notification history with its tag and group; the check removed it afterward. The exact script is in Task 3.
- PowerShell treats five characters as single quotes: `'` (U+0027), `‘` (U+2018), `’` (U+2019), `‚` (U+201A), and `‛` (U+201B). Verified 2026-11-21 with the toast XML in a directory named `ledger it's O’Brien ‘a‚b‛`: doubling only `'` in the script's path gives a parse error, and doubling all five shows the toast.
- `-EncodedCommand`, `-WindowStyle Hidden`, and `-ExecutionPolicy Bypass` are deliberately not used, because endpoint-protection products flag that combination.
- **macOS** and **Linux** use `/usr/bin/osascript` with `display notification` and `notify-send`. Neither was run live (see Assumptions).

### Start at sign-in

Sources: Microsoft Learn, [Run and RunOnce registry keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys) and [Console allocation policy](https://learn.microsoft.com/en-us/windows/console/console-allocation-policy); Apple, [Creating Launch Daemons and Agents](https://developer.apple.com/library/archive/documentation/MacOSX/Conceptual/BPSystemStartup/Chapters/CreatingLaunchdJobs.html); freedesktop.org, [Desktop Application Autostart Specification](https://specifications.freedesktop.org/autostart-spec/latest/) and [Desktop Entry Specification](https://specifications.freedesktop.org/desktop-entry-spec/latest/).

- `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` runs each value's command line at every sign-in of that user. "The data value for a key is a command line no longer than 260 characters." Writing it needs no administrator rights.
- A console program started outside a terminal gets a console window. The application manifest element `<consoleAllocationPolicy xmlns="http://schemas.microsoft.com/SMI/2024/WindowsSettings">detached</consoleAllocationPolicy>` skips that allocation, while a program started in a terminal still uses the terminal. It needs Windows 11 24H2 (build 26100) or Windows Server 2025; older Windows ignores it.
- Verified 2026-11-21: with `<ApplicationManifest>app.manifest</ApplicationManifest>` in `CopilotLedger.Cli.csproj`, `dotnet build` and the single-file publishes for `win-arm64` and `linux-x64` (from Windows) succeed with 0 warnings. The manifest is embedded in the single-file `gh-ledger.exe`, and the binary still prints `--version` in a terminal. A test program with the same manifest, published single-file and started from Explorer, ran with no console window.
- A per-user launchd agent is a property list in `~/Library/LaunchAgents`, loaded at sign-in. `RunAtLoad` starts it when it's loaded, and a launchd job must not daemonize itself.
- An autostart entry is a `.desktop` file in `$XDG_CONFIG_HOME/autostart` (default `~/.config/autostart`). `$XDG_CONFIG_HOME` must be an absolute path. In `Exec`, a quoted argument escapes `"`, `` ` ``, `$`, and `\` with a backslash, and `%` starts a field code.

### .NET

- `JsonElement.TryGetDecimal` accepts JSON numbers with exponents (`1e2` → 100, `1E-2` → 0.01) and fails for strings. `TryGetInt32` fails for `1e2` and `5.0`. Verified on .NET 10.
- `value % 0.01m == 0m` is true exactly when a `decimal` has at most two decimal places: `12.50`, `1.500`, `1e2`, and `55.99` pass, and `0.001`, `1.001`, and `12.345` fail. It's exact and doesn't round, so this plan uses it for every "at most 2 decimal places" check, honoring the rule against rounding in Core and Storage. Verified on .NET 10.
- `JsonWriterOptions.NewLine = "\n"` with `Indented = true` writes `\n` line breaks on every OS. `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` leaves `'`, `+`, and non-ASCII letters unescaped but still escapes `\` and `"`. Verified on .NET 10.
- `Microsoft.Win32.Registry` is part of the shared framework. Code that uses it compiles without warnings under `TreatWarningsAsErrors` when the call is inside `if (OperatingSystem.IsWindows())` (analyzer CA1416). Verified on .NET 10.
- An `IHostedService` registered from `CliContext.ConfigureWebHost` can capture the `TestServer` (`IServer`) of the receiver that `OtlpReceiverApp.Build` creates, and `TestServer.CreateClient()` then posts to it with host `localhost`. Verified on .NET 10 with `Microsoft.AspNetCore.TestHost` 10.0.15.
- SQLite 3.53 (through `Microsoft.Data.Sqlite` 10.0.15) runs Migration 3 on a version-2 database. It supports `INSERT … ON CONFLICT DO NOTHING RETURNING id`, which returns no row when the insert is skipped, `UPDATE … RETURNING`, and `ALTER TABLE … DROP COLUMN` on the new `span_links` columns. Verified 2026-11-21 against the Migration 3 SQL below.
- `Microsoft.Data.Sqlite` 10.0.15 transactions (verified 2026-11-21):
  - `BeginTransaction()`, `BeginTransactionAsync()`, and `BeginTransaction(deferred: false)` all start `BEGIN IMMEDIATE`; only `deferred: true` starts a deferred transaction.
  - While another connection holds the write lock, a second `BEGIN IMMEDIATE` waits: the library retries a busy statement until the connection's `DefaultTimeout` (30 seconds) runs out, whatever `busy_timeout` says. Reads such as `PRAGMA user_version` don't wait in WAL mode.
  - Its `…Async` methods complete synchronously, so a call that waits for the lock blocks its caller's thread. A test that must keep running while a store waits starts the store's call with `Task.Run`.
  - With the merged loop, two concurrent `InitializeAsync` calls on a version-2 ledger reproduce the race: the second fails with `SQLite Error 1: 'duplicate column name: tool_name'`. With Task 1's loop, it waits, sees version 3, and completes.
- xUnit 2.9.3's `Assert.NotNull(object?)` returns nothing, but its `[NotNull]` parameter lets the compiler treat a checked local as non-null afterward. `Assert.NotNull<T>(T?) where T : struct` returns the value. So `Implementation? serverInfo = client.ServerInfo; Assert.NotNull(serverInfo); Assert.Equal("copilot-ledger", serverInfo.Name);` and `JsonElement structured = Assert.NotNull(result.StructuredContent);` compile under `TreatWarningsAsErrors`; reading `client.ServerInfo.Name` directly is error CS8602. Verified on .NET 10.
- `Path.GetFullPath` throws `ArgumentException` (`Null character in path.`) for a path that contains `\0`. Verified on .NET 10.0.15.

### Assumptions (not verified)

- macOS and Linux notifications and sign-in items weren't run live. The commands follow Apple's and freedesktop.org's documentation.
- The GitHub Copilot app's sessions run the same Copilot runtime as Copilot CLI (Sprint 2 verified their `workspace.yaml` and telemetry), so their tool spans and MCP client are assumed to match runs A–C. VS Code's Copilot harness is documented to read `~/.copilot/mcp-config.json`; the app isn't.
- `COPILOT_AGENT_SESSION_ID` and `COPILOT_SESSION_CWD` in an MCP server's environment were observed, not documented. CopilotLedger uses them only as hints and works without them.
- VS Code's Local agent harness may not export `process.exit.code` or tool status. Without them, rule 2 doesn't trip; rule 1 still works.

## Design decisions

### Breaker model

1. **Rules run per prompt chain; trips belong to a (session, branch) pair.** Both rules in the user's plan describe one prompt: rule 1 is "a single prompt/turn chain", and rule 2's retries happen inside the agent's autonomous loop for one prompt. A prompt chain is one trace (Sprint 2 decision 8), so the monitor evaluates each trace that an export request touches. Every trip records its conversation (the session) and its repository and branch keys. The breaker is *open* for a branch, or for a session, while at least one of its trips is open. A user re-prompting five times isn't a runaway loop, because a person is in it, so rule 2 doesn't count failures across traces.
2. **Rule 1, credit velocity.** It sums billed cost per span: `cost_usd_pico`, which is GitHub AI-credit spend plus BYOK spend. Local and unpriced spans cost 0. The window slides over span **end** times, because cost is incurred when a response completes. For each span with a positive cost, in order of end time then span ID, the window holds the spans that ended in `(end − window, end]`. The rule trips at the first span whose window total **exceeds** the threshold (`>`). It's strict, matching the user's "exceeds": a window at exactly the threshold doesn't trip. The defaults are the user's: 150 AI credits ($1.50) in 5 minutes.
3. **Rule 2, retry churn, on observable signals.** Run A shows what Copilot exports: a shell call is the `powershell` tool (on Windows), whose span names the shell executable and carries `process.exit.code`; an edit is `apply_patch`, with no path; and MCP and other tool failures set span status ERROR. So:
   - a **failure** is an `execute_tool` span with an exit code other than 0, or with status code 2;
   - a **passing command** is an `execute_tool` span with exit code 0 and no ERROR status; it stands in for "a passing build/test span", and it resets the count;
   - a **new commit** is a change in the `head_commit_sha` recorded on the trace's `chat` spans; it resets the count;
   - every other tool call (edits, reads, searches) is neutral: it neither counts nor resets.

   The rule trips at the start of the call that brings the consecutive-failure count to the threshold (default 5). Calls are ordered by start time, then span ID; a commit observation at the same millisecond as a tool call comes first.
4. **Rule 2's limits, documented in `docs/cli.md`.**
   - The command isn't exported, so the rule can't tell a test run from any other command. "The same tool or file-edit cycle" becomes "failures with no passing command or commit between them".
   - Any successful command resets the count, so a loop that alternates a failing test with, say, a successful `ls` never trips.
   - A loop that only edits, without running anything that fails, is invisible.
   - `head_commit_sha` is read from `.git` when a batch is ingested, not when the commit happened, and the Git resolver caches it for up to 5 seconds. A commit can therefore reset the count a few seconds late.
   - VS Code's Local harness may not export exit codes or status (Assumptions); the rule then never trips there.
5. **A trip is recorded once and stays open until a person resets it.** `breaker_trips` has `UNIQUE (rule, trace_id)`, so a prompt chain trips each rule at most once, even after a reset, and every re-evaluation of a trace after it trips is a no-op. Trips don't expire. A breaker that closes on its own would remove the flag file under a wrapper script that's waiting for a person. `gh-ledger breaker reset` is the only way to close a trip; no MCP tool resets it, so an agent can't close its own breaker. A new prompt is a new trace and is evaluated from scratch.

### Concurrency and restarts

6. **The monitor runs inside the ingestor's gate.** `LedgerIngestor` already serializes batches with `SemaphoreSlim(1, 1)`. After back-fill, inside the gate, it calls `CircuitBreakerMonitor.EvaluateAsync` with the batch's trace IDs. So one writer records trips, rule windows see every span already committed, and evaluating a trace twice can't record two trips. The monitor keeps no windows in memory: it re-reads each touched trace from SQLite, with at most five queries on the `trace_id` indexes. Other processes (`breaker reset`, `breaker config`, `budget`, and the MCP server's `set_branch_budget`) write through SQLite, whose WAL journal and 5-second busy timeout already serialize them. Store exceptions from the monitor propagate like any other store exception, so the receiver answers 503, Copilot retries the batch, and re-ingesting is idempotent.

   Migrations can race too: at the first sign-in after an upgrade, the sign-in daemon and Copilot's `gh-ledger mcp` can both open a version-2 ledger. Each migration runs in a `BEGIN IMMEDIATE` transaction that re-reads `user_version` inside it, so the second process waits for the first and then skips what's already applied (Task 1). Before this fix, the second process failed with `duplicate column name` and, if it was the daemon, exited.
7. **Alerts never block ingestion.** The monitor hands each alert to `IBreakerAlertSink.Publish`. The daemon's sink, `AlertDispatcher`, writes it to an unbounded single-reader `Channel`, and its own loop logs it, shows the notification (bounded by a 15-second timeout), and records `notified_at`. A slow or failing notifier delays only the next notification, and an alert that can't be handled is skipped without stopping the ones after it.
8. **Restarts lose nothing that's been ingested.** Trips, budgets, and settings live in SQLite, and the rules recompute from the ledger, so a restarted daemon evaluates the next batch of a trace exactly as before. At startup, before the receiver starts, the dispatcher re-queues open trips and budget alerts that were recorded in the last hour but never notified. Older ones are stale, but `breaker status` and the MCP tools still show them. Doing this before the receiver starts means a trip can't be both re-queued and published live. Spans that Copilot exports while the daemon is down are lost (Sprint 2), which is why Task 5 starts the daemon at sign-in.

### Actions on a trip

9. **Notifications use only what the OS already has.** The `net10.0`-only rule rules out WinRT projections, and toast libraries such as `Microsoft.Toolkit.Uwp.Notifications` need a `-windows10.0.x` target framework for them. Windows PowerShell 5.1, which ships with Windows, can call the WinRT toast API, and this was verified without administrator rights. So the daemon runs it through `ICliPlatform.RunAsync`, the same seam as `gh`, and tests assert the exact command line with `FakeCliPlatform`. macOS uses `osascript`, and Linux uses `notify-send` when it's on `PATH`. The fallback on every platform is the `ALERT` line in `daemon.log`, plus `breaker status`, the MCP warnings, and the optional flag file. A terminal bell doesn't apply, because the daemon has no terminal; webhooks are deferred. `breaker config --notifications off` keeps the log line and suppresses the desktop notification.
10. **The `CIRCUIT_OPEN` flag file is opt-in and always matches the ledger.** With `breaker config --flag-file on`, the daemon writes `<repo root>/.copilot-ledger/CIRCUIT_OPEN` when a trip opens, and `breaker reset` removes it when the last open trip for that root closes.
    - It holds JSON describing the open trips, and `.copilot-ledger/.gitignore` (`*`) keeps it out of commits.
    - It's written to a temporary file and moved into place, so a wrapper never reads half a file.
    - Writers don't patch the file. Each one *syncs* it from the database, then re-reads the database and syncs again if anything changed, for at most three writes. So the daemon recording a trip and a CLI resetting one at the same moment still converge on the database's state.
    - A trip whose repository has no local root, such as one attributed only from VS Code's Git attributes, gets no flag file.
    - When the daemon can't write the file, it logs a warning. `gh-ledger breaker config --flag-file on` rewrites every flag file from the ledger, whatever the previous setting.
    - It's off by default because it writes into the user's working tree.
11. **Budgets are soft caps per branch.** `budget set` and the MCP tool `set_branch_budget` store a budget in AI credits per (repository, branch) key, the same keys as the Branch rollup. Spend is billed, all-time spend on that key: GitHub AI credits plus BYOK USD at $0.01 per credit, so a BYOK branch can have a budget too. After each batch, the monitor checks the budget of every branch the batch touched and alerts once, when spend exceeds the budget. Like rule 1, it's strict, matching the user's "crosses": spend exactly at the budget doesn't alert. Setting the budget again re-arms the alert. The update that records the alert also checks that the stored budget is still exceeded, so a budget re-set between the monitor's read and its update can't consume the new budget's alert. A budget alert isn't a trip: it opens no breaker and writes no flag file.

### MCP server

12. **The tools are read-only except `set_branch_budget`.** The user's plan calls the server read-only but also asks for `set_branch_budget`, which writes. That tool writes only the `branch_budgets` row for one branch. It can't change or delete spend, reset the breaker, or change settings, and setting the same budget twice has the same effect as once. It's annotated `readOnlyHint: false`, `destructiveHint: false`, `idempotentHint: true`, and `openWorldHint: false`, and the three read tools are `readOnlyHint: true` and `openWorldHint: false`. Run B showed the effect: Copilot CLI runs read-only tools in prompt mode without approval and denies a write tool unless `--allow-tool 'ledger(set_branch_budget)'` allows it. Interactive sessions ask the user. Resetting the breaker and changing its settings stay CLI-only.
13. **The server uses the SDK's low-level handlers with constant, closed schemas.** Each tool's `inputSchema` is a constant JSON object with `additionalProperties: false`. The SDK doesn't enforce schemas, so the server validates arguments itself and returns `isError: true` with an exact message, which the spec says lets the model correct itself. An unknown tool is a protocol error (`-32602`). Tools declare no `outputSchema`, which keeps `tools/list` short; every result carries the same object twice, as `structuredContent` and as compact JSON text, after any warning lines. The text uses `UnsafeRelaxedJsonEscaping`. The SDK escapes it again on the wire, and clients undo that.
14. **The scope is explicit or comes from the session's working directory.** Every tool takes an optional `repository` and `branch` pair. Without them, the server resolves Git from `COPILOT_SESSION_CWD` when it's set to an existing absolute directory, and otherwise from its own working directory, which Copilot sets to the session's (run B). MCP Roots would have given the client's directories, but 2026-07-28 deprecates them in favor of tool parameters. The repository key is the slug, or the root when there's no slug, and the branch is `(none)` for a detached HEAD, both as in the reports.
15. **Every result warns about an open breaker or an exceeded budget.** A result lists the open trips on its branch and the open trips of the calling session (`COPILOT_AGENT_SESSION_ID`, as observed), and the branch's budget if one is set. It starts with one `WARNING:` line for each such trip and for an exceeded budget, telling the agent to stop and ask the user. That's the "surface the warning in the MCP tool response" action. It reaches the agent only when the agent calls a ledger tool; a `preToolUse` hook that blocks tools while the breaker is open is deferred.
16. **`get_top_expensive_prompts` ranks by cost by default.** The user's plan asks for "the top 5 most expensive prompt chains … by output tokens and retry loops". The tool returns 5 chains by default (`limit` 1–20). Each chain reports its output tokens and its failed tool calls, the retry-loop measure from rule 2. `orderBy` is `cost` (billed USD, the default, because the tool is about expense), `outputTokens`, or `failedToolCalls`. Candidates are the branch's top 1,000 chains by cost. Failed tool calls come from `span_links`, which the daemon prunes after 7 days, so older chains show 0.

### Packaging and sign-in

17. **The daemon starts at sign-in through the OS, not a Copilot hook.** Run D showed that a `sessionStart` hook's detached child is killed when the Copilot CLI session ends, and hooks can also be disabled by policy or by `disableAllHooks`. So `install` registers a per-user sign-in item:
    - **Windows:** the `CopilotLedger` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, which runs `"<gh-ledger.exe>" daemon start`. It needs no administrator rights, and the command must be at most 260 characters.
    - **macOS:** `~/Library/LaunchAgents/io.github.swigerb.copilot-ledger.plist`, which runs `daemon run --log-file …` in the foreground, because launchd jobs must not daemonize.
    - **Linux:** `$XDG_CONFIG_HOME/autostart/copilot-ledger.desktop`, which runs `daemon start`.

    `install.json` records the item, and `uninstall` removes it. `daemon start` is idempotent, so starting the daemon again at sign-in is harmless.
18. **No console window at sign-in.** `gh-ledger` is a console program, and Windows gives a console program started from the Run key its own console window. The application manifest's `consoleAllocationPolicy` `detached` suppresses it on Windows 11 24H2 and later. Older Windows shows the window for about a second, because `daemon start` exits once the detached daemon is up.
19. **One new package.** `ModelContextProtocol` 2.4.0, the version the user's plan names, is the only new package. Only the Mcp project references it directly; the Cli and test projects get it through their project references. It's the official SDK; the server uses its `ModelContextProtocol.Core` types, and its hosting helpers go unused. Toasts, the Run key, LaunchAgents, and autostart entries need no packages.
20. **Version 0.3.0.** The CLI gains the `breaker`, `budget`, and `mcp` commands, so Task 5 bumps the version from 0.2.0.

## Task 1 specification: Tool-Call Signals & Breaker Storage

### Files

- `src/CopilotLedger.Otlp/Decoding/OtlpModel.cs`: add `OtlpSpan.StatusCode`.
- `src/CopilotLedger.Otlp/Decoding/OtlpProtobufTraceReader.cs` and `OtlpJsonTraceReader.cs`: read the span status.
- `src/CopilotLedger.Core/Telemetry/GenAiSpanLink.cs`: add `ToolName`, `ToolExitCode`, and `StatusCode`.
- `src/CopilotLedger.Otlp/Normalization/GenAiAttributeNames.cs`: add `ToolName` and `ProcessExitCode`.
- `src/CopilotLedger.Otlp/Normalization/GenAiSpanNormalizer.cs`: fill the three link properties.
- `src/CopilotLedger.Core/Breaker/BreakerRule.cs` (with `BreakerRuleNames`), `BreakerSettings.cs`, `BreakerEvents.cs` (`CostEvent`, `ToolCallEvent`, `CommitObservation`), and `TraceActivity.cs`.
- `src/CopilotLedger.Storage/LedgerMigrations.cs`: Migration 3.
- `src/CopilotLedger.Storage/BreakerTrip.cs` (`NewBreakerTrip`, `BreakerTrip`), `BreakerTripFilter.cs`, and `BranchBudgetRow.cs`.
- `src/CopilotLedger.Storage/SqliteLedgerStore.cs`: the migration loop in `InitializeAsync`, three more link columns, and the new methods.
- Tests: `TestSupport/GitFixture.cs` (new, shared by Tasks 1–4), `Otlp/OtlpProtobufDecodingTests.cs` (PB8), `Otlp/OtlpJsonDecodingTests.cs` (JS10), `Otlp/GenAiSpanNormalizerTests.cs` (NZ12), `Storage/SpanLinkStoreTests.cs` (SL1 changed; SL8–SL10 new), `Storage/SqliteLedgerStoreTests.cs` (S1 changed), and the new `Storage/BreakerStoreTests.cs` (BS1–BS7).

### Contracts

Add to `OtlpSpan`:

```csharp
public int StatusCode { get; init; }                   // Status.code: 1 ok, 2 error; 0 when unset, missing, or any other value
```

Add to `GenAiSpanLink`:

```csharp
public string? ToolName { get; init; }                 // gen_ai.tool.name on an execute_tool span; null otherwise
public long? ToolExitCode { get; init; }               // process.exit.code (an int attribute) on an execute_tool span; null otherwise
public int StatusCode { get; init; }                   // OtlpSpan.StatusCode
```

Add to `GenAiAttributeNames`:

```csharp
public const string ToolName = "gen_ai.tool.name";
public const string ProcessExitCode = "process.exit.code";
```

`src/CopilotLedger.Core/Breaker/` (namespace `CopilotLedger.Core.Breaker`):

```csharp
public enum BreakerRule
{
    CreditVelocity,                                    // rule 1
    RetryChurn,                                        // rule 2
}

public static class BreakerRuleNames
{
    public const string CreditVelocity = "credit_velocity";
    public const string RetryChurn = "retry_churn";

    // Throws ArgumentOutOfRangeException for an undefined value.
    public static string ToName(BreakerRule rule);

    // Ordinal. Returns false for any other text, including null.
    public static bool TryParse(string? name, out BreakerRule rule);
}

public sealed record BreakerSettings
{
    public const decimal MinVelocityCredits = 1m;
    public const decimal MaxVelocityCredits = 1_000_000m;
    public const int MinVelocityWindowMinutes = 1;
    public const int MaxVelocityWindowMinutes = 1440;
    public const int MinChurnFailures = 2;
    public const int MaxChurnFailures = 100;

    public static BreakerSettings Default { get; } = new();

    public decimal VelocityCredits { get; init; } = 150m;          // rule 1 threshold in AI credits
    public int VelocityWindowMinutes { get; init; } = 5;           // rule 1 window
    public int ChurnFailures { get; init; } = 5;                   // rule 2 threshold
    public bool FlagFile { get; init; }                            // keep <repo root>/.copilot-ledger/CIRCUIT_OPEN while a trip is open
    public bool Notifications { get; init; } = true;               // show desktop notifications; alerts are always logged

    public decimal VelocityThresholdUsd => VelocityCredits * AiCreditMath.UsdPerAiCredit;
    public TimeSpan VelocityWindow => TimeSpan.FromMinutes(VelocityWindowMinutes);

    // Limits are inclusive. "At most two decimal places" is value % 0.01m == 0m throughout this plan; it doesn't round.
    public bool IsValid =>
        VelocityCredits >= MinVelocityCredits
        && VelocityCredits <= MaxVelocityCredits
        && VelocityCredits % 0.01m == 0m
        && VelocityWindowMinutes is >= MinVelocityWindowMinutes and <= MaxVelocityWindowMinutes
        && ChurnFailures is >= MinChurnFailures and <= MaxChurnFailures;
}

public sealed record CostEvent(
    string SpanId,                                     // spans.span_id
    DateTimeOffset EndTime,                            // spans.end_time_unix_ms
    decimal CostUsd);                                  // PicoUsd.ToUsd(spans.cost_usd_pico): GitHub AI credits or BYOK

public sealed record ToolCallEvent(
    string SpanId,                                     // span_links.span_id of an execute_tool span
    DateTimeOffset StartTime,                          // span_links.start_time_unix_ms
    string? ToolName,                                  // span_links.tool_name
    long? ExitCode,                                    // span_links.tool_exit_code
    int StatusCode)                                    // span_links.status_code
{
    public bool IsFailure => (ExitCode is long code && code != 0) || StatusCode == 2;
    public bool IsSuccessfulCommand => ExitCode == 0 && StatusCode != 2;
}

public sealed record CommitObservation(
    string SpanId,                                     // spans.span_id
    DateTimeOffset StartTime,                          // spans.start_time_unix_ms
    string CommitSha);                                 // spans.head_commit_sha

public sealed record TraceActivity
{
    public required string TraceId { get; init; }
    public string? ConversationId { get; init; }                         // MAX over the trace's spans rows; when that's null, MAX over its span_links rows
    public required string Repository { get; init; }                     // MAX(COALESCE(repo_slug, repo_root, '(none)')); "(none)" with no spans rows
    public required string Branch { get; init; }                         // MAX(COALESCE(branch_name, '(none)')); "(none)" with no spans rows
    public string? RepositoryRoot { get; init; }                         // MAX(repo_root)
    public required long SpanCount { get; init; }                        // the trace's spans rows
    public required IReadOnlyList<CostEvent> Costs { get; init; }        // spans rows with cost_usd_pico > 0, by end time, then span ID
    public required IReadOnlyList<ToolCallEvent> ToolCalls { get; init; } // execute_tool links, by start time, then span ID
    public required IReadOnlyList<CommitObservation> Commits { get; init; } // spans rows with a head_commit_sha, by start time, then span ID
}
```

`BreakerSettings.IsValid` is a computed property, so it isn't part of the record's equality.

`src/CopilotLedger.Storage/` (namespace `CopilotLedger.Storage`):

```csharp
public sealed record NewBreakerTrip
{
    public required BreakerRule Rule { get; init; }
    public required string TraceId { get; init; }
    public string? ConversationId { get; init; }
    public required string Repository { get; init; }                     // TraceActivity.Repository
    public required string Branch { get; init; }                         // TraceActivity.Branch
    public string? RepositoryRoot { get; init; }                         // TraceActivity.RepositoryRoot
    public required DateTimeOffset TrippedAt { get; init; }
    public required string Detail { get; init; }                         // "180.00 AI credits in 5 minutes" or "5 failed tool calls in a row"
    public decimal? WindowCostUsd { get; init; }                         // CreditVelocity only
    public int? WindowMinutes { get; init; }                             // CreditVelocity only
    public int? FailureCount { get; init; }                              // RetryChurn only
}

public sealed record BreakerTrip
{
    public required long Id { get; init; }
    public required BreakerRule Rule { get; init; }
    public required string TraceId { get; init; }
    public string? ConversationId { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public string? RepositoryRoot { get; init; }
    public required DateTimeOffset TrippedAt { get; init; }
    public required string Detail { get; init; }
    public decimal? WindowCostUsd { get; init; }
    public int? WindowMinutes { get; init; }
    public int? FailureCount { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public DateTimeOffset? NotifiedAt { get; init; }
    public DateTimeOffset? ResetAt { get; init; }
}

public sealed record BreakerTripFilter
{
    public static BreakerTripFilter All { get; } = new();
    public long? Id { get; init; }
    public string? Repository { get; init; }
    public string? Branch { get; init; }
    public string? RepositoryRoot { get; init; }                         // ordinal match on repo_root
}

public sealed record BranchBudgetRow
{
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required decimal BudgetAiCredits { get; init; }
    public required decimal SpentAiCredits { get; init; }                // all-time billed spend on the key: GitHub AI credits plus BYOK USD / 0.01
    public required DateTimeOffset SetAt { get; init; }
    public DateTimeOffset? AlertedAt { get; init; }                      // when the monitor first saw spend exceed the budget
    public DateTimeOffset? NotifiedAt { get; init; }                     // when the daemon finished that alert
    public bool Exceeded => SpentAiCredits > BudgetAiCredits;            // strict: spend exactly at the budget isn't exceeded
}
```

New `SqliteLedgerStore` methods. Each one throws `InvalidOperationException` before `InitializeAsync`, opens one connection, and uses the store's `TimeProvider` for every `@now` (`GetUtcNow().ToUnixTimeMilliseconds()`):

```csharp
// One trace's breaker inputs, from the five queries below on one connection. Throws ArgumentException for a blank traceId.
public Task<TraceActivity> GetTraceActivityAsync(string traceId, CancellationToken cancellationToken = default);

// Inserts the trip unless (Rule, TraceId) already has one, and returns the new id, or null when it was skipped.
// Throws ArgumentException for a blank TraceId, Repository, Branch, or Detail; ArgumentOutOfRangeException for a negative WindowCostUsd.
public Task<long?> RecordBreakerTripAsync(NewBreakerTrip trip, CancellationToken cancellationToken = default);

// Open trips (reset_at_unix_ms IS NULL) that match every non-null filter property, newest first.
public Task<IReadOnlyList<BreakerTrip>> GetOpenBreakerTripsAsync(BreakerTripFilter filter, CancellationToken cancellationToken = default);

// Resets the open trips that match the filter and returns them, with ResetAt set, in ascending Id order.
public Task<IReadOnlyList<BreakerTrip>> ResetBreakerTripsAsync(BreakerTripFilter filter, CancellationToken cancellationToken = default);

// Sets notified_at_unix_ms when it's NULL. Returns true when a row changed.
public Task<bool> MarkBreakerTripNotifiedAsync(long id, CancellationToken cancellationToken = default);

// Open, never-notified trips recorded at or after recordedSince, oldest first.
public Task<IReadOnlyList<BreakerTrip>> GetUnnotifiedBreakerTripsAsync(DateTimeOffset recordedSince, CancellationToken cancellationToken = default);

// The settings row; BreakerSettings.Default if the row is missing.
public Task<BreakerSettings> GetBreakerSettingsAsync(CancellationToken cancellationToken = default);

// Throws ArgumentNullException for null; ArgumentOutOfRangeException when !settings.IsValid, without writing.
public Task UpdateBreakerSettingsAsync(BreakerSettings settings, CancellationToken cancellationToken = default);

// Inserts or replaces the budget and clears alerted_at and notified_at. Throws ArgumentException for a blank key, and
// ArgumentOutOfRangeException unless 0 < aiCredits <= 1,000,000 and aiCredits % 0.01m == 0m.
public Task SetBranchBudgetAsync(string repository, string branch, decimal aiCredits, CancellationToken cancellationToken = default);

// Returns true when a budget was deleted.
public Task<bool> ClearBranchBudgetAsync(string repository, string branch, CancellationToken cancellationToken = default);

// Budgets with their spend, ordered by repository, then branch (SQLite BINARY collation). A null argument matches every value.
public Task<IReadOnlyList<BranchBudgetRow>> GetBranchBudgetsAsync(string? repository = null, string? branch = null, CancellationToken cancellationToken = default);

// Sets alerted_at_unix_ms when it's NULL and the key's spend, summed in the same statement, exceeds the stored budget.
// Returns true when a row changed. Checking in the UPDATE means a budget re-set after the caller's read is never marked.
public Task<bool> MarkBranchBudgetAlertedAsync(string repository, string branch, CancellationToken cancellationToken = default);

// Sets notified_at_unix_ms when alerted_at_unix_ms is set and notified_at_unix_ms is NULL. Returns true when a row changed.
public Task<bool> MarkBranchBudgetNotifiedAsync(string repository, string branch, CancellationToken cancellationToken = default);

// Budgets alerted at or after alertedSince and never notified, ordered by alerted_at, then repository, then branch.
public Task<IReadOnlyList<BranchBudgetRow>> GetUnnotifiedBudgetAlertsAsync(DateTimeOffset alertedSince, CancellationToken cancellationToken = default);

// For each given trace with at least one failed execute_tool link (an exit code other than 0, or status 2), that count.
// Traces without failures are absent. An empty input returns an empty dictionary without a query.
public Task<IReadOnlyDictionary<string, long>> GetFailedToolCallCountsAsync(IEnumerable<string> traceIds, CancellationToken cancellationToken = default);
```

Money conversions use `PicoUsd`: a budget or threshold of `c` AI credits is stored as `PicoUsd.FromUsd(c * AiCreditMath.UsdPerAiCredit)` and read back as `PicoUsd.ToUsd(pico) / AiCreditMath.UsdPerAiCredit`. Rules are stored with `BreakerRuleNames`, and an unknown stored rule throws `InvalidDataException("Unknown breaker rule '<value>'.")`.

### Decoding the span status

`Span` field 15 is `status` (LEN, a `Status` message), and `Status` field 3 is `code` (VARINT, enum `STATUS_CODE_UNSET` 0, `STATUS_CODE_OK` 1, `STATUS_CODE_ERROR` 2). Field 2, `message`, isn't kept.

- **Protobuf.** In `ReadSpan`, handle field 15 with wire type 2 in the same branch as fields 1, 2, 4, 5, and 9. Decode the bytes with a `ProtoReader`: for each field 3 with wire type 0, read the varint and keep `1` or `2`, or `0` for any other value (the last one wins); skip every other field with `Skip`. Field 15 with another wire type, like every unknown field, is skipped by the existing `else` branch.
- **JSON.** `StatusCode` is 1 or 2 when `TryFindProperty(span, "status", out status)`, `status.ValueKind == JsonValueKind.Object`, `TryFindProperty(status, "code", out code)`, `code.ValueKind == JsonValueKind.Number`, and `code.TryGetInt32(out value)` succeed with `value` 1 or 2. Anything else gives 0: OTLP/JSON encodes enums as integers, so a string such as `"STATUS_CODE_ERROR"` or `"2"` isn't accepted. Use only `TryFindProperty` and `NameIs`, never `TryGetProperty`, so an unreadable name can't throw (amendment 4 of Sprint 2).

### Normalizing tool calls

In `TryNormalize`, where it builds the `GenAiSpanLink`:

- `StatusCode = span.StatusCode` for every link;
- when `operation` equals `GenAiOperationNames.ExecuteTool` (ordinal): `ToolName = Text(attributes, GenAiAttributeNames.ToolName)`, and `ToolExitCode` is the `IntValue` of the first `process.exit.code` attribute when its kind is `OtlpValueKind.Int` (any value, including negative ones), otherwise null;
- for every other operation, `ToolName` and `ToolExitCode` are null.

Nothing else changes: these attributes never reject a span, and `NormalizedGenAiSpan` doesn't change.

### Migration 3 (copy exactly)

Append `(3, """…""")` to `LedgerMigrations.Migrations` with this SQL:

```sql
ALTER TABLE span_links ADD COLUMN tool_name TEXT;
ALTER TABLE span_links ADD COLUMN tool_exit_code INTEGER;
ALTER TABLE span_links ADD COLUMN status_code INTEGER NOT NULL DEFAULT 0;

CREATE TABLE breaker_trips (
    id                   INTEGER PRIMARY KEY,
    rule                 TEXT    NOT NULL CHECK (rule IN ('credit_velocity', 'retry_churn')),
    trace_id             TEXT    NOT NULL,
    conversation_id      TEXT,
    repository           TEXT    NOT NULL,
    branch               TEXT    NOT NULL,
    repo_root            TEXT,
    tripped_at_unix_ms   INTEGER NOT NULL,
    detail               TEXT    NOT NULL,
    window_cost_usd_pico INTEGER CHECK (window_cost_usd_pico >= 0),
    window_minutes       INTEGER,
    failure_count        INTEGER,
    recorded_at_unix_ms  INTEGER NOT NULL,
    notified_at_unix_ms  INTEGER,
    reset_at_unix_ms     INTEGER,
    UNIQUE (rule, trace_id)
) STRICT;

CREATE INDEX ix_breaker_trips_open ON breaker_trips (reset_at_unix_ms, repository, branch);

CREATE TABLE branch_budgets (
    repository          TEXT    NOT NULL,
    branch              TEXT    NOT NULL,
    budget_usd_pico     INTEGER NOT NULL CHECK (budget_usd_pico > 0),
    set_at_unix_ms      INTEGER NOT NULL,
    alerted_at_unix_ms  INTEGER,
    notified_at_unix_ms INTEGER,
    PRIMARY KEY (repository, branch)
) STRICT, WITHOUT ROWID;

CREATE TABLE breaker_settings (
    id                          INTEGER PRIMARY KEY CHECK (id = 1),
    velocity_threshold_usd_pico INTEGER NOT NULL CHECK (velocity_threshold_usd_pico > 0),
    velocity_window_minutes     INTEGER NOT NULL CHECK (velocity_window_minutes BETWEEN 1 AND 1440),
    churn_failures              INTEGER NOT NULL CHECK (churn_failures BETWEEN 2 AND 100),
    flag_file                   INTEGER NOT NULL CHECK (flag_file IN (0, 1)),
    notifications               INTEGER NOT NULL CHECK (notifications IN (0, 1)),
    updated_at_unix_ms          INTEGER NOT NULL
) STRICT;

INSERT INTO breaker_settings (id, velocity_threshold_usd_pico, velocity_window_minutes, churn_failures, flag_file, notifications, updated_at_unix_ms)
VALUES (1, 1500000000000, 5, 5, 0, 1, 0);
```

`RecordSpanLinksAsync` adds `tool_name`, `tool_exit_code`, and `status_code` to its column list and parameters (`OrDbNull(link.ToolName)`, `OrDbNull(link.ToolExitCode)`, and `link.StatusCode`). Rows that already exist stay unchanged.

### Applying migrations (copy exactly)

`InitializeAsync` reads `user_version` once, before its loop, so two processes that open a version-2 ledger at the same time both try Migration 3, and the second fails with `duplicate column name: tool_name`. That happens at the first sign-in after an upgrade, when the sign-in daemon and Copilot's `gh-ledger mcp` start together. Replace the `foreach` loop in `InitializeAsync` with this one. The WAL check, the first `GetUserVersionAsync` call, the "newer than the supported version" check, and `_initialized = true;` stay as they are.

```csharp
foreach ((int version, string sql) in LedgerMigrations.Migrations)
{
    if (version <= userVersion)
    {
        continue;
    }

    // BEGIN IMMEDIATE takes the write lock before the version is read again, so a second process waits here
    // and then skips the migrations the first one committed. Disposing an uncommitted transaction rolls it back.
    await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
    await using (SqliteCommand versionQuery = connection.CreateCommand())
    {
        versionQuery.Transaction = transaction;
        versionQuery.CommandText = "PRAGMA user_version;";
        userVersion = Convert.ToInt32(
            await versionQuery.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    if (version <= userVersion)
    {
        continue;
    }

    await using (SqliteCommand migrationCommand = connection.CreateCommand())
    {
        migrationCommand.Transaction = transaction;
        migrationCommand.CommandText = sql;
        await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    await using (SqliteCommand versionCommand = connection.CreateCommand())
    {
        versionCommand.Transaction = transaction;
        versionCommand.CommandText = $"PRAGMA user_version = {version};";
        await versionCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    await transaction.CommitAsync(cancellationToken);
    userVersion = version;
}
```

While another connection holds the write lock, `BeginTransaction` waits: Microsoft.Data.Sqlite retries a busy statement for the connection's `DefaultTimeout` (30 seconds), whatever `busy_timeout` says.

### Store SQL (copy exactly)

`<trip columns>` stands for `id, rule, trace_id, conversation_id, repository, branch, repo_root, tripped_at_unix_ms, detail, window_cost_usd_pico, window_minutes, failure_count, recorded_at_unix_ms, notified_at_unix_ms, reset_at_unix_ms`, read in that order into `BreakerTrip`. Times convert with `DateTimeOffset.FromUnixTimeMilliseconds`, and `window_cost_usd_pico` with `PicoUsd.ToUsd`. `<budget select>` stands for:

```sql
SELECT b.repository, b.branch, b.budget_usd_pico, b.set_at_unix_ms, b.alerted_at_unix_ms, b.notified_at_unix_ms,
       (SELECT COALESCE(SUM(s.cost_usd_pico), 0) FROM spans AS s
         WHERE COALESCE(s.repo_slug, s.repo_root, '(none)') = b.repository
           AND COALESCE(s.branch_name, '(none)') = b.branch) AS spent_usd_pico
FROM branch_budgets AS b
```

| Method | SQL |
|---|---|
| `GetTraceActivityAsync` | (1) `SELECT MAX(conversation_id), MAX(COALESCE(repo_slug, repo_root, '(none)')), MAX(COALESCE(branch_name, '(none)')), MAX(repo_root), COUNT(*) FROM spans WHERE trace_id = @trace;` (2) only when (1)'s conversation is NULL: `SELECT MAX(conversation_id) FROM span_links WHERE trace_id = @trace;` (3) `SELECT span_id, end_time_unix_ms, cost_usd_pico FROM spans WHERE trace_id = @trace AND cost_usd_pico > 0 ORDER BY end_time_unix_ms, span_id;` (4) `SELECT span_id, start_time_unix_ms, tool_name, tool_exit_code, status_code FROM span_links WHERE trace_id = @trace AND operation_name = 'execute_tool' ORDER BY start_time_unix_ms, span_id;` (5) `SELECT span_id, start_time_unix_ms, head_commit_sha FROM spans WHERE trace_id = @trace AND head_commit_sha IS NOT NULL ORDER BY start_time_unix_ms, span_id;` With `COUNT(*)` 0, the repository and branch are `(none)` and the root is null. |
| `RecordBreakerTripAsync` | `INSERT INTO breaker_trips (rule, trace_id, conversation_id, repository, branch, repo_root, tripped_at_unix_ms, detail, window_cost_usd_pico, window_minutes, failure_count, recorded_at_unix_ms) VALUES (@rule, @trace_id, @conversation_id, @repository, @branch, @repo_root, @tripped_at_unix_ms, @detail, @window_cost_usd_pico, @window_minutes, @failure_count, @now) ON CONFLICT (rule, trace_id) DO NOTHING RETURNING id;` with `ExecuteScalarAsync`: a `long`, or null when nothing was inserted. |
| `GetOpenBreakerTripsAsync` | `SELECT <trip columns> FROM breaker_trips WHERE reset_at_unix_ms IS NULL AND (@id IS NULL OR id = @id) AND (@repository IS NULL OR repository = @repository) AND (@branch IS NULL OR branch = @branch) AND (@repo_root IS NULL OR repo_root = @repo_root) ORDER BY tripped_at_unix_ms DESC, id DESC;` |
| `ResetBreakerTripsAsync` | `UPDATE breaker_trips SET reset_at_unix_ms = @now WHERE reset_at_unix_ms IS NULL AND (@id IS NULL OR id = @id) AND (@repository IS NULL OR repository = @repository) AND (@branch IS NULL OR branch = @branch) AND (@repo_root IS NULL OR repo_root = @repo_root) RETURNING <trip columns>;` then sort by `Id` ascending (`RETURNING` order isn't defined). |
| `MarkBreakerTripNotifiedAsync` | `UPDATE breaker_trips SET notified_at_unix_ms = @now WHERE id = @id AND notified_at_unix_ms IS NULL;` |
| `GetUnnotifiedBreakerTripsAsync` | `SELECT <trip columns> FROM breaker_trips WHERE reset_at_unix_ms IS NULL AND notified_at_unix_ms IS NULL AND recorded_at_unix_ms >= @since ORDER BY recorded_at_unix_ms, id;` |
| `GetBreakerSettingsAsync` | `SELECT velocity_threshold_usd_pico, velocity_window_minutes, churn_failures, flag_file, notifications FROM breaker_settings WHERE id = 1;` |
| `UpdateBreakerSettingsAsync` | `INSERT INTO breaker_settings (id, velocity_threshold_usd_pico, velocity_window_minutes, churn_failures, flag_file, notifications, updated_at_unix_ms) VALUES (1, @threshold, @minutes, @failures, @flag_file, @notifications, @now) ON CONFLICT (id) DO UPDATE SET velocity_threshold_usd_pico = excluded.velocity_threshold_usd_pico, velocity_window_minutes = excluded.velocity_window_minutes, churn_failures = excluded.churn_failures, flag_file = excluded.flag_file, notifications = excluded.notifications, updated_at_unix_ms = excluded.updated_at_unix_ms;` (booleans as 0 or 1) |
| `SetBranchBudgetAsync` | `INSERT INTO branch_budgets (repository, branch, budget_usd_pico, set_at_unix_ms, alerted_at_unix_ms, notified_at_unix_ms) VALUES (@repository, @branch, @budget_usd_pico, @now, NULL, NULL) ON CONFLICT (repository, branch) DO UPDATE SET budget_usd_pico = excluded.budget_usd_pico, set_at_unix_ms = excluded.set_at_unix_ms, alerted_at_unix_ms = NULL, notified_at_unix_ms = NULL;` |
| `ClearBranchBudgetAsync` | `DELETE FROM branch_budgets WHERE repository = @repository AND branch = @branch;` |
| `GetBranchBudgetsAsync` | `<budget select> WHERE (@repository IS NULL OR b.repository = @repository) AND (@branch IS NULL OR b.branch = @branch) ORDER BY b.repository, b.branch;` |
| `MarkBranchBudgetAlertedAsync` | `UPDATE branch_budgets SET alerted_at_unix_ms = @now WHERE repository = @repository AND branch = @branch AND alerted_at_unix_ms IS NULL AND budget_usd_pico < (SELECT COALESCE(SUM(cost_usd_pico), 0) FROM spans WHERE COALESCE(repo_slug, repo_root, '(none)') = @repository AND COALESCE(branch_name, '(none)') = @branch);` |
| `MarkBranchBudgetNotifiedAsync` | `UPDATE branch_budgets SET notified_at_unix_ms = @now WHERE repository = @repository AND branch = @branch AND alerted_at_unix_ms IS NOT NULL AND notified_at_unix_ms IS NULL;` |
| `GetUnnotifiedBudgetAlertsAsync` | `<budget select> WHERE b.alerted_at_unix_ms IS NOT NULL AND b.alerted_at_unix_ms >= @since AND b.notified_at_unix_ms IS NULL ORDER BY b.alerted_at_unix_ms, b.repository, b.branch;` |
| `GetFailedToolCallCountsAsync` | `SELECT trace_id, COUNT(*) FROM span_links WHERE trace_id IN (@t0, @t1, …) AND operation_name = 'execute_tool' AND ((tool_exit_code IS NOT NULL AND tool_exit_code <> 0) OR status_code = 2) GROUP BY trace_id;` with one parameter per distinct trace ID. Return a `Dictionary<string, long>` with `StringComparer.Ordinal`. |

`cost_usd_pico` is 0 for local, unpriced, and unknown-model spans, so `SUM(cost_usd_pico)` is GitHub plus BYOK spend. Every statement that writes runs as one statement, so no explicit transaction is needed.

### Acceptance criteria

- PB8, JS10, NZ12, SL8–SL10, and BS1–BS7 pass, along with SL1 and S1 as changed. Every other Sprint 1 and Sprint 2 test passes unchanged.
- A version-2 ledger upgrades to version 3 and keeps its rows; a fresh ledger is created at version 3 with the default settings row. Two processes that upgrade the same ledger at once both succeed (SL10).
- `src/` contains no `double` or `float`. Money and thresholds are stored only as `INTEGER` pico-USD.
- No new package.

### Verify

```shell
dotnet build CopilotLedger.slnx
dotnet test CopilotLedger.slnx
```

Report the passed, failed, and skipped counts.

## Task 2 specification: Circuit Breaker Engine & Flag File

### Files

- `src/CopilotLedger.Core/Breaker/BreakerRules.cs`: `BreakerRules`, `VelocityTrip`, and `ChurnTrip`.
- `src/CopilotLedger.Otlp/Breaker/BreakerAlert.cs` (`BreakerAlertKind`, `BreakerAlert`, `IBreakerAlertSink`), `BreakerMessages.cs`, `CircuitFlagFile.cs`, and `CircuitBreakerMonitor.cs` (namespace `CopilotLedger.Otlp.Breaker`).
- `src/CopilotLedger.Otlp/Ingestion/LedgerIngestor.cs`: a second constructor and the monitor call.
- Tests: `TestSupport/BreakerTraces.cs` and `TestSupport/RecordingAlertSink.cs` (new), `Breaker/BreakerRulesTests.cs` (BR1–BR4, new), and `Otlp/CircuitBreakerMonitorTests.cs` (BM1–BM11, new).

### Contracts

```csharp
namespace CopilotLedger.Core.Breaker;

public sealed record VelocityTrip(
    DateTimeOffset TrippedAt,                          // end of the span whose window first exceeded the threshold
    decimal WindowCostUsd);                            // the window's total at that span

public sealed record ChurnTrip(
    DateTimeOffset TrippedAt,                          // start of the failing call that reached the threshold
    int FailureCount);                                 // always the threshold

public static class BreakerRules
{
    // Rule 1. Throws ArgumentNullException for null costs; ArgumentOutOfRangeException unless thresholdUsd > 0 and window > TimeSpan.Zero.
    public static VelocityTrip? FindVelocityTrip(IReadOnlyList<CostEvent> costs, decimal thresholdUsd, TimeSpan window);

    // Rule 2. Throws ArgumentNullException for a null list; ArgumentOutOfRangeException unless failures >= 2.
    public static ChurnTrip? FindChurnTrip(IReadOnlyList<ToolCallEvent> toolCalls, IReadOnlyList<CommitObservation> commits, int failures);
}
```

```csharp
namespace CopilotLedger.Otlp.Breaker;

public enum BreakerAlertKind
{
    Trip,
    Budget,
}

public sealed record BreakerAlert
{
    public required BreakerAlertKind Kind { get; init; }
    public long? TripId { get; init; }                 // Trip alerts only
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required bool ShowNotification { get; init; } // BreakerSettings.Notifications when the alert was raised
}

public interface IBreakerAlertSink
{
    void Publish(BreakerAlert alert);                  // must return quickly and not throw
    void Warn(string message);                         // a problem the monitor handled, such as a flag file it couldn't write; must not throw
}

public static class BreakerMessages
{
    public const string TripTitle = "CopilotLedger circuit breaker tripped";
    public const string BudgetTitle = "CopilotLedger branch budget exceeded";

    public static string Credits(decimal aiCredits);                          // decimal.Round(aiCredits, 2, MidpointRounding.AwayFromZero).ToString("N2", CultureInfo.InvariantCulture)
    public static string Minutes(int minutes);                                // "1 minute", otherwise "<n> minutes" (invariant culture)
    public static string VelocityDetail(decimal windowCostUsd, int windowMinutes); // "<Credits(windowCostUsd / 0.01)> AI credits in <Minutes(windowMinutes)>"
    public static string ChurnDetail(int failureCount);                       // "<n> failed tool calls in a row"
    public static string RulesSummary(BreakerSettings settings);              // "more than <Credits(VelocityCredits)> AI credits in <Minutes(VelocityWindowMinutes)> on one prompt; <ChurnFailures> failed tool calls in a row on one prompt"
    public static BreakerAlert ForTrip(long tripId, string repository, string branch, string detail, bool showNotification);
    public static BreakerAlert ForBudget(BranchBudgetRow budget, bool showNotification);
}

public static class CircuitFlagFile
{
    public const string DirectoryName = ".copilot-ledger";
    public const string FileName = "CIRCUIT_OPEN";

    public static string GetPath(string repositoryRoot);                     // Path.Combine(repositoryRoot, DirectoryName, FileName)
    public static string Render(string repositoryRoot, IReadOnlyList<BreakerTrip> openTrips);   // the file's text; openTrips is non-empty

    // Makes the flag file match the ledger. Throws IOException or UnauthorizedAccessException from the file system, and
    // whatever the store throws. Does nothing when repositoryRoot isn't an existing directory.
    public static Task SyncAsync(SqliteLedgerStore store, string repositoryRoot, CancellationToken cancellationToken = default);
}

public sealed class CircuitBreakerMonitor
{
    // Throws ArgumentNullException for null arguments. The caller initializes the store.
    public CircuitBreakerMonitor(SqliteLedgerStore store, IBreakerAlertSink sink);

    // Evaluates both rules for each trace and the budgets of the branches those traces touched. Store exceptions propagate.
    // Flag-file IOException and UnauthorizedAccessException are reported to sink.Warn and not rethrown.
    public Task EvaluateAsync(IEnumerable<string> traceIds, CancellationToken cancellationToken = default);
}
```

Add to `LedgerIngestor`:

```csharp
// monitor null means no circuit breaker. Throws ArgumentNullException for the other null arguments.
public LedgerIngestor(
    SqliteLedgerStore store,
    ICreditCalculator calculator,
    IGitContextResolver gitResolver,
    CopilotSessionWorkspaceLocator sessionLocator,
    CircuitBreakerMonitor? monitor)
```

The existing four-argument constructor becomes `: this(store, calculator, gitResolver, sessionLocator, null)`. In `IngestAsync`, inside the gate, immediately after `BackfillFromAgentSpansAsync(traceIds, …)` and inside the same `if (traceIds.Length > 0)`, add `if (_monitor is not null) { await _monitor.EvaluateAsync(traceIds, cancellationToken); }`. `IngestResult` doesn't change.

### Rule 1 algorithm (`FindVelocityTrip`)

1. Let `events` be the costs with `CostUsd > 0`, sorted by `EndTime`, then by `SpanId` (ordinal).
2. Keep a running `sum` and the index `first` of the oldest event in the window, both starting at 0.
3. For each index `i`: add `events[i].CostUsd` to `sum`. Then, while `events[first].EndTime <= events[i].EndTime - window`, subtract `events[first].CostUsd` and increment `first`. If `sum > thresholdUsd` (strict; a window exactly at the threshold doesn't trip), return `new VelocityTrip(events[i].EndTime, sum)`.
4. Return null.

### Rule 2 algorithm (`FindChurnTrip`)

1. Merge the commits (kind 0) and the tool calls (kind 1) into one sequence, sorted by start time, then kind, then `SpanId` (ordinal).
2. `lastSha` starts null and `streak` at 0. For each item:
   - a commit: if `lastSha` isn't null and differs from `CommitSha` (ordinal), set `streak` to 0. Then set `lastSha` to `CommitSha`;
   - a tool call with `IsFailure`: increment `streak`; when `streak == failures`, return `new ChurnTrip(call.StartTime, failures)`;
   - a tool call with `IsSuccessfulCommand`: set `streak` to 0;
   - any other tool call: no change.
3. Return null.

### Messages

| Text | Value |
|---|---|
| Trip alert title | `CopilotLedger circuit breaker tripped` |
| Trip alert body | `<Repository> @ <Branch>: <Detail> on one prompt. Run: gh-ledger breaker status` |
| Budget alert title | `CopilotLedger branch budget exceeded` |
| Budget alert body | `<Repository> @ <Branch>: <Credits(SpentAiCredits)> of <Credits(BudgetAiCredits)> AI credits spent. Run: gh-ledger budget list` |

`ForTrip` sets `Kind = Trip`, `TripId = tripId`, and the given repository and branch, and builds the body from `detail`. `ForBudget` sets `Kind = Budget` and `TripId = null`.

### Monitor algorithm (`EvaluateAsync`)

1. Take the distinct trace IDs in ordinal order. If there are none, return.
2. `settings = await store.GetBreakerSettingsAsync()`. Keep an ordinal-sorted set of budget keys (repository, then branch).
3. For each trace:
   1. `activity = await store.GetTraceActivityAsync(trace)`. If `activity.SpanCount > 0`, add `(activity.Repository, activity.Branch)` to the budget keys.
   2. If `FindVelocityTrip(activity.Costs, settings.VelocityThresholdUsd, settings.VelocityWindow)` returns a trip, record a `NewBreakerTrip` with `Rule = CreditVelocity`, the activity's trace ID, conversation, repository, branch, and root, `TrippedAt`, `Detail = VelocityDetail(trip.WindowCostUsd, settings.VelocityWindowMinutes)`, `WindowCostUsd = trip.WindowCostUsd`, and `WindowMinutes = settings.VelocityWindowMinutes`.
   3. If `FindChurnTrip(activity.ToolCalls, activity.Commits, settings.ChurnFailures)` returns a trip, record a `NewBreakerTrip` with `Rule = RetryChurn`, the same keys, `TrippedAt`, `Detail = ChurnDetail(trip.FailureCount)`, and `FailureCount = trip.FailureCount`.
   4. To record a trip: `id = await store.RecordBreakerTripAsync(newTrip)`. If `id` is null (this rule already tripped on this trace), stop. Otherwise, without reading the trip back, call `sink.Publish(BreakerMessages.ForTrip(id.Value, newTrip.Repository, newTrip.Branch, newTrip.Detail, settings.Notifications))`. Publishing before any other store call means a store exception after the insert can't lose the alert: the retried batch finds the trip already recorded. Then, if `settings.FlagFile` and `newTrip.RepositoryRoot` isn't null, call `CircuitFlagFile.SyncAsync(store, root)`, catching only `IOException` and `UnauthorizedAccessException` and calling `sink.Warn($"could not update {CircuitFlagFile.GetPath(root)}: {ex.Message}")`.
4. For each budget key, in order: read `GetBranchBudgetsAsync(repository, branch)`. If it has one row, that row is `Exceeded`, and its `AlertedAt` is null, call `MarkBranchBudgetAlertedAsync`, whose update re-checks the budget against the spend. If that returns true, read the row again. Skip the alert when the row is gone, isn't `Exceeded`, or has a null `AlertedAt` (the budget was cleared or set again in between; a new budget alerts on a later batch). Otherwise `sink.Publish(BreakerMessages.ForBudget(row, settings.Notifications))`.

The monitor catches nothing else.

### Flag file

`Render(root, trips)` writes this JSON with `Utf8JsonWriter` and `new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }`, then appends `"\n"`. Trips keep the order given (the store's newest-first order). `trippedAt` is `TrippedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)`, `rule` is `BreakerRuleNames.ToName(rule)`, and a null `conversationId` is written as `null`.

```json
{
  "schemaVersion": 1,
  "repositoryRoot": "/home/dev/repo",
  "openTrips": [
    {
      "id": 1,
      "rule": "retry_churn",
      "repository": "swigerb/copilot-ledger",
      "branch": "feature/ledger",
      "conversationId": "982f73ad-18f4-49ef-a1f9-70748fa013fb",
      "traceId": "0a000000000000000000000000000001",
      "trippedAt": "2026-11-21T00:17:17.2600000+00:00",
      "detail": "5 failed tool calls in a row",
      "resetCommand": "gh-ledger breaker reset --id 1"
    }
  ]
}
```

`SyncAsync(store, root)`:

1. If `!Directory.Exists(root)`, return.
2. Let `path = GetPath(root)` and `applied` be unset. Repeat at most three times:
   1. Read `settings = GetBreakerSettingsAsync()`. `trips` is `GetOpenBreakerTripsAsync(new BreakerTripFilter { RepositoryRoot = root })` when `settings.FlagFile`, and empty otherwise. `desired` is null when `trips` is empty, otherwise `Render(root, trips)`.
   2. If `applied` is set and `desired` equals it (ordinal; two nulls are equal), return.
   3. Apply `desired`. For null: if `File.Exists(path)`, `File.Delete(path)`. Otherwise:
      - `Directory.CreateDirectory(Path.Combine(root, DirectoryName))`;
      - if `<root>/.copilot-ledger/.gitignore` doesn't exist, write `*\n`;
      - write `desired` to `temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"`;
      - `File.Move(temp, path, overwrite: true)`.

      If writing `temp` or the move throws `IOException` or `UnauthorizedAccessException`, delete `temp` (ignoring `IOException` and `UnauthorizedAccessException` from that delete) and rethrow. No other catches.

      All files are UTF-8 without a BOM (`new UTF8Encoding(false)`).
   4. Set `applied = desired`.

The `.gitignore` is never deleted.

### Acceptance criteria

- BR1–BR4 and BM1–BM11 pass, and every earlier test passes unchanged, including all `LedgerIngestorTests`, which use the four-argument constructor.
- Replaying live run A trips rule 2 once, at the fifth failing `powershell` call (2026-11-21T00:17:17.260Z), when the sixth export request arrives, and not before.
- The rules are pure: `BreakerRules` reads no clock and does no I/O.
- `src/` contains no `double` or `float`. No new package.

### Verify

```shell
dotnet build CopilotLedger.slnx
dotnet test CopilotLedger.slnx
```

Report the passed, failed, and skipped counts.

## Task 3 specification: Breaker Alerts and the Breaker & Budget Commands

### Files

- `src/CopilotLedger.Cli/Breaker/BreakerCommands.cs` (new, `internal static`): `Create(CliContext context)` yields the `breaker` and `budget` commands.
- `src/CopilotLedger.Cli/Alerts/AlertDispatcher.cs` and `Alerts/DesktopNotifier.cs` (new, `internal`).
- `src/CopilotLedger.Cli/Daemon/DaemonHost.cs`: create the dispatcher and monitor, and pass the monitor to the ingestor.
- `src/CopilotLedger.Cli/GhLedgerCli.cs`: add `foreach (Command command in BreakerCommands.Create(context)) { root.Add(command); }` after the install commands.
- `docs/cli.md`: the "Circuit breaker" and "Budgets" sections below.
- Tests: `TestSupport/CliHarness.cs` and `TestSupport/FakeCliPlatform.cs` (changes below), `TestSupport/BreakerTraces.cs` (adds `TraceVJson`), `Cli/BreakerCommandTests.cs` (BC1–BC6, new), and `Cli/AlertDaemonTests.cs` (AD1–AD3, new).

### Commands in this task

| Command | Options |
|---|---|
| `breaker status` | `--json` |
| `breaker reset` | `--id <id>`, `--all`, `--repo <key>`, `--branch <name>` |
| `breaker config` | `--velocity-credits <credits>`, `--velocity-minutes <minutes>`, `--churn-failures <count>`, `--flag-file <on\|off>`, `--notifications <on\|off>` |
| `budget set <credits>` | `--repo <key>`, `--branch <name>` |
| `budget clear` | `--repo <key>`, `--branch <name>` |
| `budget list` | `--json` |

Descriptions: `breaker` "Show and control the runaway-loop circuit breaker.", `breaker status` "Show the breaker's state, rules, and open trips.", `breaker reset` "Close open trips.", `breaker config` "Show or change the breaker's rules.", `budget` "Set soft AI-credit budgets for branches.", `budget set` "Set the current branch's budget.", `budget clear` "Remove the current branch's budget.", and `budget list` "List branch budgets and their spend." Every command opens `new SqliteLedgerStore(context.DatabasePath, context.TimeProvider)` and calls `InitializeAsync`, like the report commands.

Every option value is a `string` parsed by the command, except `--id` (`Option<long?>`), `--all` and `--json` (`Option<bool>`), and the integer options (`Option<int?>`). Validation errors are added with `result.AddError(message)` in a validator, so the exit code is 1 and the message is on `Error`:

| Check | Message |
|---|---|
| `--velocity-credits`: `decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out d)`, `1 <= d <= 1000000`, `d % 0.01m == 0m` | `--velocity-credits must be a number from 1 to 1000000 with at most 2 decimal places.` |
| `--velocity-minutes` from 1 to 1440 | `--velocity-minutes must be from 1 to 1440.` |
| `--churn-failures` from 2 to 100 | `--churn-failures must be from 2 to 100.` |
| `--flag-file` is `on` or `off` (ordinal) | `--flag-file must be on or off.` |
| `--notifications` is `on` or `off` (ordinal) | `--notifications must be on or off.` |
| `budget set <credits>`: the same parse, `0 < d <= 1000000`, `d % 0.01m == 0m` | `Credits must be a number greater than 0 and at most 1000000 with at most 2 decimal places.` |
| `breaker reset`: more than one of `--id`, `--all`, and (`--repo` or `--branch`) | `Use only one of --id, --all, or --repo with --branch.` |
| `breaker reset`, `budget set`, `budget clear`: exactly one of `--repo` and `--branch` | `--repo and --branch must be used together.` |

**Scope.** `breaker reset` with no option, `budget set`, and `budget clear` without `--repo` and `--branch` use the current branch: `new FileSystemGitContextResolver().Resolve(context.WorkingDirectory)`, with the repository `RepositorySlug ?? RepositoryRoot` and the branch `BranchName ?? "(none)"`. `Resolve` returns null outside a repository and catches errors from reading files, but not from the path APIs it calls: `Path.GetFullPath` throws `ArgumentException` for a `gitdir:` line with a null character (verified on .NET 10.0.15). So the commands catch only `IOException`, `UnauthorizedAccessException`, and `ArgumentException` from `Resolve`, the three that `LedgerIngestor` catches, and treat them as outside a repository. Outside a Git repository they write one line to `Error` and return 1: `Not in a Git repository. Pass --repo and --branch, --id, or --all.` for `breaker reset`, and `Not in a Git repository. Pass --repo and --branch.` for the budget commands.

### breaker status

Text output, with `<rules>` = `BreakerMessages.RulesSummary(settings)` and `<on|off>` from the settings:

```text
Circuit breaker: closed
Rules: <rules>
Flag file: <on|off>. Notifications: <on|off>.
```

When `GetOpenBreakerTripsAsync(BreakerTripFilter.All)` returns `n` trips, the first line is `Circuit breaker: open (<n> trip)` for 1 and `Circuit breaker: open (<n> trips)` otherwise. Then come the rules and flag lines, an empty line, two lines per trip in the store's order, and a final line:

```text
Trip <id> at <LedgerFormat.LocalTime(TrippedAt, context.TimeProvider.LocalTimeZone)>: <repository> @ <branch>: <detail> on one prompt
  rule <rule name>, trace <trace id>, session <conversation id or (none)>
Reset a trip with: gh-ledger breaker reset --id <id>
```

The last line is literal: it shows `<id>`, not a number.

`--json` writes this object with `ReportCommands`' writer options (`Indented = true`, `UnsafeRelaxedJsonEscaping`; declare an identical `JsonOptions` field in `BreakerCommands`), `LedgerFormat.WriteMoney`, and `LedgerFormat.WriteTime`:

```json
{
  "state": "open",
  "settings": {
    "velocityCredits": 150,
    "velocityWindowMinutes": 5,
    "churnFailures": 5,
    "flagFile": false,
    "notifications": true
  },
  "openTrips": [
    {
      "id": 1,
      "rule": "credit_velocity",
      "repository": "swigerb/copilot-ledger",
      "branch": "feature/ledger",
      "repositoryRoot": null,
      "conversationId": null,
      "traceId": "11111111111111111111111111111111",
      "trippedAt": "2026-11-21T10:03:00.0000000+00:00",
      "detail": "180.00 AI credits in 5 minutes",
      "windowAiCredits": 180,
      "windowMinutes": 5,
      "failureCount": null,
      "recordedAt": "2026-11-21T12:00:00.0000000+00:00",
      "notifiedAt": null
    }
  ]
}
```

`state` is `closed` with an empty `openTrips`. `velocityCredits` and `windowAiCredits` (`WindowCostUsd / 0.01`, or `null`) use `WriteMoney`; the other numbers are integers or `null`.

### breaker reset

The filter is `{ Id = id }` for `--id`, `BreakerTripFilter.All` for `--all`, and `{ Repository, Branch }` otherwise. Call `ResetBreakerTripsAsync`, then write `Reset 1 trip.`, `Reset <n> trips.`, or `No open trips to reset.` and return 0. Then, for each distinct non-null `RepositoryRoot` of the reset trips (ordinal order), call `CircuitFlagFile.SyncAsync(store, root)`. Catch only `IOException` and `UnauthorizedAccessException`, write `Warning: could not update <path>: <message>` to `Error`, and still return 0.

### breaker config

With no option, write the `Rules:` and `Flag file:` lines exactly as in `breaker status`. With options, apply them to the current settings (`settings with { … }`), call `UpdateBreakerSettingsAsync`, and write `Updated the circuit breaker settings.` followed by the two lines with the new values. Whenever `--flag-file` is given, even with the value it already had, sync the flag file (as `breaker reset` does, with the same warning handling) for each distinct non-null `RepositoryRoot` of `GetOpenBreakerTripsAsync(BreakerTripFilter.All)`. So `--flag-file on` also repairs a flag file the daemon couldn't write.

### budget set, clear, and list

- `budget set <credits>`: `SetBranchBudgetAsync(repository, branch, credits)`, then read the row back and write `Budget for <repository> @ <branch>: <Credits(budget)> AI credits. Spent so far: <Credits(spent)>.` When the row is `Exceeded`, add the line `This branch is already over its budget.`
- `budget clear`: `Cleared the budget for <repository> @ <branch>.` when `ClearBranchBudgetAsync` returns true; otherwise `No budget is set for <repository> @ <branch>.`
- `budget list`: `No branch budgets are set.` when there are none; otherwise one line per row, `<repository> @ <branch>: <Credits(spent)> of <Credits(budget)> AI credits`, followed by ` (over budget)` when it's `Exceeded`.
- `budget list --json`, with the same writer options:

```json
{
  "budgets": [
    {
      "repository": "contoso/api",
      "branch": "main",
      "budgetAiCredits": 40.5,
      "spentAiCredits": 31.5,
      "exceeded": false,
      "setAt": "2026-11-21T12:00:00.0000000+00:00",
      "alertedAt": null
    }
  ]
}
```

`Credits` here is `BreakerMessages.Credits`.

### Alert dispatcher

```csharp
namespace CopilotLedger.Cli.Alerts;

internal sealed class AlertDispatcher : IBreakerAlertSink
{
    // log writes one daemon log line. Throws ArgumentNullException for null arguments.
    public AlertDispatcher(CliContext context, SqliteLedgerStore store, Action<string> log);

    public void Publish(BreakerAlert alert);           // Channel.CreateUnbounded<BreakerAlert>(new UnboundedChannelOptions { SingleReader = true }); TryWrite
    public void Warn(string message);                  // log("Warning: " + message), catching only IOException (the line is dropped), so it never throws into the monitor

    // Publishes, oldest first, ForTrip(trip.Id, trip.Repository, trip.Branch, trip.Detail, notifications) for each trip from
    // GetUnnotifiedBreakerTripsAsync(now - 1 hour), then ForBudget(row, notifications) for each row from GetUnnotifiedBudgetAlertsAsync(now - 1 hour),
    // where now is context.TimeProvider.GetUtcNow() and notifications is GetBreakerSettingsAsync().Notifications.
    public Task EnqueuePendingAsync(CancellationToken cancellationToken);

    // Handles alerts until Complete() has been called and the channel is empty. Cancellation stops it (OperationCanceledException).
    public Task RunAsync(CancellationToken cancellationToken);

    public void Complete();                            // Writer.TryComplete()
}
```

For each alert, `RunAsync`:

1. calls `log($"ALERT {alert.Title}: {alert.Body}")`;
2. if `alert.ShowNotification`, awaits `DesktopNotifier.ShowAsync(context, alert.Title, alert.Body, log, cancellationToken)`;
3. records the notification: `MarkBreakerTripNotifiedAsync(alert.TripId!.Value)` for a trip, or `MarkBranchBudgetNotifiedAsync(alert.Repository, alert.Branch)` for a budget. It catches only `SqliteException` here, logging `Warning: could not record the notification: <message>`.

Step 3 runs even when the notification failed or was skipped, so a restart doesn't repeat it.

Each alert is handled inside its own `try`, which catches only `IOException`: `log` throws it when `daemon.log` can't be written, for example on a full disk. The failing alert's remaining steps are skipped, so it stays unnotified and is re-sent at the next start if it's less than an hour old, and the next alert is handled normally. Any other exception ends `RunAsync` and faults its task, which `DaemonHost` logs without rethrowing (see Daemon changes).

### Desktop notifier

`internal static class DesktopNotifier` has `Task ShowAsync(CliContext context, string title, string body, Action<string> log, CancellationToken cancellationToken)`. It chooses one command from `context.Platform`:

| Platform | File name | Arguments |
|---|---|---|
| `IsWindows` | `<SystemRoot>\System32\WindowsPowerShell\v1.0\powershell.exe` | `-NoProfile`, `-NonInteractive`, `-Command`, `<script>` |
| `IsMacOS` | `/usr/bin/osascript` | `-e`, `on run argv`, `-e`, `display notification (item 2 of argv) with title (item 1 of argv)`, `-e`, `end run`, `<title>`, `<body>` |
| otherwise | `FindOnPath("notify-send")` | `--app-name=CopilotLedger`, `<title>`, `<body>` |

- `<SystemRoot>` is `context.Platform.GetEnvironmentVariable("SystemRoot")` with trailing `\` removed, or `C:\Windows` when that's null or empty. Never read it with `Environment.GetEnvironmentVariable`: tests set it through `FakeCliPlatform`. Join it with string concatenation, not `Path.Combine`, so the expected value is the same when tests run on Linux.
- On Windows, first write `Path.Combine(context.DataDirectory, "alert-toast.xml")`, UTF-8 without a BOM:

  ```xml
  <toast><visual><binding template="ToastGeneric"><text>TITLE</text><text>BODY</text></binding></visual></toast>
  ```

  `TITLE` and `BODY` are `SecurityElement.Escape(title)` and `SecurityElement.Escape(body)`, and the file has no line break. `<script>` is this single line, with `XMLPATH` replaced by the XML file's path in which each PowerShell single-quote character is doubled. PowerShell treats five characters as single quotes: `'` (U+0027), `‘` (U+2018), `’` (U+2019), `‚` (U+201A), and `‛` (U+201B). Write the last four as `\u2018`, `\u2019`, `\u201A`, and `\u201B` in C#.

  ```text
  $ErrorActionPreference='Stop'; [void][Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]; [void][Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom,ContentType=WindowsRuntime]; $x=New-Object Windows.Data.Xml.Dom.XmlDocument; $x.LoadXml([IO.File]::ReadAllText('XMLPATH')); $t=[Windows.UI.Notifications.ToastNotification]::new($x); $t.Tag='copilot-ledger'; $t.Group='alerts'; [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe').Show($t)
  ```

  Keep it as a C# raw string literal on one line (`"""…"""`), not an interpolated one, and use `Replace("XMLPATH", …, StringComparison.Ordinal)`. The tag and group make a new alert replace the previous one in the notification center.
- On Linux, when `FindOnPath` returns null, log `Notification skipped: notify-send was not found.` and return.

Run the command with `context.Platform.RunAsync(fileName, arguments, timeout.Token)`, where `using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);` and `timeout.CancelAfter(TimeSpan.FromSeconds(15));`. `CancelAfter` runs on the wall clock, not `context.TimeProvider`, so a test's `ManualTimeProvider` doesn't affect it. Then:

- an exit code other than 0 logs `Notification failed: <file name> exited with code <n>: <trimmed standard error>`, or `Notification failed: <file name> exited with code <n>.` when standard error is blank;
- `OperationCanceledException` when `cancellationToken` isn't cancelled logs `Notification timed out after 15 seconds.`;
- `Win32Exception`, `IOException`, `UnauthorizedAccessException`, and `InvalidOperationException`, including from writing the XML file, log `Notification failed: <exception message>`.

No other catches; an `OperationCanceledException` from `cancellationToken` propagates.

### Daemon changes

In `DaemonHost.RunAsync`:

1. Wrap the log writer: `TextWriter writer = TextWriter.Synchronized(CreateLogWriter(context, logFile, out bool ownsWriter));`, because the dispatcher writes from another thread. Disposal is unchanged.
2. Before the `try`, next to `WebApplication? app = null;`, declare `AlertDispatcher? dispatcher = null;`, `Task? dispatching = null;`, and `CancellationTokenSource dispatchStop = new();`, because `finally` uses them.
3. After pruning `span_links`, set `dispatcher = new AlertDispatcher(context, store, message => WriteLog(writer, context, message));`, create `CircuitBreakerMonitor monitor = new(store, dispatcher);`, and pass `monitor` to the five-argument `LedgerIngestor` constructor.
4. Before `app.StartAsync`, `await dispatcher.EnqueuePendingAsync(cancellationToken);`, then `dispatching = dispatcher.RunAsync(dispatchStop.Token);`. Enqueuing before the receiver starts means a trip can't be both re-sent and published live.
5. In `finally`, right after the `app.StopAsync` block, when `dispatching` isn't null:
   1. call `dispatcher!.Complete()`;
   2. if `await Task.WhenAny(dispatching, Task.Delay(TimeSpan.FromSeconds(5)))` isn't `dispatching`, call `dispatchStop.Cancel()` and then `await Task.WhenAny(dispatching, Task.Delay(TimeSpan.FromSeconds(1)));`;
   3. if `dispatching.IsFaulted`, log `Warning: the alert dispatcher stopped: <dispatching.Exception!.GetBaseException().Message>`.

   `Task.WhenAny` never throws, so `finally` doesn't rethrow anything from the dispatcher. Both delays run on the wall clock, not `context.TimeProvider`, and together stay well inside `daemon stop`'s 15-second timeout. Then, whether or not `dispatching` was started, dispose `dispatchStop`. Alerts still queued stay unnotified and are re-sent at the next start if they're less than an hour old.

### docs/cli.md changes

Add these two sections after "Reports":

```markdown
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
```

### Test support changes

- `FakeCliPlatform.RunAsync`: wrap the `ProcessRuns.Add` in `lock (ProcessRuns)`, because the daemon's dispatcher calls it from another thread. Tests read `ProcessRuns` only after the daemon has stopped.
- `CliHarness`:
  - Its `ConfigureWebHost` becomes `builder => { builder.UseTestServer(); builder.ConfigureServices(services => services.AddHostedService(provider => new TestServerCapture(provider.GetRequiredService<IServer>(), server => _daemonServer = server))); }`, where `TestServerCapture` is a private nested `sealed class` implementing `IHostedService`. Its `StartAsync` passes `(TestServer)server` to the callback and returns `Task.CompletedTask`, and its `StopAsync` returns `Task.CompletedTask`.
  - `public HttpClient CreateDaemonClient()` returns `_daemonServer.CreateClient()`, or throws `InvalidOperationException("No in-process daemon is running.")` when it's null.
  - `public string DaemonOutput => InProcessDaemon is { IsCompletedSuccessfully: true } run ? run.Result.Out : throw new InvalidOperationException("The in-process daemon hasn't finished.");`. The daemon's log is its `CliRun.Out`: `InvokeAsync` replaces the `Out` that `StartInProcessDaemonAsync` and `StartDetachedHandler` set with its own writer, so `_daemonOut` stays empty. Tests read `DaemonOutput` only after awaiting `InProcessDaemon`, and only for a daemon started with `StartInProcessDaemonAsync()`, whose log goes to `Out` because it has no `--log-file`.

### Acceptance criteria

- BC1–BC6 and AD1–AD3 pass, and every earlier test passes unchanged.
- No test shows a real notification, starts a process, or opens a socket: notifications go through `FakeCliPlatform.RunAsync`, and the daemon uses `TestServer`.
- `src/` contains no `double` or `float`. No new package.

### Verify

```shell
dotnet build CopilotLedger.slnx
dotnet test CopilotLedger.slnx
```

Report the passed, failed, and skipped counts.

## Task 4 specification: MCP Server

### Files

- `src/CopilotLedger.Mcp/CopilotLedger.Mcp.csproj` (new): references `ModelContextProtocol` 2.4.0 and the Core and Storage projects:

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
      <PackageReference Include="ModelContextProtocol" Version="2.4.0" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\CopilotLedger.Core\CopilotLedger.Core.csproj" />
      <ProjectReference Include="..\CopilotLedger.Storage\CopilotLedger.Storage.csproj" />
    </ItemGroup>
  </Project>
  ```

- `src/CopilotLedger.Mcp/LedgerMcpContext.cs` and `LedgerMcpServer.cs` (public), and `LedgerMcpTools.cs` (internal: the schemas, argument checks, and results).
- `CopilotLedger.slnx`: add `<Project Path="src/CopilotLedger.Mcp/CopilotLedger.Mcp.csproj" />` to `/src/`, between Core and Otlp.
- `src/CopilotLedger.Cli/CopilotLedger.Cli.csproj` and `tests/CopilotLedger.Tests/CopilotLedger.Tests.csproj`: add a `ProjectReference` to the Mcp project.
- `src/CopilotLedger.Cli/CliContext.cs`: two optional streams. `src/CopilotLedger.Cli/Mcp/McpCommand.cs` (new, `internal static`) and `GhLedgerCli.cs`: the `mcp` command.
- `docs/cli.md`: the "MCP server" section below.
- Tests: `TestSupport/McpTestHarness.cs` (new) and `Mcp/LedgerMcpServerTests.cs` (MC1–MC12, new).

### Contracts

```csharp
namespace CopilotLedger.Mcp;

public sealed record LedgerMcpContext
{
    public required string DatabasePath { get; init; }
    public required string WorkingDirectory { get; init; }                 // the server process's working directory
    public required Func<string, string?> GetEnvironmentVariable { get; init; }
    public required IGitContextResolver GitResolver { get; init; }
    public required TimeProvider TimeProvider { get; init; }               // the store's clock
    public required string Version { get; init; }                          // serverInfo.version
}

public static class LedgerMcpServer
{
    public const string ServerName = "copilot-ledger";
    public const string Instructions =
        "CopilotLedger reports the AI spend that Copilot recorded on this machine, by Git branch. " +
        "Tools use the current repository and branch unless you pass repository and branch. " +
        "When a result starts with WARNING, stop and tell the user before doing more work.";

    // ServerInfo = { Name = ServerName, Version = context.Version }, ServerInstructions = Instructions, and the two handlers.
    public static McpServerOptions CreateOptions(LedgerMcpContext context);

    // await using McpServer server = McpServer.Create(new StreamServerTransport(input, output, ServerName), CreateOptions(context));
    // then await server.RunAsync(cancellationToken). McpServer is IAsyncDisposable.
    // Returns when input ends or cancellationToken is cancelled. Writes nothing else to output.
    public static Task RunAsync(LedgerMcpContext context, Stream input, Stream output, CancellationToken cancellationToken = default);
}
```

Add to `CliContext`:

```csharp
public Stream? StandardInput { get; init; }            // mcp: null uses Console.OpenStandardInput()
public Stream? StandardOutput { get; init; }           // mcp: null uses Console.OpenStandardOutput()
```

`gh-ledger mcp` ("Run the CopilotLedger MCP server on standard input and output.") has no options. It runs `LedgerMcpServer.RunAsync` with `DatabasePath = context.DatabasePath`, `WorkingDirectory = context.WorkingDirectory`, `GetEnvironmentVariable = context.Platform.GetEnvironmentVariable`, `GitResolver = new FileSystemGitContextResolver()`, `TimeProvider = context.TimeProvider`, and `Version = GhLedgerCli.Version`, then returns 0. It never writes to `context.Out`, because standard output carries the protocol.

### Tools

`tools/list` returns these four tools in this order. Every `inputSchema` is parsed once from a constant string and cloned (`JsonDocument.Parse(…).RootElement.Clone()`).

| Name | Title | Annotations |
|---|---|---|
| `get_branch_ledger` | `Branch ledger` | `ReadOnlyHint = true`, `OpenWorldHint = false` |
| `get_top_expensive_prompts` | `Top expensive prompts` | `ReadOnlyHint = true`, `OpenWorldHint = false` |
| `get_agent_breakdown` | `Agent breakdown` | `ReadOnlyHint = true`, `OpenWorldHint = false` |
| `set_branch_budget` | `Set branch budget` | `ReadOnlyHint = false`, `DestructiveHint = false`, `IdempotentHint = true`, `OpenWorldHint = false` |

Descriptions:

- `get_branch_ledger`: `Spend on one Git branch: AI credits, USD, BYOK USD, local savings, top model, budget, and circuit-breaker status. Uses the current repository and branch unless you pass both.`
- `get_top_expensive_prompts`: `The most expensive prompt chains on one Git branch, with their output tokens and failed tool calls (retry loops). Uses the current repository and branch unless you pass both. Returns 5 by default, ordered by cost.`
- `get_agent_breakdown`: `Spend on one Git branch split by agent (gen_ai.agent.name). Uses the current repository and branch unless you pass both.`
- `set_branch_budget`: `Sets a soft AI-credit budget for one Git branch. CopilotLedger alerts once when the branch's billed spend exceeds it; nothing is blocked. Uses the current repository and branch unless you pass both.`

`get_branch_ledger` and `get_agent_breakdown` share this schema:

```json
{
  "type": "object",
  "properties": {
    "repository": { "type": "string", "minLength": 1, "description": "Repository key: owner/repo, a repository root, or (none). Pass with branch." },
    "branch": { "type": "string", "minLength": 1, "description": "Branch name, or (none). Pass with repository." }
  },
  "additionalProperties": false
}
```

`get_top_expensive_prompts`:

```json
{
  "type": "object",
  "properties": {
    "repository": { "type": "string", "minLength": 1, "description": "Repository key: owner/repo, a repository root, or (none). Pass with branch." },
    "branch": { "type": "string", "minLength": 1, "description": "Branch name, or (none). Pass with repository." },
    "limit": { "type": "integer", "minimum": 1, "maximum": 20, "default": 5, "description": "Number of prompt chains." },
    "orderBy": { "type": "string", "enum": ["cost", "outputTokens", "failedToolCalls"], "default": "cost", "description": "Sort key, highest first." }
  },
  "additionalProperties": false
}
```

`set_branch_budget`:

```json
{
  "type": "object",
  "properties": {
    "repository": { "type": "string", "minLength": 1, "description": "Repository key: owner/repo, a repository root, or (none). Pass with branch." },
    "branch": { "type": "string", "minLength": 1, "description": "Branch name, or (none). Pass with repository." },
    "aiCredits": { "type": "number", "exclusiveMinimum": 0, "maximum": 1000000, "description": "Budget in AI credits (1 AI credit = $0.01), with at most 2 decimal places." }
  },
  "required": ["aiCredits"],
  "additionalProperties": false
}
```

### Handling a call

1. An unknown tool name throws `new McpProtocolException($"Unknown tool: {name}", McpErrorCode.InvalidParams)`.
2. Check the arguments (null means none), stopping at the first failure. The SDK answers a request whose argument names or values contain an escaped lone surrogate with JSON-RPC error `-32603` before the handler runs (Verified facts), so these checks needn't handle unreadable strings. A failure returns `isError: true` with one `TextContentBlock` holding the message, and no `structuredContent`:

   | Check | Message |
   |---|---|
   | Every argument name is one of the tool's properties; report the ordinal-first unknown name | `Unknown argument: <name>.` |
   | `repository`, when present, is a JSON string that isn't blank | `repository must be a non-empty string.` |
   | `branch`, when present, is a JSON string that isn't blank | `branch must be a non-empty string.` |
   | Both or neither of `repository` and `branch` | `Pass repository and branch together, or neither.` |
   | `limit`, when present, is a number with `TryGetInt32` from 1 to 20 | `limit must be an integer from 1 to 20.` |
   | `orderBy`, when present, is the string `cost`, `outputTokens`, or `failedToolCalls` (ordinal) | `orderBy must be cost, outputTokens, or failedToolCalls.` |
   | `aiCredits` is present | `aiCredits is required.` |
   | `aiCredits` is a number with `TryGetDecimal`, `0 < d <= 1000000`, `d % 0.01m == 0m` | `aiCredits must be a number greater than 0 and at most 1000000, with at most 2 decimal places.` |

3. Resolve the scope. Without `repository` and `branch`: the directory is `context.GetEnvironmentVariable("COPILOT_SESSION_CWD")` when it isn't blank, `Path.IsPathFullyQualified`, and `Directory.Exists`; otherwise `context.WorkingDirectory`. `GitResolver.Resolve(directory)` gives the repository `RepositorySlug ?? RepositoryRoot` and the branch `BranchName ?? "(none)"`. Catch only `IOException`, `UnauthorizedAccessException`, and `ArgumentException` from `Resolve` (as `LedgerIngestor` does) and treat them like a null result. A null result returns `isError` with `No Git repository at <directory>. Pass repository and branch.`
4. Open the store: `new SqliteLedgerStore(context.DatabasePath, context.TimeProvider)` and `InitializeAsync`, once per server, guarded by a `SemaphoreSlim(1, 1)`; a failed open is retried on the next call. Steps 4 and 5 catch only `SqliteException`, `IOException`, `UnauthorizedAccessException`, and `InvalidOperationException`, returning `isError` with `The ledger at <DatabasePath> is unavailable: <exception message>`.
5. Build the result object below. Return `StructuredContent` = the object, and one `TextContentBlock` whose text is each warning followed by `\n`, then the object as compact JSON (`new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }`, not indented). `IsError` stays null.

### Results

Every result ends with `circuitBreaker` and `warnings`:

- **Trips.** `GetOpenBreakerTripsAsync(BreakerTripFilter.All)`, keeping, in that order, the trips whose `Repository` and `Branch` equal the scope's, or whose `ConversationId` equals the session ID. The session ID is `context.GetEnvironmentVariable("COPILOT_AGENT_SESSION_ID")` when it isn't blank; otherwise there's none, and no trip matches by conversation.
- **Budget.** `GetBranchBudgetsAsync(repository, branch)`: zero or one row.
- **`circuitBreaker`** is `{ "state": "open" | "closed", "openTrips": [ … ] }`, where each trip is `{ "id", "rule", "repository", "branch", "traceId", "conversationId", "currentSession", "trippedAt", "detail", "resetCommand" }`. `currentSession` is true when the trip's conversation equals the session ID, and `resetCommand` is `gh-ledger breaker reset --id <id>`.
- **`warnings`** holds one line per kept trip, `WARNING: CopilotLedger circuit breaker is open for <repository> @ <branch>: <detail> on one prompt (trip <id>). Stop and ask the user before continuing. The user can reset it with: gh-ledger breaker reset --id <id>`, then, when the budget is exceeded, `WARNING: <repository> @ <branch> has spent <Credits(spent)> of its <Credits(budget)> AI credit budget.` `Credits` rounds to 2 places with `MidpointRounding.AwayFromZero` and formats `N2` with the invariant culture.
- **`budget`** is `null` or `{ "aiCredits", "spentAiCredits", "exceeded" }`.

Money is written raw with `value.ToString("0.############", CultureInfo.InvariantCulture)`, as `LedgerFormat.WriteMoney` does; times as `ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)`. `billedAiCredits` is `AiCredits + FoundryByokUsd / 0.01`.

| Tool | Object (properties in this order) |
|---|---|
| `get_branch_ledger` | `repository`, `branch`, `spans`, `unpricedSpans`, `inputTokens`, `outputTokens`, `cacheReadInputTokens`, `cacheWriteInputTokens`, `aiCredits`, `aiCreditsUsd`, `foundryByokUsd`, `localSavedUsd`, `billedAiCredits`, `topModel`, `budget`, `circuitBreaker`, `warnings`. The totals are the single row of `GetRollupAsync(LedgerGrouping.Branch, new LedgerFilter { Repository, Branch })`, or all zeros when there's no row. `topModel` is the first row's `Model` from the Model rollup with the same filter, or `null`. |
| `get_top_expensive_prompts` | `repository`, `branch`, `orderBy`, `limit`, `prompts`, `circuitBreaker`, `warnings`. Each prompt is `rank`, `traceId`, `conversationId`, `startTime`, `endTime`, `agents`, `models`, `spans`, `unpricedSpans`, `inputTokens`, `outputTokens`, `cacheReadInputTokens`, `cacheWriteInputTokens`, `aiCredits`, `aiCreditsUsd`, `foundryByokUsd`, `localSavedUsd`, `billedAiCredits`, `failedToolCalls`. |
| `get_agent_breakdown` | `repository`, `branch`, `agents`, `circuitBreaker`, `warnings`. Each agent row of `GetRollupAsync(LedgerGrouping.Agent, filter)`, in its order, is `agent`, `spans`, `unpricedSpans`, `inputTokens`, `outputTokens`, `cacheReadInputTokens`, `cacheWriteInputTokens`, `aiCredits`, `aiCreditsUsd`, `foundryByokUsd`, `localSavedUsd`, `billedAiCredits`. |
| `set_branch_budget` | `repository`, `branch`, `budget`, `circuitBreaker`, `warnings`, after `SetBranchBudgetAsync(repository, branch, aiCredits)`. |

`get_top_expensive_prompts` takes the candidates from `GetTopPromptChainsAsync(new LedgerFilter { Repository, Branch }, 1000)` and their counts from `GetFailedToolCallCountsAsync` (0 when absent). It sorts them by the `orderBy` key, highest first: billed USD (`AiCreditsUsd + FoundryByokUsd`) for `cost`, `OutputTokens`, or the failed count. Ties go to billed USD (descending), then `StartTime` (descending), then `TraceId` (ordinal, ascending). It takes `limit` (default 5) and ranks them from 1. `orderBy` defaults to `cost`, and the result echoes both values.

### docs/cli.md changes

Add this section after "Budgets":

````markdown
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
````

### Acceptance criteria

- MC1–MC12 pass, and every earlier test passes unchanged.
- `tools/list` returns exactly the four tools above, and each `inputSchema` has `"additionalProperties": false`.
- No tool writes anything but `branch_budgets`, and nothing but the MCP protocol is written to standard output.
- `src/` contains no `double` or `float`; `aiCredits` is read with `TryGetDecimal`. The only new package is `ModelContextProtocol` 2.4.0, and the restore has no `NU190x` warning.

### Verify

```shell
dotnet build CopilotLedger.slnx
dotnet test CopilotLedger.slnx
```

Report the passed, failed, and skipped counts.

## Task 5 specification: Start at Sign-in, Version 0.3.0 & CI

### Files

- `src/CopilotLedger.Cli/ICliPlatform.cs`, `SystemCliPlatform.cs`: `GetStartupCommand` and `SetStartupCommand`.
- `src/CopilotLedger.Cli/Install/StartupItem.cs` (new, `internal static`), `InstallCommands.cs`, and `InstallManifest.cs`.
- `src/CopilotLedger.Cli/app.manifest` (new) and `CopilotLedger.Cli.csproj` (`ApplicationManifest`, version 0.3.0).
- `src/CopilotLedger.Cli/GhLedgerCli.cs`: the fallback version and its comment become `0.3.0`.
- `.github/workflows/ci.yml` and `docs/cli.md`.
- Tests: `TestSupport/FakeCliPlatform.cs`; `Cli/InstallCommandTests.cs` (IN1, IN2, IN3, IN9, and IN10 changed; IN11–IN16 new); `Cli/DaemonCommandTests.cs` (DM1's version); and `Cli/ReportCommandTests.cs` (the `status` text's version line).

### Contracts

Add to `ICliPlatform`:

```csharp
string? GetStartupCommand(string name);                // Windows: the HKCU\Software\Microsoft\Windows\CurrentVersion\Run value; null when absent and on other platforms
void SetStartupCommand(string name, string? command);  // Windows: sets that value (REG_SZ), or deletes it when command is null; other platforms throw PlatformNotSupportedException
```

`SystemCliPlatform` implements them with `Microsoft.Win32.Registry.CurrentUser`: `OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false)` and `GetValue(name) as string` to read; `CreateSubKey(…, writable: true)`, then `SetValue(name, command, RegistryValueKind.String)` or `DeleteValue(name, throwOnMissingValue: false)` to write. Every registry call is inside `if (OperatingSystem.IsWindows())`.

Add to `InstallManifest`, not `required`, so 0.2.0 manifests still load:

```csharp
public string? StartupItem { get; init; }              // the registered sign-in item: the Run value's full name on Windows, the file path elsewhere; null when none
```

`FakeCliPlatform` gains `public Dictionary<string, string> StartupCommands { get; } = new(StringComparer.Ordinal);`, `public List<(string Name, string? Command)> StartupCommandWrites { get; } = [];`, and `public Exception? StartupCommandException { get; set; }`. `GetStartupCommand` reads `StartupCommands`. `SetStartupCommand` throws `StartupCommandException` when it isn't null, without recording anything; otherwise it appends to `StartupCommandWrites`, then sets or removes the entry.

### Sign-in items

`StartupItem.Plan(CliContext context, string executable)` returns the item for this platform: its identifier, its expected content, and a skip warning when it can't be registered. `<exe>` is `context.Platform.ProcessPath`, and `<home>` is `context.Platform.HomeDirectory`.

| Platform | Identifier (`InstallManifest.StartupItem`) | Content | Skipped when |
|---|---|---|---|
| `IsWindows` | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CopilotLedger` | Run value `CopilotLedger` = `"<exe>" daemon start` | the command is longer than 260 characters |
| `IsMacOS` | `Path.Combine(<home>, "Library", "LaunchAgents", "io.github.swigerb.copilot-ledger.plist")` | the property list below | never |
| otherwise | `Path.Combine(<config>, "autostart", "copilot-ledger.desktop")` | the desktop entry below | `<exe>` contains a double quote, a backtick, `$`, `\`, `%`, `\n`, or `\r` |

`<config>` is `context.Platform.GetEnvironmentVariable("XDG_CONFIG_HOME")` when it isn't blank and `Path.IsPathFullyQualified`; otherwise `Path.Combine(<home>, ".config")`. Read every variable through `context.Platform`, never `Environment.GetEnvironmentVariable`: GitHub's Ubuntu runners set `XDG_CONFIG_HOME`, so a direct read would make tests write the runner's real autostart folder.

The property list, where `EXE` is `SecurityElement.Escape(<exe>)` and `LOG` is `SecurityElement.Escape(Path.Combine(context.DataDirectory, "daemon.log"))`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>io.github.swigerb.copilot-ledger</string>
  <key>ProgramArguments</key>
  <array>
    <string>EXE</string>
    <string>daemon</string>
    <string>run</string>
    <string>--log-file</string>
    <string>LOG</string>
  </array>
  <key>RunAtLoad</key>
  <true/>
</dict>
</plist>
```

The desktop entry, where `EXE` is `<exe>`:

```ini
[Desktop Entry]
Type=Application
Name=CopilotLedger
Comment=Starts the CopilotLedger daemon
Exec="EXE" daemon start
Terminal=false
```

Both files use `\n` line breaks, end with `\n`, and are UTF-8 without a BOM. Build them from raw string literals with `.ReplaceLineEndings("\n")`, because the repository's checkouts use `core.autocrlf=true`.

### install

Insert this step after the `sh.exe` warning and before the manifest is written. (`InstallAsync` writes `install.json` before it calls `DaemonCommands.StartAsync`, so the step must come before the manifest, not just before the daemon.) The step computes `startupItem`, which starts as the previous manifest's `StartupItem` (null without one); the `InstallManifest` initializer sets `StartupItem = startupItem`. A dry run writes no manifest.

1. If `context.Platform.GetEnvironmentVariable("COPILOT_LEDGER_HOME")` isn't blank, write `Warning: COPILOT_LEDGER_HOME is set. The daemon that starts when you sign in uses it only if your sign-in environment sets it too.`
2. If the item is skipped, write its warning, leave `startupItem` unchanged, and skip steps 3–5:
   - Windows: `Warning: the gh-ledger path is too long for a sign-in command, so gh-ledger won't start when you sign in. Run gh-ledger daemon start after you sign in.`
   - Linux: `Warning: the gh-ledger path has characters that a desktop autostart entry can't run, so gh-ledger won't start when you sign in. Run gh-ledger daemon start after you sign in.`
3. If the item already has exactly the expected content (the Run value equals the command, ordinal; or the file's text equals the content, ordinal), write `gh-ledger already starts when you sign in.`
4. Otherwise, in a dry run, write `Would start gh-ledger when you sign in.` Otherwise register it: `SetStartupCommand("CopilotLedger", command)` on Windows, or `Directory.CreateDirectory` for the file's folder and `File.WriteAllTextAsync` with `new UTF8Encoding(false)` elsewhere. Then write `gh-ledger will start when you sign in.`
5. When the item was registered or already present (and this isn't a dry run), set `startupItem` to its identifier.

Steps 3–5 run inside one `try` that catches only `IOException`, `UnauthorizedAccessException`, and `System.Security.SecurityException` (the registry throws the last). On one of those, write `Warning: could not register the sign-in item: <exception message>`, leave `startupItem` unchanged, and continue the install. No other catches.

### uninstall

Insert this step right after the daemon is stopped. When `manifest.StartupItem` isn't null and the item exists (`GetStartupCommand("CopilotLedger")` isn't null on Windows, or `File.Exists(StartupItem)` elsewhere): in a dry run write `Would stop starting gh-ledger when you sign in.`; otherwise delete it (`SetStartupCommand("CopilotLedger", null)` or `File.Delete`) and write `gh-ledger will no longer start when you sign in.` When it doesn't exist, write nothing. The check and the delete run inside one `try` that catches only `IOException`, `UnauthorizedAccessException`, and `System.Security.SecurityException`. On one of those, write `Warning: could not remove the sign-in item: <exception message>` and continue the uninstall. No other catches.

### Application manifest

`src/CopilotLedger.Cli/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="gh-ledger.app"/>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 and Windows 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <!-- Windows 11 24H2 and later: no console window when started outside a terminal -->
      <consoleAllocationPolicy xmlns="http://schemas.microsoft.com/SMI/2024/WindowsSettings">detached</consoleAllocationPolicy>
    </windowsSettings>
  </application>
</assembly>
```

In `CopilotLedger.Cli.csproj`, add `<ApplicationManifest>app.manifest</ApplicationManifest>` to the first `PropertyGroup` and change `<Version>0.2.0</Version>` to `<Version>0.3.0</Version>`.

### CI

In `.github/workflows/ci.yml`, the smoke-test step becomes:

```yaml
      - name: Smoke-test the published binary
        shell: bash
        env:
          COPILOT_LEDGER_HOME: ${{ runner.temp }}/copilot-ledger
        run: |
          test "$(./artifacts/gh-ledger/gh-ledger --version | tr -d '\r')" = "0.3.0"
          ./artifacts/gh-ledger/gh-ledger rates validate
          ./artifacts/gh-ledger/gh-ledger daemon start
          ./artifacts/gh-ledger/gh-ledger status
          ./artifacts/gh-ledger/gh-ledger breaker status
          ./artifacts/gh-ledger/gh-ledger budget list
          request='{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientCapabilities":{}}}}'
          response=$( { echo "$request"; sleep 10; } | ./artifacts/gh-ledger/gh-ledger mcp )
          echo "$response"
          grep -q '"get_branch_ledger"' <<< "$response"
          ./artifacts/gh-ledger/gh-ledger daemon stop
```

On Windows, `--version` ends its line with `\r\n`, and Git Bash's `$(…)` keeps the `\r`, so `tr -d '\r'` removes it before the comparison. The server answers the request, and when `sleep` ends, standard input closes and the server exits 0. Ten seconds covers a cold start of the single-file binary on a CI runner; closing standard input before the server has answered can lose the response. The here-string avoids a pipe into `grep -q`, which would fail under `pipefail` when `grep` exits early. Nothing else in the workflow changes.

### docs/cli.md changes

- **Install:** after "sets the `gh ledger` alias,", add "registers the daemon to start when you sign in,". After the paragraph, add: "The sign-in item is the `CopilotLedger` value under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` on Windows, `~/Library/LaunchAgents/io.github.swigerb.copilot-ledger.plist` on macOS, and `copilot-ledger.desktop` in `$XDG_CONFIG_HOME/autostart` (default `~/.config/autostart`) on Linux."
- **The daemon:** replace "The daemon does not start at login yet. Run `gh-ledger daemon start` after you sign in. Copilot drops telemetry while the daemon is not running." with "After `install`, the daemon starts when you sign in. Copilot drops telemetry while the daemon is not running."
- **Uninstall:** replace the paragraph's first sentence, "`gh-ledger uninstall` reverses the environment variable, profile block, and `gh` alias changes recorded by `install`, restoring the previous environment value when applicable, and stops the daemon if it is running.", with "`gh-ledger uninstall` stops the daemon if it is running, removes the sign-in item, and reverses the environment variable, profile block, and `gh` alias changes recorded by `install`, restoring the previous environment value when applicable."
- **Troubleshooting:** add two items:
  - "**A console window flashes at sign-in.** Windows 11 24H2 and later start `gh-ledger` without a window. Earlier versions of Windows show its window for about a second."
  - "**The daemon started at sign-in uses the default data directory.** If you set `COPILOT_LEDGER_HOME`, set it where your sign-in session sees it, such as a Windows user environment variable."

### Acceptance criteria

- IN1–IN16, DM1, and the `status` text test pass with the changes below, and every other test passes unchanged.
- `gh-ledger --version` prints `0.3.0`, and the CI smoke test passes on `ubuntu-latest` and `windows-latest`, including the MCP `tools/list` check.
- No test touches the real registry, `~/Library`, `~/.config`, or any file outside its `TempDirectory`.
- `src/` contains no `double` or `float`. No new package.

### Verify

```shell
dotnet build CopilotLedger.slnx
dotnet test CopilotLedger.slnx
dotnet publish src/CopilotLedger.Cli/CopilotLedger.Cli.csproj -c Release -r <rid> -o artifacts/gh-ledger
```

Use your machine's runtime identifier. Then run the CI smoke commands against `artifacts/gh-ledger` with `COPILOT_LEDGER_HOME` set to a temporary directory, and report the passed, failed, and skipped counts. Don't run `install` on your machine.

## Test specifications

Shared rules for every task, which extend Sprint 2's:

- **Money.** Assertions use `decimal`. In JSON, compare money by value: read it with `GetDecimal()` or compare whole documents with `JsonNode.DeepEquals`, which compares numbers by value (`2.86` equals `2.860`). Never use `GetDouble`.
- **Public API only.** No `InternalsVisibleTo`. CLI behavior is tested through `GhLedgerCli.InvokeAsync` (`CliHarness`), and the MCP server through the SDK's `McpClient` over in-memory pipes.
- **Isolation.**
  - Each test that writes files gets its own `TempDirectory`, called `<root>`. A path written `<root>/a/b` means `Path.Combine(<root>, "a", "b")`. Build every path in a test with `Path.Combine`; never write `\` or `/` into a path, because CI runs on Linux and Windows.
  - Nothing touches the real registry, `~/.copilot`, `~/.copilot-ledger`, `~/Library`, `~/.config`, gh, user environment variables, or shell profiles. Notifications and processes go through `FakeCliPlatform`; receivers and daemons use `TestServer`; the MCP server uses pipes. No test opens a socket, starts a process, or runs `git`.
- **SQLite and disposal.** As in Sprint 2: keep every database under the test's `TempDirectory`, and declare the `TempDirectory` first so it's disposed last (`using TempDirectory temp = new();`, then the harness, store, or app).
- **Text.**
  - `CliRun.Out` and `CliRun.Error` already have `\r\n` replaced with `\n`. When a case says `Out` *is* a text, compare it with that text plus a final `\n`.
  - Files the product writes with `\n` line breaks (the flag file, sign-in items, the toast XML) are compared exactly, without normalizing.
  - Write multi-line expected text as raw string literals followed by `.ReplaceLineEndings("\n")`, because checkouts use `core.autocrlf=true` and a raw literal takes the file's line endings.
- **JSON.** Compare JSON with `JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected))`. In expected JSON, a path is inserted as `JsonSerializer.Serialize(path, RelaxedJson)`, where `RelaxedJson` is `new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }`, so `\` is escaped on Windows and the text matches files written with that encoder even when the temp path isn't ASCII.
- **Clocks.** `NOW` is `2026-11-21T12:00:00Z` and `T0` is `2026-11-21T10:00:00Z`. "A store at `NOW`" is `new SqliteLedgerStore(<path>, new ManualTimeProvider(NOW))`. `ManualTimeProvider.Advance` moves it.
- **Waiting.** Tests that wait for the daemon use a private `WaitUntilAsync(Func<Task<bool>> condition)` that checks every 25 ms for up to 10 seconds and then throws `TimeoutException`.
- **Git fixture.** `LedgerIngestorTests` keeps its own private fixture, so Task 1 adds a shared one; don't change `LedgerIngestorTests`. `tests/CopilotLedger.Tests/TestSupport/GitFixture.cs`:

  ```csharp
  public static class GitFixture
  {
      public const string Sha1 = "0123456789abcdef0123456789abcdef01234567";
      public const string Sha2 = "89abcdef0123456789abcdef0123456789abcdef";

      // Creates <root>/repo with .git/HEAD "ref: refs/heads/feature/ledger\n", .git/refs/heads/feature/ledger Sha1 + "\n",
      // .git/config "[remote \"origin\"]\n\turl = https://github.com/swigerb/copilot-ledger.git\n", and an empty <root>/repo/src.
      // Returns <root>/repo.
      public static string Create(string root);

      // new GitWorkspaceContext(<root>/repo, <root>/repo/.git, "feature/ledger", Sha1, "https://github.com/swigerb/copilot-ledger.git", "swigerb/copilot-ledger").
      // It creates no files.
      public static GitWorkspaceContext Feature(string root);
  }
  ```

  Below, `SHA1` and `SHA2` are `GitFixture.Sha1` and `GitFixture.Sha2`, `FEATURE` is `GitFixture.Feature(<root>)`, and "the Git fixture" is `GitFixture.Create(<root>)`. BS1 and the MC seeds use `FEATURE`. The BM harness, BC3, BC6, AD1, and the MC harness (MC1–MC12) create the Git fixture.
- **Outside a repository.** BC3 and BC6 expect "not in a Git repository" for `<root>`. The CLI commands resolve Git with `new FileSystemGitContextResolver()`, with no ceiling, as `branch` does, so these cases assume no `.git` directory or file above the system temp directory, as `ReportCommandTests` already does. That holds on developer machines and on GitHub's runners, whose temp directories are outside the checkout. MC9 doesn't depend on it, because the MC context's resolver stops at `<root>`.
- **MCP tool hash.** `HASH` is `c3d1fe831856c77818bb8c1242d32d6efa6c37366eb902e56c5fd0054c1dcbc3/918a44e755cc5eee09b08d214831e8339a645158ef1f831a0fa02bd43999700f`, the `gen_ai.tool.name` of run C's `probe_write`.

### Task 1 tests

#### Protobuf decoding: `OtlpProtobufDecodingTests`

**PB8** (theory over a case name). Decode `OtlpSpan span = Assert.Single(Assert.Single(DecodeSingleSpan(writer).ResourceSpans).Spans);`, where `writer` is `new OtlpProtoWriter().Bytes(2, Convert.FromHexString("eee19b7ec3c1b174"))` followed by the field below. Assert the span's `SpanId` is `eee19b7ec3c1b174` and its `StatusCode`:

| Case | Field after the span ID | `StatusCode` |
|---|---|---|
| `error` | `.Message(15, new OtlpProtoWriter().String(2, "Tool failed").Varint(3, 2))` | 2 |
| `ok` | `.Message(15, new OtlpProtoWriter().Varint(3, 1))` | 1 |
| `empty` | `.Message(15, new OtlpProtoWriter())` | 0 |
| `unknown-code` | `.Message(15, new OtlpProtoWriter().Varint(3, 7))` | 0 |
| `last-wins` | `.Message(15, new OtlpProtoWriter().Varint(3, 2).Varint(3, 1))` | 1 |
| `status-wrong-wire-type` | `.Varint(15, 2)` | 0 |
| `code-wrong-wire-type` | `.Message(15, new OtlpProtoWriter().String(3, "2"))` | 0 |
| `missing` | nothing | 0 |

#### JSON decoding: `OtlpJsonDecodingTests`

**JS10** (theory). Decode `SingleSpan(json)` for this JSON, where `STATUS` is empty in the `missing` case and otherwise `,"status":` followed by the value below:

```text
{"resourceSpans":[{"scopeSpans":[{"spans":[{"traceId":"5b8efff798038103d269b633813fc60c","spanId":"eee19b7ec3c1b174","name":"execute_tool powershell pwsh.exe","startTimeUnixNano":"1795220217552000000"STATUS}]}]}]}
```

Assert `SpanId` is `eee19b7ec3c1b174` and `StatusCode`:

| `status` value | `StatusCode` |
|---|---|
| `{"code":2,"message":"Tool failed"}` | 2 |
| `{"message":"","code":0}` | 0 |
| `{"code":1}` | 1 |
| `{"code":"2"}` | 0 |
| `{"code":"STATUS_CODE_ERROR"}` | 0 |
| `{"code":3}` | 0 |
| `{"code":2.5}` | 0 |
| `{"code":1,"code":2}` | 2 |
| `null` | 0 |
| `"error"` | 0 |
| (missing) | 0 |

#### Normalization: `GenAiSpanNormalizerTests`

**NZ12.** Normalize `Request(CopilotResource, …)` with these 8 spans, in this order. Each uses `Span(spanId: <id>, attributes: [...])` with the other defaults, and `with { StatusCode = <status> }` where a status is given. In the table, `execute_tool` stands for `Str("gen_ai.operation.name", "execute_tool")`:

| # | Span ID | Attributes | Status |
|---|---|---|---|
| 1 | `00000000000000a1` | `Str("gen_ai.operation.name", "execute_tool")`, `Str("gen_ai.tool.name", "powershell")`, `Int("process.exit.code", 1)` | |
| 2 | `00000000000000a2` | `execute_tool`, `Str("gen_ai.tool.name", "apply_patch")` | |
| 3 | `00000000000000a3` | `execute_tool`, `Str("gen_ai.tool.name", HASH)` | 2 |
| 4 | `00000000000000a4` | `execute_tool`, `Str("gen_ai.tool.name", "powershell")`, `Int("process.exit.code", -1073741510)` | |
| 5 | `00000000000000a5` | `execute_tool`, `Str("gen_ai.tool.name", "powershell")`, `Str("process.exit.code", "1")` | |
| 6 | `00000000000000a6` | `execute_tool`, `Int("gen_ai.tool.name", 7)` | |
| 7 | `00000000000000c1` | `Str("gen_ai.operation.name", "chat")`, `Str("gen_ai.request.model", "mai-code-2")`, `Int("gen_ai.usage.input_tokens", 10)`, `Str("gen_ai.tool.name", "x")`, `Int("process.exit.code", 3)` | 1 |
| 8 | `00000000000000b1` | none (a `permission` span) | |

Assert: `ReceivedSpanCount` 8, `RejectedSpanCount` 0, one inference span (`00000000000000c1`), and 8 links in input order with these values:

| # | `OperationName` | `ToolName` | `ToolExitCode` | `StatusCode` |
|---|---|---|---|---|
| 1 | `execute_tool` | `powershell` | 1 | 0 |
| 2 | `execute_tool` | `apply_patch` | null | 0 |
| 3 | `execute_tool` | `HASH` | null | 2 |
| 4 | `execute_tool` | `powershell` | -1073741510 | 0 |
| 5 | `execute_tool` | `powershell` | null | 0 |
| 6 | `execute_tool` | null | null | 0 |
| 7 | `chat` | null | null | 1 |
| 8 | null | null | null | 0 |

#### Storage: `SpanLinkStoreTests` and `SqliteLedgerStoreTests`

- **SL1** (changed). Its setup SQL becomes `DROP INDEX ix_spans_trace; DROP TABLE span_links; DROP TABLE breaker_trips; DROP TABLE branch_budgets; DROP TABLE breaker_settings; PRAGMA user_version = 1;`, and the expected `user_version` is `3L`. Its other assertions are unchanged.
- **S1** (changed). The expected `PRAGMA user_version` is `3L`.
- **SL8.** Migration 3 upgrades a version-2 ledger. Record `Entry("mai-code-2", 1000, 100)` and the link `Link("8", "8888888888888881", null, "execute_tool") with { ToolName = "powershell", ToolExitCode = 1, StatusCode = 2 }`. Through a separate connection run `DROP TABLE breaker_trips; DROP TABLE branch_budgets; DROP TABLE breaker_settings; ALTER TABLE span_links DROP COLUMN tool_name; ALTER TABLE span_links DROP COLUMN tool_exit_code; ALTER TABLE span_links DROP COLUMN status_code; PRAGMA user_version = 2;`. A new store calls `InitializeAsync`. Then, through a separate connection: `PRAGMA user_version` is 3L; `SELECT COUNT(*) FROM spans` is 1L; `SELECT COUNT(*) FROM span_links` is 1L; `SELECT tool_name IS NULL AND tool_exit_code IS NULL AND status_code = 0 FROM span_links` is 1L; `SELECT velocity_threshold_usd_pico FROM breaker_settings WHERE id = 1` is 1500000000000L; and `SELECT COUNT(*) FROM breaker_trips` and `SELECT COUNT(*) FROM branch_budgets` are 0L.
- **SL9.** `RecordSpanLinksAsync` with `Link("9", <span>, null, <operation>) with { ToolName, ToolExitCode, StatusCode }` for: `9999999999999991` (`execute_tool`, `powershell`, 1, 0); `9999999999999992` (`execute_tool`, `apply_patch`, null, 0); `9999999999999993` (`execute_tool`, `HASH`, null, 2); and `9999999999999994` (`chat`, null, null, 1). It returns 4. Recording `9999999999999991` again with `ToolName = "bash"` returns 0. Then `SELECT span_id, tool_name, tool_exit_code, status_code FROM span_links ORDER BY span_id`, through a separate connection, returns exactly those four rows, with `powershell` for `9999999999999991` and `DBNull` for the nulls.
- **SL10, a migration that another process applies first.**
  1. `StoreAsync(temp)` creates the ledger at version 3. Through a separate connection, run `PRAGMA user_version = 2;`, leaving Migration 3's tables and columns in place, as another process would have.
  2. Open a separate connection `holder` and take the write lock: `SqliteTransaction held = holder.BeginTransaction(deferred: false);`.
  3. `Task init = Task.Run(() => new SqliteLedgerStore(DatabasePath(temp)).InitializeAsync());`. Use `Task.Run`: the store's async calls complete synchronously, so calling `InitializeAsync` directly would block the test thread while it waits for the lock.
  4. `await Task.Delay(TimeSpan.FromSeconds(1));` and assert `init.IsCompleted` is false: the store has read version 2 and is waiting for the lock.
  5. On `holder`, with `Transaction = held`, run `PRAGMA user_version = 3;`, then `held.Commit()`.
  6. `await init` completes without an exception. (With the merged loop it throws `SqliteException`, `duplicate column name: tool_name`.)
  7. Through a separate connection: `PRAGMA user_version` is 3L, `SELECT COUNT(*) FROM breaker_settings` is 1L, and `SELECT COUNT(*) FROM pragma_table_info('span_links') WHERE name = 'tool_name'` is 1L.

  With the fix, this case passes however the threads are scheduled. Without it, it fails whenever the store reads the version before the commit, which the 1-second delay all but guarantees.

#### Breaker storage: `BreakerStoreTests` (new)

Each case uses a store at `NOW` on `<root>/ledger.db`, initialized. `ToolLink(trace, span, start, tool, exit, status, operation = "execute_tool", conversation = null)` is a private helper that builds a `GenAiSpanLink` with those values. Trace IDs: `TA = "b5000000000000000000000000000001"`, `TB = "b5000000000000000000000000000002"`, `TC = "b5000000000000000000000000000003"`.

**BS1, `GetTraceActivityAsync`.** Record three entries for `TA`, built with `TestSpans.Chat(...) with { TraceId = TA, SpanId = … }` and priced by the embedded calculator:

- `b500000000000001`: `mai-code-2`, input 1,000, output 100, start `T0`, end `T0 + 2 s`, conversation `conv-b`, Git `FEATURE`;
- `b500000000000002`: `claude-opus-5.5`, input 100,000, output 10,000, start `T0 + 60 s`, end `T0 + 62 s`, conversation `conv-b`, Git `FEATURE with { HeadCommitSha = SHA2 }`;
- `b500000000000003`: `qwen3.8-27b-cuda-gpu:1`, server `127.0.0.1:11434`, input 1,000, start `T0 + 30 s`, end `T0 + 31 s`, no conversation, no Git.

Record these links: `ToolLink(TA, "b5000000000000a1", T0 + 20 s, "powershell", 1, 0)`, `ToolLink(TA, "b5000000000000a2", T0 + 40 s, "powershell", 0, 0)`, `ToolLink(TA, "b5000000000000a3", T0 + 40 s, HASH, null, 2)`, `ToolLink(TA, "b500000000000001", T0, null, null, 0, operation: "chat")`, and `ToolLink(TB, "b5000000000000b1", T0, "powershell", 1, 0, conversation: "conv-links")`.

- `GetTraceActivityAsync(TA)`: `TraceId` `TA`, `ConversationId` `conv-b`, `Repository` `swigerb/copilot-ledger`, `Branch` `feature/ledger`, `RepositoryRoot` `<root>/repo`, and `SpanCount` 3.
  - `Costs` equals `[new CostEvent("b500000000000001", T0 + 2 s, 0.00075m), new CostEvent("b500000000000002", T0 + 62 s, 0.60m)]`.
  - `ToolCalls` equals `[new ToolCallEvent("b5000000000000a1", T0 + 20 s, "powershell", 1, 0), new ToolCallEvent("b5000000000000a2", T0 + 40 s, "powershell", 0, 0), new ToolCallEvent("b5000000000000a3", T0 + 40 s, HASH, null, 2)]`.
  - `Commits` equals `[new CommitObservation("b500000000000001", T0, SHA1), new CommitObservation("b500000000000002", T0 + 60 s, SHA2)]`.
- `GetTraceActivityAsync(TB)`: `ConversationId` `conv-links`, `Repository` `(none)`, `Branch` `(none)`, `RepositoryRoot` null, `SpanCount` 0, empty `Costs` and `Commits`, and `ToolCalls` equal to `[new ToolCallEvent("b5000000000000b1", T0, "powershell", 1, 0)]`.
- `GetTraceActivityAsync(TC)`: `ConversationId` null, `(none)`, `(none)`, `RepositoryRoot` null, `SpanCount` 0, and three empty lists.
- `GetTraceActivityAsync(" ")` throws `ArgumentException`.

**BS2, recording and reading trips.** `A` is `NewBreakerTrip { Rule = CreditVelocity, TraceId = TA, Repository = "swigerb/copilot-ledger", Branch = "feature/ledger", RepositoryRoot = <root>/repo, TrippedAt = T0 + 180 s, Detail = "180.00 AI credits in 5 minutes", WindowCostUsd = 1.80m, WindowMinutes = 5 }`. `B` is `{ Rule = RetryChurn, TraceId = TA, ConversationId = "conv-b", the same repository, branch, and root, TrippedAt = T0 + 120 s, Detail = "5 failed tool calls in a row", FailureCount = 5 }`. `C` is `{ Rule = RetryChurn, TraceId = TB, Repository = "contoso/api", Branch = "main", TrippedAt = T0 - 3600 s, Detail = "7 failed tool calls in a row", FailureCount = 7 }`.

- `RecordBreakerTripAsync(A)` returns 1; `A with { Detail = "x" }` returns null; `B` returns 2; `C` returns 3.
- `GetOpenBreakerTripsAsync(BreakerTripFilter.All)` returns ids 1, 2, 3 in that order. Trip 1 has `Rule` `CreditVelocity`, `TraceId` `TA`, `ConversationId` null, `Repository` `swigerb/copilot-ledger`, `Branch` `feature/ledger`, `RepositoryRoot` `<root>/repo`, `TrippedAt` `T0 + 180 s`, `Detail` `180.00 AI credits in 5 minutes`, `WindowCostUsd` 1.80m, `WindowMinutes` 5, `FailureCount` null, `RecordedAt` `NOW`, and `NotifiedAt` and `ResetAt` null. Trip 2 has `ConversationId` `conv-b`, `WindowCostUsd` null, `WindowMinutes` null, and `FailureCount` 5. Trip 3 has `RepositoryRoot` null and `FailureCount` 7.
- With filters: `{ Repository = "contoso/api" }` returns [3]; `{ Branch = "feature/ledger" }` returns [1, 2]; `{ RepositoryRoot = <root>/repo }` returns [1, 2]; `{ Id = 2 }` returns [2]; and `{ Repository = "swigerb/copilot-ledger", Branch = "main" }` returns [].
- `RecordBreakerTripAsync(A with { TraceId = TC, Detail = " " })` throws `ArgumentException`, and `A with { TraceId = TC, WindowCostUsd = -1m }` throws `ArgumentOutOfRangeException`.

**BS3, notifying and resetting.** Record `A`, `B`, and `C` from BS2.

- `GetUnnotifiedBreakerTripsAsync(NOW - 1 h)` returns ids [1, 2, 3].
- `MarkBreakerTripNotifiedAsync(1)` returns true, again returns false, and `MarkBreakerTripNotifiedAsync(99)` returns false.
- Advance the clock 30 minutes. `GetUnnotifiedBreakerTripsAsync(NOW - 1 h)` returns [2, 3], and `GetUnnotifiedBreakerTripsAsync(NOW + 1 ms)` returns [].
- `ResetBreakerTripsAsync({ Repository = "swigerb/copilot-ledger", Branch = "feature/ledger" })` returns trips [1, 2], each with `ResetAt` `NOW + 30 min`; again, it returns [].
- `GetOpenBreakerTripsAsync(All)` returns [3], and `GetUnnotifiedBreakerTripsAsync(NOW - 1 h)` returns [3].
- `ResetBreakerTripsAsync({ Id = 3 })` returns [3]. Then `GetOpenBreakerTripsAsync(All)` and `ResetBreakerTripsAsync(All)` return [].

**BS4, settings.** `GetBreakerSettingsAsync()` equals `BreakerSettings.Default` (150, 5, 5, false, true). `UpdateBreakerSettingsAsync(new BreakerSettings { VelocityCredits = 12.34m, VelocityWindowMinutes = 1440, ChurnFailures = 2, FlagFile = true, Notifications = false })`; then `GetBreakerSettingsAsync()` equals it, and through a separate connection `velocity_threshold_usd_pico` is 123400000000L and `updated_at_unix_ms` is `NOW`'s Unix milliseconds. Then the upper bound: `UpdateBreakerSettingsAsync(BreakerSettings.Default with { VelocityCredits = 1000000m })` succeeds, `GetBreakerSettingsAsync()` equals that value, and `velocity_threshold_usd_pico` is 10000000000000000L.

**BS5, budgets.** Seed `LedgerSeeds.SeedChainsAsync(store, <root>/repo, <root>/api)`.

- `SetBranchBudgetAsync("swigerb/copilot-ledger", "feature/ledger", 50m)`, `("contoso/api", "main", 40.5m)`, and `("(none)", "(none)", 1m)`.
- `GetBranchBudgetsAsync()` returns, in order:

  | Repository | Branch | Budget | Spent | Exceeded |
  |---|---|---|---|---|
  | `(none)` | `(none)` | 1 | 0 | false |
  | `contoso/api` | `main` | 40.5 | 31.5 | false |
  | `swigerb/copilot-ledger` | `feature/ledger` | 50 | 56 | true |

  Each has `SetAt` `NOW` and null `AlertedAt` and `NotifiedAt`.
- `GetBranchBudgetsAsync("contoso/api")` returns only that row, and `GetBranchBudgetsAsync("swigerb/copilot-ledger", "main")` returns [].
- `MarkBranchBudgetAlertedAsync("swigerb/copilot-ledger", "feature/ledger")` returns true, again returns false, and `("x", "y")` returns false. `GetUnnotifiedBudgetAlertsAsync(NOW - 1 h)` returns that one row, with `AlertedAt` `NOW`.
- `MarkBranchBudgetNotifiedAsync("swigerb/copilot-ledger", "feature/ledger")` returns true and again false; `MarkBranchBudgetNotifiedAsync("contoso/api", "main")` returns false, because it was never alerted. `GetUnnotifiedBudgetAlertsAsync(NOW - 1 h)` returns [].
- Advance the clock 1 hour. `SetBranchBudgetAsync("swigerb/copilot-ledger", "feature/ledger", 60m)`; that row now has budget 60, spent 56, `Exceeded` false, `SetAt` `NOW + 1 h`, and null `AlertedAt` and `NotifiedAt`.
- The boundary. `SetBranchBudgetAsync("swigerb/copilot-ledger", "feature/ledger", 56m)`: the row has budget 56, spent 56, and `Exceeded` false, because spend exactly at the budget doesn't exceed it. `MarkBranchBudgetAlertedAsync("swigerb/copilot-ledger", "feature/ledger")` returns false, and the row's `AlertedAt` is still null. Then `SetBranchBudgetAsync("swigerb/copilot-ledger", "feature/ledger", 55.99m)`: `Exceeded` is true, `MarkBranchBudgetAlertedAsync` returns true, and the row's `AlertedAt` is `NOW + 1 h`.
- `ClearBranchBudgetAsync("contoso/api", "main")` returns true and again false, and `GetBranchBudgetsAsync()` then has 2 rows.
- In a `foreach` inside this test (not a theory), each of `0m`, `-1m`, `1000000.01m`, and `0.001m` makes `SetBranchBudgetAsync("a", "b", value)` throw `ArgumentOutOfRangeException`. `SetBranchBudgetAsync(" ", "b", 1m)` throws `ArgumentException`. `GetBranchBudgetsAsync()` still has 2 rows.

**BS6, failed tool calls.** Record links for trace `X = new string('7', 32)`: `ToolLink(X, "7777777777777771", T0, "powershell", 1, 0)`, `("7777777777777772", T0 + 1 s, "powershell", -1, 0)`, `("7777777777777773", T0 + 2 s, HASH, null, 2)`, `("7777777777777774", T0 + 3 s, "powershell", 0, 0)`, `("7777777777777775", T0 + 4 s, "powershell", 0, 2)`, and `("7777777777777776", T0 + 5 s, "apply_patch", null, 0)`. For `Y = new string('8', 32)`: `("8888888888888881", T0, "bash", 2, 0)`. For `Z = new string('9', 32)`: `("9999999999999991", T0, "powershell", 0, 0)`. `GetFailedToolCallCountsAsync([X, Y, Z, new string('6', 32)])` has exactly 2 entries: `X` 4 and `Y` 1. `GetFailedToolCallCountsAsync([])` is empty.

**BS7, invalid settings** (a theory in its own method, with a fresh store for each case). `public static TheoryData<decimal, int, int> InvalidSettings` holds `(0.99m, 5, 5)`, `(1000000.01m, 5, 5)`, `(12.345m, 5, 5)`, `(150m, 0, 5)`, `(150m, 1441, 5)`, `(150m, 5, 1)`, and `(150m, 5, 101)`. For each `(credits, minutes, failures)`, `UpdateBreakerSettingsAsync(BreakerSettings.Default with { VelocityCredits = credits, VelocityWindowMinutes = minutes, ChurnFailures = failures })` throws `ArgumentOutOfRangeException`, and `GetBreakerSettingsAsync()` still equals `BreakerSettings.Default`.

### Task 2 tests

#### Test support

`RecordingAlertSink : IBreakerAlertSink` keeps `public List<BreakerAlert> Alerts { get; } = []` and `public List<string> Warnings { get; } = []`; `Publish` and `Warn` append to them.

`BreakerTraces` builds requests with the `OtlpRequests` helpers:

```csharp
public static class BreakerTraces
{
    public const string TraceH = "0a000000000000000000000000000001";
    public const string ConversationH = "982f73ad-18f4-49ef-a1f9-70748fa013fb";
    public const string TraceV = "0b000000000000000000000000000001";
    public const string TraceW = "0c000000000000000000000000000001";

    public static OtlpTraceRequest TraceHRequest(int request);                          // run A's export request 1–7
    public static OtlpTraceRequest TraceVRequest(string repoRoot, params int[] chats);  // Trace V chats 1–3
    public static OtlpTraceRequest TraceWRequest(string repoRoot);                       // Trace W's three chats
}
```

**Trace H** replays live run A with synthetic IDs and the captured times and token counts. Every span has trace `TraceH`, parent `0a000000000000f0` (except the root), and `Str("gen_ai.conversation.id", ConversationH)`. The resource is `[.. CopilotResource, Str("service.version", "1.0.112")]`.

- A **chat** span is named `chat mai-code-2-flash`, with `Str("gen_ai.operation.name", "chat")`, `Str("gen_ai.provider.name", "github")`, `Str("gen_ai.request.model", "mai-code-2-flash")`, `Str("gen_ai.response.model", "mai-code-2-flash")`, `Int("gen_ai.usage.input_tokens", in)`, `Int("gen_ai.usage.output_tokens", out)`, and, when the cache read isn't 0, `Int("gen_ai.usage.cache_read.input_tokens", cache)`.
- A **tool** span is named `execute_tool apply_patch` or `execute_tool powershell pwsh.exe`, with `Str("gen_ai.operation.name", "execute_tool")` and `Str("gen_ai.tool.name", "apply_patch" or "powershell")`. A `powershell` span also has `Int("process.exit.code", exit)`.
- The **root** `0a000000000000f0` has no parent and is named `invoke_agent`, with `Str("gen_ai.operation.name", "invoke_agent")`, `Str("gen_ai.provider.name", "github")`, and `Str("github.copilot.agent.type", "builtin")`. It spans 1795220210353000000–1795220248464000000 and is in request 7.

Each request holds its spans in this table's order (chats and tools are interleaved by time):

| Request | Span | Kind | Start ns | End ns | Input | Output | Cache read | Exit |
|---|---|---|---|---|---|---|---|---|
| 1 | `0a000000000000c1` | chat | 1795220210781000000 | 1795220215181000000 | 17299 | 176 | 6656 | |
| 1 | `0a000000000000a1` | `apply_patch` | 1795220215194000000 | 1795220215351000000 | | | | |
| 2 | `0a000000000000c2` | chat | 1795220215368000000 | 1795220217545000000 | 17568 | 44 | 16896 | |
| 2 | `0a000000000000a2` | `powershell` | 1795220217552000000 | 1795220219452000000 | | | | 1 |
| 2 | `0a000000000000c3` | chat | 1795220219475000000 | 1795220221084000000 | 17681 | 44 | 16896 | |
| 2 | `0a000000000000a3` | `apply_patch` | 1795220221093000000 | 1795220221233000000 | | | | |
| 3 | `0a000000000000c4` | chat | 1795220221248000000 | 1795220222694000000 | 17825 | 44 | 16896 | |
| 3 | `0a000000000000a4` | `powershell` | 1795220222710000000 | 1795220223969000000 | | | | 1 |
| 3 | `0a000000000000c5` | chat | 1795220223990000000 | 1795220226844000000 | 17938 | 44 | 16896 | |
| 3 | `0a000000000000a5` | `apply_patch` | 1795220226853000000 | 1795220227044000000 | | | | |
| 4 | `0a000000000000c6` | chat | 1795220227064000000 | 1795220228239000000 | 18082 | 44 | 16896 | |
| 4 | `0a000000000000a6` | `powershell` | 1795220228246000000 | 1795220229660000000 | | | | 1 |
| 4 | `0a000000000000c7` | chat | 1795220229692000000 | 1795220231435000000 | 18195 | 44 | 0 | |
| 4 | `0a000000000000a7` | `apply_patch` | 1795220231440000000 | 1795220231604000000 | | | | |
| 5 | `0a000000000000c8` | chat | 1795220231625000000 | 1795220232986000000 | 18339 | 44 | 17920 | |
| 5 | `0a000000000000a8` | `powershell` | 1795220232994000000 | 1795220234202000000 | | | | 1 |
| 5 | `0a000000000000c9` | chat | 1795220234220000000 | 1795220235567000000 | 18452 | 44 | 17920 | |
| 5 | `0a000000000000a9` | `apply_patch` | 1795220235578000000 | 1795220235717000000 | | | | |
| 6 | `0a000000000000ca` | chat | 1795220235746000000 | 1795220237251000000 | 18596 | 44 | 17920 | |
| 6 | `0a000000000000aa` | `powershell` | 1795220237260000000 | 1795220238525000000 | | | | 1 |
| 6 | `0a000000000000cb` | chat | 1795220238545000000 | 1795220240767000000 | 18709 | 44 | 17920 | |
| 6 | `0a000000000000ab` | `apply_patch` | 1795220240772000000 | 1795220240870000000 | | | | |
| 7 | `0a000000000000cc` | chat | 1795220240889000000 | 1795220242864000000 | 18853 | 44 | 6656 | |
| 7 | `0a000000000000ac` | `powershell` | 1795220242870000000 | 1795220243998000000 | | | | 0 |
| 7 | `0a000000000000cd` | chat | 1795220244019000000 | 1795220245927000000 | 18964 | 57 | 17920 | |
| 7 | `0a000000000000ad` | `powershell` | 1795220245934000000 | 1795220246848000000 | | | | 0 |
| 7 | `0a000000000000ce` | chat | 1795220246868000000 | 1795220248402000000 | 19101 | 6 | 17920 | |
| 7 | `0a000000000000f0` | root | 1795220210353000000 | 1795220248464000000 | | | | |

The 14 chats total input 255,602, output 723, and cache read 205,312, and they cost 0.372181 AI credits at `mai-code-2-flash`'s promotional price: run A's 372,181,000 nano-AIU. The fifth failing call, `0a000000000000aa`, starts at 2026-11-21T00:17:17.260Z (Unix milliseconds 1795220237260).

**Trace V** has three `claude-opus-5.5` chats, each costing $0.60 (input 100,000 at $4 and output 10,000 at $20 per 1M). Each is a root span (no parent) named `chat claude-opus-5.5` with trace `TraceV`, `Str("gen_ai.operation.name", "chat")`, `Str("gen_ai.provider.name", "github")`, `Str("gen_ai.request.model", "claude-opus-5.5")`, `Str("gen_ai.conversation.id", "conv-v")`, `Int("gen_ai.usage.input_tokens", 100000)`, and `Int("gen_ai.usage.output_tokens", 10000)`. The resource is `[.. CopilotResource, Str("process.working_directory", repoRoot)]`. Chat `k` has span ID `0b000000000000c<k>`, ends at `T0 + 60k s`, and starts 2 seconds earlier:

| Chat | Span | Start ns | End ns |
|---|---|---|---|
| 1 | `0b000000000000c1` | 1795255258000000000 | 1795255260000000000 |
| 2 | `0b000000000000c2` | 1795255318000000000 | 1795255320000000000 |
| 3 | `0b000000000000c3` | 1795255378000000000 | 1795255380000000000 |

**Trace W** is built the same way with trace `TraceW`, span IDs `0c000000000000c1`–`0c000000000000c3`, conversation `conv-w`, and ends at `T0 + 60 s`, `T0 + 240 s`, and `T0 + 420 s` (end ns 1795255260000000000, 1795255440000000000, and 1795255620000000000). Each starts 2 seconds earlier. Any 5-minute window holds at most two of its chats ($1.20), so it never trips the default rule.

#### Rules: `BreakerRulesTests` (new, `tests/CopilotLedger.Tests/Breaker/`)

**BR1, rule 1** (theory over the case name, `TheoryData<string>`; each case builds its costs in the test, so no decimal passes through `InlineData`). Costs are `new CostEvent(<id>, T0 + <s> s, <usd>)`, passed in the order given. The rule is strict, so V1, exactly at the threshold, doesn't trip:

| Case | Costs (id@seconds=USD) | Threshold USD | Window | Result |
|---|---|---|---|---|
| V1 | a@30=0.50, b@120=0.60, c@299=0.40 | 1.50 | 5 min | null |
| V2 | a@30=0.50, b@120=0.60, c@299=0.39 | 1.50 | 5 min | null |
| V3 | a@0=1.00, b@300=0.60 | 1.50 | 5 min | null |
| V4 | a@1=1.00, b@300=0.60 | 1.50 | 5 min | `T0 + 300 s`, 1.60 |
| V5 | b@200=0.70, z@150=0.00, a@100=0.81 | 1.50 | 5 min | `T0 + 200 s`, 1.51 |
| V6 | b@60=0.76, a@60=0.75 | 1.50 | 5 min | `T0 + 60 s`, 1.51 |
| V7 | a@0=1.00, b@59=0.51 | 1.50 | 1 min | `T0 + 59 s`, 1.51 |
| V8 | a@0=1.00, b@60=0.51 | 1.50 | 1 min | null |
| V9 | none | 1.50 | 5 min | null |
| V10 | a@30=0.50, b@120=0.60, c@299=0.400000000001 | 1.50 | 5 min | `T0 + 299 s`, 1.500000000001 |

A non-null result is asserted as `Assert.Equal(new VelocityTrip(<time>, <usd>m), result)`.

**BR2, rule 2** (theory over the case name, like BR1). The `k`-th tool call in a case has span ID `0k` (two digits) and starts at `T0 + <s> s`. `F` is `("powershell", 1, 0)`, `S` is `("powershell", 0, 0)`, `E` is `(HASH, null, 2)`, `N` is `("apply_patch", null, 0)`, and `X` is `("powershell", 0, 2)`. Commits are `new CommitObservation(<id>, T0 + <s> s, <sha>)`.

| Case | Tool calls (kind@seconds) | Commits | Failures | Result |
|---|---|---|---|---|
| C1 | F@1 F@2 F@3 S@4 F@5 F@6 F@7 F@8 | none | 5 | null |
| C2 | E@1 E@2 E@3 E@4 E@5 | none | 5 | `T0 + 5 s`, 5 |
| C3 | F@1 F@2 F@3 F@4 F@5 | `c1`@0.5=`aaaa`, `c2`@3.5=`bbbb` | 5 | null |
| C4 | F@1 F@2 F@3 F@4 F@5 | `c1`@0.5=`aaaa`, `c2`@3.5=`aaaa` | 5 | `T0 + 5 s`, 5 |
| C5 | F@1 F@2 F@3 F@4 F@5 | `c1`@0.5=`aaaa`, `c2`@5=`bbbb` | 5 | null (a commit sorts before a call at the same time) |
| C6 | F@1 N@2 F@3 N@4 F@5 N@6 F@7 N@8 F@9 | none | 5 | `T0 + 9 s`, 5 |
| C7 | F@1 F@2 F@3 F@4 X@5 | none | 5 | `T0 + 5 s`, 5 |
| C8 | F@1 F@2 | none | 2 | `T0 + 2 s`, 2 |
| C9 | F@1 F@2 F@3 F@4 N@5 F@6 | none | 5 | `T0 + 6 s`, 5 |
| C10 | F@1 F@2 F@3 F@4 S@5 F@6 | none | 5 | null |
| C11 | none | none | 5 | null |

A non-null result is asserted as `Assert.Equal(new ChurnTrip(<time>, <n>), result)`.

**BR3, argument checks.** `FindVelocityTrip(null!, 1m, TimeSpan.FromMinutes(5))` throws `ArgumentNullException`; `FindVelocityTrip([], 0m, TimeSpan.FromMinutes(5))` and `FindVelocityTrip([], 1m, TimeSpan.Zero)` throw `ArgumentOutOfRangeException`. `FindChurnTrip(null!, [], 5)` and `FindChurnTrip([], null!, 5)` throw `ArgumentNullException`, and `FindChurnTrip([], [], 1)` throws `ArgumentOutOfRangeException`.

**BR4, messages.**

- `BreakerMessages.Credits`: 180m → `180.00`, 1234.5m → `1,234.50`, 0.005m → `0.01`, 0.004999m → `0.00`, 2.675m → `2.68`.
- `Minutes(1)` → `1 minute`; `Minutes(5)` → `5 minutes`.
- `VelocityDetail(1.80m, 5)` → `180.00 AI credits in 5 minutes`; `VelocityDetail(0.015m, 1)` → `1.50 AI credits in 1 minute`. `ChurnDetail(5)` → `5 failed tool calls in a row`.
- `RulesSummary(BreakerSettings.Default)` → `more than 150.00 AI credits in 5 minutes on one prompt; 5 failed tool calls in a row on one prompt`. With `VelocityCredits = 12.5m`, `VelocityWindowMinutes = 1`, and `ChurnFailures = 2` → `more than 12.50 AI credits in 1 minute on one prompt; 2 failed tool calls in a row on one prompt`.
- `ForTrip(7, "swigerb/copilot-ledger", "feature/ledger", "5 failed tool calls in a row", false)` has `Kind` `Trip`, `TripId` 7, that repository and branch, `Title` `CopilotLedger circuit breaker tripped`, `Body` `swigerb/copilot-ledger @ feature/ledger: 5 failed tool calls in a row on one prompt. Run: gh-ledger breaker status`, and `ShowNotification` false.
- `ForBudget(row, true)`, for `Repository = "contoso/api"`, `Branch = "main"`, `BudgetAiCredits = 40.5m`, and `SpentAiCredits = 31.5m`, has `Kind` `Budget`, `TripId` null, `Title` `CopilotLedger branch budget exceeded`, `Body` `contoso/api @ main: 31.50 of 40.50 AI credits spent. Run: gh-ledger budget list`, and `ShowNotification` true. (`ForBudget` formats any row; the monitor calls it only for exceeded ones.)

#### Monitor and ingestion: `CircuitBreakerMonitorTests` (new)

The harness:

- a store at `NOW` on `<root>/data/ledger.db`, initialized; when a case gives settings, `UpdateBreakerSettingsAsync(settings)` runs next;
- the Git fixture, `GitFixture.Create(<root>)`, and `<root>/copilot-home/session-state/982f73ad-18f4-49ef-a1f9-70748fa013fb/workspace.yaml` containing `cwd: <root>/repo` and `\n`;
- `sink = new RecordingAlertSink()` and `monitor = new CircuitBreakerMonitor(store, sink)`;
- `new LedgerIngestor(store, JsonRateCardCalculator.LoadEmbedded(), new FileSystemGitContextResolver(ceilingDirectory: <root>), new CopilotSessionWorkspaceLocator(<root>/copilot-home), monitor)`. It uses the uncached resolver, so a new commit is visible at once.

`FLAG` is `<root>/repo/.copilot-ledger/CIRCUIT_OPEN`. `TripH` is the expected trip for Trace H: `Id` 1, `Rule` `RetryChurn`, `TraceId` `TraceH`, `ConversationId` `ConversationH`, `Repository` `swigerb/copilot-ledger`, `Branch` `feature/ledger`, `RepositoryRoot` `<root>/repo`, `TrippedAt` `DateTimeOffset.FromUnixTimeMilliseconds(1795220237260)`, `Detail` `5 failed tool calls in a row`, `WindowCostUsd` null, `WindowMinutes` null, `FailureCount` 5, `RecordedAt` `NOW`, and `NotifiedAt` and `ResetAt` null. Compare trips property by property.

**BM1, run A trips rule 2 once, on request 6.**

- Ingest `TraceHRequest(1)` through `TraceHRequest(5)`. After each, `sink.Alerts` is empty and `GetOpenBreakerTripsAsync(All)` is empty.
- Ingest request 6: the result is Received 4, Rejected 0, Inference 2, and Recorded 2. `GetOpenBreakerTripsAsync(All)` is `[TripH]`. `sink.Alerts` has one alert: `Kind` `Trip`, `TripId` 1, `Repository` `swigerb/copilot-ledger`, `Branch` `feature/ledger`, `Title` `CopilotLedger circuit breaker tripped`, `Body` `swigerb/copilot-ledger @ feature/ledger: 5 failed tool calls in a row on one prompt. Run: gh-ledger breaker status`, and `ShowNotification` true.
- Ingest request 7: Received 6, Inference 3, Recorded 3. There's still one alert and one open trip.
- Ingest request 6 again: Recorded 0, and there's still one alert.
- The Model rollup has one row: `mai-code-2-flash`, 14 spans, `AiCredits` 0.372181, `AiCreditsUsd` 0.00372181. The Branch rollup has one row: `swigerb/copilot-ledger` / `feature/ledger`.
- `Directory.Exists(<root>/repo/.copilot-ledger)` is false, and `sink.Warnings` is empty.

**BM2, a new commit resets the count.** Ingest requests 1–4. Overwrite `<root>/repo/.git/refs/heads/feature/ledger` with `SHA2` and `\n`. Ingest requests 5–7. `sink.Alerts` and `GetOpenBreakerTripsAsync(All)` are empty. Through a separate connection, `SELECT COUNT(*) FROM spans WHERE head_commit_sha = '<SHA2>'` is 7L and `… = '<SHA1>'` is 7L.

**BM3, a higher threshold.** Settings `BreakerSettings.Default with { ChurnFailures = 6 }`. Ingest requests 1–7. `sink.Alerts` and `GetOpenBreakerTripsAsync(All)` are empty.

**BM4, budget then velocity.** Call `SetBranchBudgetAsync("swigerb/copilot-ledger", "feature/ledger", 100m)` first.

- Ingest `TraceVRequest(<root>/repo, 1)`: no alerts.
- Ingest `TraceVRequest(<root>/repo, 2)`: one alert, `Kind` `Budget`, `TripId` null, `Repository` `swigerb/copilot-ledger`, `Branch` `feature/ledger`, `Title` `CopilotLedger branch budget exceeded`, `Body` `swigerb/copilot-ledger @ feature/ledger: 120.00 of 100.00 AI credits spent. Run: gh-ledger budget list`, and `ShowNotification` true. The budget row's `AlertedAt` is `NOW`.
- Ingest `TraceVRequest(<root>/repo, 3)`: two alerts. The second has `Kind` `Trip`, `TripId` 1, `Body` `swigerb/copilot-ledger @ feature/ledger: 180.00 AI credits in 5 minutes on one prompt. Run: gh-ledger breaker status`, and `ShowNotification` true. The open trip has `Id` 1, `Rule` `CreditVelocity`, `TraceId` `TraceV`, `ConversationId` `conv-v`, `Repository` `swigerb/copilot-ledger`, `Branch` `feature/ledger`, `RepositoryRoot` `<root>/repo`, `TrippedAt` `T0 + 180 s`, `Detail` `180.00 AI credits in 5 minutes`, `WindowCostUsd` 1.80m, `WindowMinutes` 5, `FailureCount` null, and `RecordedAt` `NOW`.
- Ingest `TraceVRequest(<root>/repo, 3)` again: still two alerts.

**BM5, the window slides.** Ingest `TraceWRequest(<root>/repo)`. `sink.Alerts` and `GetOpenBreakerTripsAsync(All)` are empty.

**BM6, settings change the trip.** Settings `BreakerSettings.Default with { VelocityCredits = 100m, Notifications = false }`. Ingest `TraceVRequest(<root>/repo, 1, 2)` as one request. One alert: `Kind` `Trip`, `Body` `swigerb/copilot-ledger @ feature/ledger: 120.00 AI credits in 5 minutes on one prompt. Run: gh-ledger breaker status`, and `ShowNotification` false. The trip has `TrippedAt` `T0 + 120 s` and `WindowCostUsd` 1.20m.

**BM7, the flag file.** Settings `BreakerSettings.Default with { FlagFile = true }`. Ingest requests 1–6.

- `File.ReadAllText(FLAG)` is exactly this text, with `ROOT` replaced by `JsonSerializer.Serialize(<root>/repo, RelaxedJson)`, after `.ReplaceLineEndings("\n")`, plus a final `\n`:

  ```text
  {
    "schemaVersion": 1,
    "repositoryRoot": ROOT,
    "openTrips": [
      {
        "id": 1,
        "rule": "retry_churn",
        "repository": "swigerb/copilot-ledger",
        "branch": "feature/ledger",
        "conversationId": "982f73ad-18f4-49ef-a1f9-70748fa013fb",
        "traceId": "0a000000000000000000000000000001",
        "trippedAt": "2026-11-21T00:17:17.2600000+00:00",
        "detail": "5 failed tool calls in a row",
        "resetCommand": "gh-ledger breaker reset --id 1"
      }
    ]
  }
  ```

- `File.ReadAllText(<root>/repo/.copilot-ledger/.gitignore)` is exactly `*\n`.
- `ResetBreakerTripsAsync(BreakerTripFilter.All)`, then `CircuitFlagFile.SyncAsync(store, <root>/repo)`: `FLAG` doesn't exist, and the `.gitignore` still does.
- `CircuitFlagFile.SyncAsync(store, <root>/missing)` returns without an exception, and `<root>/missing` doesn't exist.
- `sink.Warnings` is empty.

**BM8, a flag file that can't be written.** Settings `BreakerSettings.Default with { FlagFile = true }`. Create an empty **file** at `<root>/repo/.copilot-ledger`. Ingest requests 1–6. The trip is recorded (`GetOpenBreakerTripsAsync(All)` is `[TripH]`), `sink.Alerts` has the one trip alert, and `sink.Warnings` has one message starting with `could not update <FLAG>: ` (the rest is the OS's message).

**BM9, no monitor.** Build the ingestor with the four-argument constructor and ingest requests 1–7. `GetOpenBreakerTripsAsync(All)` is empty, and through a separate connection `SELECT COUNT(*) FROM breaker_trips` is 0L.

**BM10, a trip without a repository.** Settings `BreakerSettings.Default with { FlagFile = true, ChurnFailures = 2 }`. Ingest `Request(CopilotResource, …)` with two spans in trace `0d000000000000000000000000000001`, span IDs `0d000000000000a1` and `0d000000000000a2`, starting at `T0 + 1 s` and `T0 + 2 s` (end 1 second later), each with `Str("gen_ai.operation.name", "execute_tool")`, `Str("gen_ai.tool.name", "powershell")`, and `Int("process.exit.code", 1)`. The open trip has `Repository` `(none)`, `Branch` `(none)`, `RepositoryRoot` null, `ConversationId` null, `TrippedAt` `T0 + 2 s`, `Detail` `2 failed tool calls in a row`, and `FailureCount` 2. The one alert's `Body` is `(none) @ (none): 2 failed tool calls in a row on one prompt. Run: gh-ledger breaker status`. `sink.Warnings` is empty, and `Directory.GetDirectories(<root>, ".copilot-ledger", SearchOption.AllDirectories)` is empty.

**BM11, spend exactly at the budget doesn't alert.** Call `SetBranchBudgetAsync("swigerb/copilot-ledger", "feature/ledger", 120m)` first, then ingest `TraceVRequest(<root>/repo, 1)`, `TraceVRequest(<root>/repo, 2)`, and `TraceVRequest(<root>/repo, 3)` as three separate requests.

- After chat 1: `sink.Alerts` is empty.
- After chat 2, the branch has spent exactly 120.00 AI credits: `sink.Alerts` is still empty, `GetOpenBreakerTripsAsync(All)` is empty ($1.20 isn't over $1.50), and the budget row has `Exceeded` false and a null `AlertedAt`.
- After chat 3: `sink.Alerts` has exactly two alerts, in this order. First the trip: `Kind` `Trip`, `TripId` 1, `Title` `CopilotLedger circuit breaker tripped`, `Body` `swigerb/copilot-ledger @ feature/ledger: 180.00 AI credits in 5 minutes on one prompt. Run: gh-ledger breaker status`. Then the budget: `Kind` `Budget`, `TripId` null, `Title` `CopilotLedger branch budget exceeded`, `Body` `swigerb/copilot-ledger @ feature/ledger: 180.00 of 120.00 AI credits spent. Run: gh-ledger budget list`. Both have `Repository` `swigerb/copilot-ledger`, `Branch` `feature/ledger`, and `ShowNotification` true. The budget row's `AlertedAt` is `NOW`.

### Task 3 tests

#### Test support

`BreakerTraces.TraceVJson(string repoRoot)` returns Trace V's three chats as one OTLP/JSON request, on one line, with `REPO` replaced by `JsonSerializer.Serialize(repoRoot, RelaxedJson)`:

```text
{"resourceSpans":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"github-copilot"}},{"key":"process.working_directory","value":{"stringValue":REPO}}]},"scopeSpans":[{"scope":{"name":"github.copilot"},"spans":[SPAN1,SPAN2,SPAN3]}]}]}
```

`SPANk` is the following, with `ID`, `START`, and `END` from Trace V's table (chat `k`):

```text
{"traceId":"0b000000000000000000000000000001","spanId":"ID","parentSpanId":"","name":"chat claude-opus-5.5","kind":3,"startTimeUnixNano":"START","endTimeUnixNano":"END","attributes":[{"key":"gen_ai.operation.name","value":{"stringValue":"chat"}},{"key":"gen_ai.provider.name","value":{"stringValue":"github"}},{"key":"gen_ai.request.model","value":{"stringValue":"claude-opus-5.5"}},{"key":"gen_ai.conversation.id","value":{"stringValue":"conv-v"}},{"key":"gen_ai.usage.input_tokens","value":{"intValue":"100000"}},{"key":"gen_ai.usage.output_tokens","value":{"intValue":"10000"}}],"status":{"code":0}}
```

`SCRIPT(path)` is Task 3's PowerShell script with `XMLPATH` replaced by `path` in which each of `'`, `\u2018`, `\u2019`, `\u201A`, and `\u201B` is doubled; the test builds it with its own private helper, because `DesktopNotifier` is internal. `WINDOWS` is `new FakeCliPlatform { IsWindows = true, ProcessPath = @"C:\tools\gh-ledger.exe", ProcessVariables = { ["SystemRoot"] = @"C:\Windows" } }`. `RULES` is `Rules: more than 150.00 AI credits in 5 minutes on one prompt; 5 failed tool calls in a row on one prompt`.

#### Commands: `BreakerCommandTests` (new)

Each case uses `new CliHarness(<root>, platform, new ManualTimeProvider(NOW, ManualTimeProvider.UtcMinus5))` and, to seed, a store at `NOW` on `<root>/data/ledger.db`. Trips are seeded with `RecordBreakerTripAsync`:

- `VA`: `{ Rule = CreditVelocity, TraceId = new string('1', 32), Repository = "swigerb/copilot-ledger", Branch = "feature/ledger", TrippedAt = T0 + 180 s, Detail = "180.00 AI credits in 5 minutes", WindowCostUsd = 1.80m, WindowMinutes = 5 }`;
- `CB`: `{ Rule = RetryChurn, TraceId = new string('2', 32), ConversationId = "conv-2", Repository = "swigerb/copilot-ledger", Branch = "main", TrippedAt = T0 + 300 s, Detail = "5 failed tool calls in a row", FailureCount = 5 }`.

**BC1, closed.** `breaker status` exits 0, and `Out` is exactly:

```text
Circuit breaker: closed
RULES
Flag file: off. Notifications: on.
```

`breaker status --json` exits 0, and its `Out` deep-equals `{"state":"closed","settings":{"velocityCredits":150,"velocityWindowMinutes":5,"churnFailures":5,"flagFile":false,"notifications":true},"openTrips":[]}`.

**BC2, open.** Seed `VA` (id 1) and `CB` (id 2), then `MarkBreakerTripNotifiedAsync(1)`. `breaker status` exits 0, and `Out` is exactly:

```text
Circuit breaker: open (2 trips)
RULES
Flag file: off. Notifications: on.

Trip 2 at 2026-11-21 05:05: swigerb/copilot-ledger @ main: 5 failed tool calls in a row on one prompt
  rule retry_churn, trace 22222222222222222222222222222222, session conv-2
Trip 1 at 2026-11-21 05:03: swigerb/copilot-ledger @ feature/ledger: 180.00 AI credits in 5 minutes on one prompt
  rule credit_velocity, trace 11111111111111111111111111111111, session (none)
Reset a trip with: gh-ledger breaker reset --id <id>
```

`breaker status --json` deep-equals:

```json
{
  "state": "open",
  "settings": { "velocityCredits": 150, "velocityWindowMinutes": 5, "churnFailures": 5, "flagFile": false, "notifications": true },
  "openTrips": [
    { "id": 2, "rule": "retry_churn", "repository": "swigerb/copilot-ledger", "branch": "main", "repositoryRoot": null, "conversationId": "conv-2", "traceId": "22222222222222222222222222222222", "trippedAt": "2026-11-21T10:05:00.0000000+00:00", "detail": "5 failed tool calls in a row", "windowAiCredits": null, "windowMinutes": null, "failureCount": 5, "recordedAt": "2026-11-21T12:00:00.0000000+00:00", "notifiedAt": null },
    { "id": 1, "rule": "credit_velocity", "repository": "swigerb/copilot-ledger", "branch": "feature/ledger", "repositoryRoot": null, "conversationId": null, "traceId": "11111111111111111111111111111111", "trippedAt": "2026-11-21T10:03:00.0000000+00:00", "detail": "180.00 AI credits in 5 minutes", "windowAiCredits": 180, "windowMinutes": 5, "failureCount": null, "recordedAt": "2026-11-21T12:00:00.0000000+00:00", "notifiedAt": "2026-11-21T12:00:00.0000000+00:00" }
  ]
}
```

**BC3, reset.** Create the Git fixture. Seed `VA` (1), `CB` (2), `CB with { TraceId = new string('3', 32), Repository = "contoso/api" }` (3), and `CB with { TraceId = new string('4', 32) }` (4). In order:

1. `breaker reset --id 2 --all` and `breaker reset --all --repo x --branch y` each exit 1, with `Error` containing `Use only one of --id, --all, or --repo with --branch.`
2. `breaker reset --repo swigerb/copilot-ledger` exits 1, with `Error` containing `--repo and --branch must be used together.`
3. `breaker reset`, run in `<root>`, exits 1, and `Error` is exactly `Not in a Git repository. Pass --repo and --branch, --id, or --all.`
4. `breaker reset` run in `<root>/repo` (`RunInAsync`) exits 0 with `Out` exactly `Reset 1 trip.`; run again, `No open trips to reset.`
5. `breaker reset --repo swigerb/copilot-ledger --branch main` prints exactly `Reset 2 trips.`
6. `breaker reset --id 3` prints exactly `Reset 1 trip.`; run again, `No open trips to reset.`
7. `breaker reset --all` prints exactly `No open trips to reset.`, and `GetOpenBreakerTripsAsync(All)` is empty.

**BC4, config.** `breaker config` exits 0, and `Out` is exactly `RULES` and `Flag file: off. Notifications: on.` on two lines. `breaker config --velocity-credits 12.5 --velocity-minutes 1 --churn-failures 2 --notifications off` exits 0, and `Out` is exactly:

```text
Updated the circuit breaker settings.
Rules: more than 12.50 AI credits in 1 minute on one prompt; 2 failed tool calls in a row on one prompt
Flag file: off. Notifications: off.
```

`GetBreakerSettingsAsync()` then equals `{ VelocityCredits = 12.5m, VelocityWindowMinutes = 1, ChurnFailures = 2, FlagFile = false, Notifications = false }`. Then, a theory in a fresh harness: each of these exits 1 with `Error` containing the message, and the settings still equal `BreakerSettings.Default`:

| Arguments | Message |
|---|---|
| `--velocity-credits 0.99`, `1000000.01`, `1.001`, `1e2`, `1,5` | `--velocity-credits must be a number from 1 to 1000000 with at most 2 decimal places.` |
| `--velocity-minutes 0`, `1441` | `--velocity-minutes must be from 1 to 1440.` |
| `--churn-failures 1`, `101` | `--churn-failures must be from 2 to 100.` |
| `--flag-file yes` | `--flag-file must be on or off.` |
| `--notifications ON` | `--notifications must be on or off.` |

**BC5, the flag file follows the setting.** Create the directory `<root>/repo` and seed `VA with { RepositoryRoot = <root>/repo }` (id 1).

- `breaker config --flag-file on` exits 0 with `Out` exactly `Updated the circuit breaker settings.`, `RULES`, and `Flag file: on. Notifications: on.` on three lines. `<root>/repo/.copilot-ledger/CIRCUIT_OPEN` then holds exactly BM7's text, with `"rule": "credit_velocity"`, `"branch": "feature/ledger"`, `"conversationId": null`, `"traceId": "11111111111111111111111111111111"`, `"trippedAt": "2026-11-21T10:03:00.0000000+00:00"`, and `"detail": "180.00 AI credits in 5 minutes"` in place of Trace H's values.
- Delete the flag file, as if the daemon had failed to write it. `breaker config --flag-file on`, run again with the setting already on, exits 0 and writes the same text again.
- `breaker config --flag-file off` exits 0, and the flag file doesn't exist.

**BC6, budgets.** Create the Git fixture.

1. `budget list` exits 0 with `Out` exactly `No branch budgets are set.`
2. Seed `LedgerSeeds.SeedChainsAsync(store, <root>/repo, <root>/api)`.
3. `budget set 56` in `<root>/repo` exits 0, and `Out` is exactly the one line `Budget for swigerb/copilot-ledger @ feature/ledger: 56.00 AI credits. Spent so far: 56.00.`: spend exactly at the budget isn't over it. Then `budget set 50` in `<root>/repo` exits 0, and `Out` is exactly `Budget for swigerb/copilot-ledger @ feature/ledger: 50.00 AI credits. Spent so far: 56.00.` and `This branch is already over its budget.` on two lines.
4. `budget set 40.5 --repo contoso/api --branch main` prints exactly `Budget for contoso/api @ main: 40.50 AI credits. Spent so far: 31.50.`
5. `budget list` prints exactly `contoso/api @ main: 31.50 of 40.50 AI credits` and `swigerb/copilot-ledger @ feature/ledger: 56.00 of 50.00 AI credits (over budget)` on two lines.
6. `budget list --json` deep-equals `{"budgets":[{"repository":"contoso/api","branch":"main","budgetAiCredits":40.5,"spentAiCredits":31.5,"exceeded":false,"setAt":"2026-11-21T12:00:00.0000000+00:00","alertedAt":null},{"repository":"swigerb/copilot-ledger","branch":"feature/ledger","budgetAiCredits":50,"spentAiCredits":56,"exceeded":true,"setAt":"2026-11-21T12:00:00.0000000+00:00","alertedAt":null}]}`.
7. `budget clear --repo contoso/api --branch main` prints exactly `Cleared the budget for contoso/api @ main.`; again, `No budget is set for contoso/api @ main.`
8. `budget set 0`, `budget set abc`, `budget set 1000000.01`, and `budget set 0.001` (in `<root>/repo`) each exit 1 with `Error` containing `Credits must be a number greater than 0 and at most 1000000 with at most 2 decimal places.`
9. `budget set 5` in `<root>` exits 1, and `Error` is exactly `Not in a Git repository. Pass --repo and --branch.`
10. `budget clear --branch main` exits 1 with `Error` containing `--repo and --branch must be used together.`

#### Daemon alerts: `AlertDaemonTests` (new)

Each case starts the daemon with `StartInProcessDaemonAsync()`, so its log goes to `DaemonOutput`. It waits with `WaitUntilAsync`, then runs `daemon stop` and awaits `harness.InProcessDaemon` before reading `ProcessRuns` or `DaemonOutput`. `ALERT_A` is `ALERT CopilotLedger circuit breaker tripped: swigerb/copilot-ledger @ feature/a: 5 failed tool calls in a row on one prompt. Run: gh-ledger breaker status`.

**AD1, end to end on Windows.** `WINDOWS` platform, `TimeProvider.System`, and the Git fixture.

1. `breaker config --flag-file on` exits 0.
2. `StartInProcessDaemonAsync()`. With `harness.CreateDaemonClient()`, post `TraceVJson(<root>/repo)` to `/v1/traces` as `application/json`: the status is 200 and the body is `{}`.
3. `<root>/repo/.copilot-ledger/CIRCUIT_OPEN` exists as soon as the response arrives.
4. Wait until the store's single open trip has a non-null `NotifiedAt`. `breaker status` exits 0, and `Out` starts with `Circuit breaker: open (1 trip)\n`.
5. Stop the daemon. `ProcessRuns` has exactly one entry: `@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"` with arguments `["-NoProfile", "-NonInteractive", "-Command", SCRIPT(<root>/data/alert-toast.xml)]`.
6. `File.ReadAllText(<root>/data/alert-toast.xml)` is exactly `<toast><visual><binding template="ToastGeneric"><text>CopilotLedger circuit breaker tripped</text><text>swigerb/copilot-ledger @ feature/ledger: 180.00 AI credits in 5 minutes on one prompt. Run: gh-ledger breaker status</text></binding></visual></toast>`.
7. `DaemonOutput` contains `ALERT CopilotLedger circuit breaker tripped: swigerb/copilot-ledger @ feature/ledger: 180.00 AI credits in 5 minutes on one prompt. Run: gh-ledger breaker status`.
8. `breaker reset` in `<root>/repo` prints exactly `Reset 1 trip.`. The flag file no longer exists, and `<root>/repo/.copilot-ledger/.gitignore` does. `breaker status` then prints `Circuit breaker: closed` as its first line.

**AD2, a restart re-sends recent alerts.** `new FakeCliPlatform { IsMacOS = true }`, with the harness on `new ManualTimeProvider(NOW)`. Before starting the daemon, seed through stores on `<root>/data/ledger.db` whose clocks are set as shown. Each trip is `{ Rule = RetryChurn, TraceId = new string(<c>, 32), Repository = "swigerb/copilot-ledger", Branch = <branch>, TrippedAt = <clock>, Detail = "5 failed tool calls in a row", FailureCount = 5 }`:

| Clock | Action |
|---|---|
| `NOW - 30 min` | `LedgerSeeds.SeedChainsAsync(store, <root>/repo, <root>/api)`; trip `'1'`, `feature/a` (id 1) |
| `NOW - 2 h` | trip `'2'`, `feature/b` (id 2) |
| `NOW - 15 min` | trip `'3'`, `feature/c` (id 3), then `MarkBreakerTripNotifiedAsync(3)` |
| `NOW - 10 min` | trip `'4'`, `feature/d` (id 4), then `ResetBreakerTripsAsync(new BreakerTripFilter { Id = 4 })` |
| `NOW - 20 min` | `SetBranchBudgetAsync("swigerb/copilot-ledger", "feature/ledger", 50m)`, then `MarkBranchBudgetAlertedAsync` for that branch |

Start the daemon with `StartInProcessDaemonAsync()`, and wait until trip 1's `NotifiedAt` and the budget's `NotifiedAt` are both set. After stopping:

- `ProcessRuns` has exactly two entries, both `/usr/bin/osascript` with arguments `["-e", "on run argv", "-e", "display notification (item 2 of argv) with title (item 1 of argv)", "-e", "end run", <title>, <body>]`. The first has `CopilotLedger circuit breaker tripped` and `swigerb/copilot-ledger @ feature/a: 5 failed tool calls in a row on one prompt. Run: gh-ledger breaker status`. The second has `CopilotLedger branch budget exceeded` and `swigerb/copilot-ledger @ feature/ledger: 56.00 of 50.00 AI credits spent. Run: gh-ledger budget list`.
- Trip 1's `NotifiedAt` is `NOW`, trip 2 is still open with a null `NotifiedAt`, and the budget's `NotifiedAt` is `NOW`.
- `DaemonOutput` contains `ALERT_A` and `ALERT CopilotLedger branch budget exceeded: swigerb/copilot-ledger @ feature/ledger: 56.00 of 50.00 AI credits spent. Run: gh-ledger budget list`, and doesn't contain `feature/b`, `feature/c`, or `feature/d`.

**AD3, notification outcomes** (theory over a case name). The harness is `new CliHarness(<harness root>, platform, new ManualTimeProvider(NOW))`, where `<harness root>` is `<root>` unless the case says otherwise. Before `StartInProcessDaemonAsync()`, seed trip `'1'` on `feature/a` (as in AD2) with a store at `NOW` on `<harness root>/data/ledger.db`. Wait until its `NotifiedAt` is set. In every case, `NotifiedAt` is then `NOW` and `DaemonOutput` contains `ALERT_A`. Then:

| Case | Platform and setup | Expected |
|---|---|---|
| `linux-notify-send` | `new FakeCliPlatform { PathExecutables = { ["notify-send"] = "/usr/bin/notify-send" } }` | `ProcessRuns` is exactly `[("/usr/bin/notify-send", ["--app-name=CopilotLedger", "CopilotLedger circuit breaker tripped", "swigerb/copilot-ledger @ feature/a: 5 failed tool calls in a row on one prompt. Run: gh-ledger breaker status"])]` |
| `linux-missing` | `new FakeCliPlatform()` | `ProcessRuns` is empty; `DaemonOutput` contains `Notification skipped: notify-send was not found.` |
| `notifications-off` | `WINDOWS`, and `breaker config --notifications off` before starting | `ProcessRuns` is empty |
| `windows-exit-code` | `WINDOWS` with `RunHandler = (_, _, _) => Task.FromResult(new ProcessRunResult(1, "", "boom\n"))` | `DaemonOutput` contains `Notification failed: C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe exited with code 1: boom` |
| `windows-throws` | `WINDOWS` with `RunHandler = (_, _, _) => throw new Win32Exception(2, "The system cannot find the file specified.")` | `DaemonOutput` contains `Notification failed: The system cannot find the file specified.` |
| `windows-quotes` | `WINDOWS`, with `<harness root>` = `Path.Combine(<root>, "it's O\u2019Brien")` | `ProcessRuns` has exactly one entry: `@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"` with `["-NoProfile", "-NonInteractive", "-Command", SCRIPT(Path.Combine(<root>, "it's O\u2019Brien", "data", "alert-toast.xml"))]`. That script contains `it''s O\u2019\u2019Brien`. |

### Task 4 tests

#### Test support

`McpTestHarness` (new) connects the SDK client to the server over two `System.IO.Pipelines.Pipe`s:

```csharp
public sealed class McpTestHarness : IAsyncDisposable
{
    // Starts LedgerMcpServer.RunAsync(context, toServer.Reader.AsStream(), toClient.Writer.AsStream()) and connects
    // McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()),
    //     new McpClientOptions { ClientInfo = new Implementation { Name = "ledger-tests", Version = "1.0" } }).
    public static Task<McpTestHarness> StartAsync(LedgerMcpContext context);

    public McpClient Client { get; }

    // Disposes the client, completes toServer.Writer, and waits up to 10 seconds for the server task.
    public ValueTask DisposeAsync();
}
```

`CallAsync(harness, tool, json)` is a private helper that turns a JSON object into arguments (`JsonDocument.Parse(json).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone())`) and calls `Client.CallToolAsync(tool, arguments)`. `Text(result)` is the `Text` of the single `TextContentBlock` in `result.Content`.

The context is `new LedgerMcpContext { DatabasePath = <root>/data/ledger.db, WorkingDirectory = <root>/repo, GetEnvironmentVariable = name => env.GetValueOrDefault(name), GitResolver = new FileSystemGitContextResolver(ceilingDirectory: <root>), TimeProvider = new ManualTimeProvider(NOW), Version = "9.9.9" }`, where `env` is a `Dictionary<string, string>` that starts empty. The Git fixture is created with `GitFixture.Create(<root>)`.

**Seeds** (`SeedAsync`, with a store at `NOW`):

1. `LedgerSeeds.SeedChainsAsync(store, <root>/repo, <root>/api)`.
2. `T5 = new string('5', 32)`: `TestSpans.Chat(startTime: 2026-11-21T15:00:00Z, requestModel: "claude-sonnet-5.5", conversationId: "conv-5", inputTokens: 100_000, outputTokens: 50_000) with { TraceId = T5, SpanId = "5555555555555555" }`, with Git `FEATURE`; it costs $0.70.
3. `T6 = new string('6', 32)`: `TestSpans.Chat(startTime: 2026-11-21T16:00:00Z, requestModel: "mai-code-2", conversationId: "conv-6", inputTokens: 10_000, outputTokens: 200_000) with { TraceId = T6, SpanId = "6666666666666666" }`, with Git `FEATURE`; it costs $0.505.
4. `execute_tool` links for `T5`: `55555555555555a1`–`55555555555555a5` (`powershell`, exit 1, starting at 15:00:01–15:00:05) and `55555555555555a6` (`powershell`, exit 0, 15:00:06). For `T6`: `66666666666666a1` (`HASH`, no exit code, status 2, 16:00:01).

So `swigerb/copilot-ledger` / `feature/ledger` holds `T2` (from `LedgerSeeds`, 56 AI credits), `T5` (70), and `T6` (50.5).

`BRANCH` is the expected `get_branch_ledger` object for that branch with no trips and no budget:

```json
{
  "repository": "swigerb/copilot-ledger",
  "branch": "feature/ledger",
  "spans": 4,
  "unpricedSpans": 0,
  "inputTokens": 1510000,
  "outputTokens": 362000,
  "cacheReadInputTokens": 400000,
  "cacheWriteInputTokens": 0,
  "aiCredits": 176.5,
  "aiCreditsUsd": 1.765,
  "foundryByokUsd": 0,
  "localSavedUsd": 0.23,
  "billedAiCredits": 176.5,
  "topModel": "mai-code-2",
  "budget": null,
  "circuitBreaker": { "state": "closed", "openTrips": [] },
  "warnings": []
}
```

#### MCP: `LedgerMcpServerTests` (new, `tests/CopilotLedger.Tests/Mcp/`)

For a successful call, "the result is X" means: `IsError` isn't true (`Assert.NotEqual(true, result.IsError)`); `Content` is one `TextContentBlock`; its text, split on `\n`, is the warnings in order followed by one line of compact JSON; that JSON and `JsonNode.Parse(structured.GetRawText())`, where `JsonElement structured = Assert.NotNull(result.StructuredContent);`, both deep-equal X; and the warning lines equal X's `warnings` array.

The SDK's `McpClient.ServerInfo` is `Implementation?` and `Tool.Annotations` is `ToolAnnotations?`. Under `TreatWarningsAsErrors`, reading a member of either directly is error CS8602, so copy each into a local and `Assert.NotNull` it first: `Implementation? serverInfo = harness.Client.ServerInfo; Assert.NotNull(serverInfo);`.

**MC1, `tools/list`.** No seeds. `serverInfo` (as above) is not null, `serverInfo.Name` is `copilot-ledger`, `serverInfo.Version` is `9.9.9`, and `Client.ServerInstructions` equals `LedgerMcpServer.Instructions`. `ListToolsAsync()` returns the four tools in Task 4's order. For each, `ProtocolTool.Title` and `Description` equal Task 4's text, `InputSchema` deep-equals Task 4's schema, `OutputSchema` is null, and `ProtocolTool.Annotations` is not null, with: `ReadOnlyHint` true, `DestructiveHint` null, `IdempotentHint` null, and `OpenWorldHint` false for the read tools; and `ReadOnlyHint` false, `DestructiveHint` false, `IdempotentHint` true, and `OpenWorldHint` false for `set_branch_budget`.

**MC2, the branch ledger.** Seeds. `get_branch_ledger` with `{}`: the result is `BRANCH`.

**MC3, warnings.** Seeds. Then, with the store, trips 1–3 and a budget:

- trip 1: `{ Rule = RetryChurn, TraceId = T5, ConversationId = "conv-5", Repository = "swigerb/copilot-ledger", Branch = "feature/ledger", RepositoryRoot = <root>/repo, TrippedAt = 2026-11-21T15:00:05Z, Detail = "5 failed tool calls in a row", FailureCount = 5 }`;
- trip 2: `{ Rule = RetryChurn, TraceId = new string('a', 32), ConversationId = "conv-other", Repository = "contoso/api", Branch = "main", TrippedAt = 2026-11-21T09:00:00Z, Detail = "7 failed tool calls in a row", FailureCount = 7 }`;
- trip 3: `{ Rule = RetryChurn, TraceId = new string('b', 32), ConversationId = "conv-5", Repository = "swigerb/copilot-ledger", Branch = "main", TrippedAt = 2026-11-21T14:30:00Z, Detail = "6 failed tool calls in a row", FailureCount = 6 }`;
- `SetBranchBudgetAsync("swigerb/copilot-ledger", "feature/ledger", 150m)`.

`OPEN1` is `{"id":1,"rule":"retry_churn","repository":"swigerb/copilot-ledger","branch":"feature/ledger","traceId":"55555555555555555555555555555555","conversationId":"conv-5","currentSession":S,"trippedAt":"2026-11-21T15:00:05.0000000+00:00","detail":"5 failed tool calls in a row","resetCommand":"gh-ledger breaker reset --id 1"}`, and `OPEN3` is `{"id":3,"rule":"retry_churn","repository":"swigerb/copilot-ledger","branch":"main","traceId":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","conversationId":"conv-5","currentSession":true,"trippedAt":"2026-11-21T14:30:00.0000000+00:00","detail":"6 failed tool calls in a row","resetCommand":"gh-ledger breaker reset --id 3"}`. The warnings are:

- `W1`: `WARNING: CopilotLedger circuit breaker is open for swigerb/copilot-ledger @ feature/ledger: 5 failed tool calls in a row on one prompt (trip 1). Stop and ask the user before continuing. The user can reset it with: gh-ledger breaker reset --id 1`
- `W3`: `WARNING: CopilotLedger circuit breaker is open for swigerb/copilot-ledger @ main: 6 failed tool calls in a row on one prompt (trip 3). Stop and ask the user before continuing. The user can reset it with: gh-ledger breaker reset --id 3`
- `WB`: `WARNING: swigerb/copilot-ledger @ feature/ledger has spent 176.50 of its 150.00 AI credit budget.`

Without `COPILOT_AGENT_SESSION_ID`, `get_branch_ledger` `{}` gives `BRANCH` with `budget` `{"aiCredits":150,"spentAiCredits":176.5,"exceeded":true}`, `circuitBreaker` `{"state":"open","openTrips":[OPEN1 with S = false]}`, and `warnings` `[W1, WB]`. With `env["COPILOT_AGENT_SESSION_ID"] = "conv-5"` in a new harness, the same call gives `openTrips` `[OPEN1 with S = true, OPEN3]` and `warnings` `[W1, W3, WB]`.

**MC4, an explicit scope.** Seeds. `get_branch_ledger` with `{"repository":"contoso/api","branch":"main"}`: the result is `{"repository":"contoso/api","branch":"main","spans":1,"unpricedSpans":0,"inputTokens":100000,"outputTokens":10000,"cacheReadInputTokens":0,"cacheWriteInputTokens":0,"aiCredits":0,"aiCreditsUsd":0,"foundryByokUsd":0.315,"localSavedUsd":0,"billedAiCredits":31.5,"topModel":"gpt-6.1-sol","budget":null,"circuitBreaker":{"state":"closed","openTrips":[]},"warnings":[]}`. With `{"repository":"swigerb/copilot-ledger","branch":"nope"}`, every count and amount is 0, `topModel` is `null`, `budget` is `null`, and the breaker is closed.

**MC5, top prompts.** Seeds. `P5`, `P2`, and `P6` are these prompt objects (without `rank`):

- `P5`: `{"traceId":"55555555555555555555555555555555","conversationId":"conv-5","startTime":"2026-11-21T15:00:00.0000000+00:00","endTime":"2026-11-21T15:00:02.0000000+00:00","agents":[],"models":["claude-sonnet-5.5"],"spans":1,"unpricedSpans":0,"inputTokens":100000,"outputTokens":50000,"cacheReadInputTokens":0,"cacheWriteInputTokens":0,"aiCredits":70,"aiCreditsUsd":0.7,"foundryByokUsd":0,"localSavedUsd":0,"billedAiCredits":70,"failedToolCalls":5}`
- `P2`: `{"traceId":"22222222222222222222222222222222","conversationId":"conv-2","startTime":"2026-11-21T13:00:00.0000000+00:00","endTime":"2026-11-21T13:05:02.0000000+00:00","agents":["ledger-builder"],"models":["mai-code-2","qwen3.8-27b"],"spans":2,"unpricedSpans":0,"inputTokens":1400000,"outputTokens":112000,"cacheReadInputTokens":400000,"cacheWriteInputTokens":0,"aiCredits":56,"aiCreditsUsd":0.56,"foundryByokUsd":0,"localSavedUsd":0.23,"billedAiCredits":56,"failedToolCalls":0}`
- `P6`: `{"traceId":"66666666666666666666666666666666","conversationId":"conv-6","startTime":"2026-11-21T16:00:00.0000000+00:00","endTime":"2026-11-21T16:00:02.0000000+00:00","agents":[],"models":["mai-code-2"],"spans":1,"unpricedSpans":0,"inputTokens":10000,"outputTokens":200000,"cacheReadInputTokens":0,"cacheWriteInputTokens":0,"aiCredits":50.5,"aiCreditsUsd":0.505,"foundryByokUsd":0,"localSavedUsd":0,"billedAiCredits":50.5,"failedToolCalls":1}`

| Arguments | `orderBy`, `limit` | `prompts` |
|---|---|---|
| `{}` | `cost`, 5 | `P5` (rank 1), `P2` (2), `P6` (3) |
| `{"orderBy":"outputTokens","limit":2}` | `outputTokens`, 2 | `P6` (1), `P2` (2) |
| `{"orderBy":"failedToolCalls"}` | `failedToolCalls`, 5 | `P5` (1), `P6` (2), `P2` (3) |

Each result is `{"repository":"swigerb/copilot-ledger","branch":"feature/ledger","orderBy":…,"limit":…,"prompts":[…],"circuitBreaker":{"state":"closed","openTrips":[]},"warnings":[]}`, where each prompt is the object above with `"rank"` added first.

**MC6, agents.** Seeds. `get_agent_breakdown` with `{}`: the result is `{"repository":"swigerb/copilot-ledger","branch":"feature/ledger","agents":[{"agent":"(none)","spans":2,"unpricedSpans":0,"inputTokens":110000,"outputTokens":250000,"cacheReadInputTokens":0,"cacheWriteInputTokens":0,"aiCredits":120.5,"aiCreditsUsd":1.205,"foundryByokUsd":0,"localSavedUsd":0,"billedAiCredits":120.5},{"agent":"ledger-builder","spans":2,"unpricedSpans":0,"inputTokens":1400000,"outputTokens":112000,"cacheReadInputTokens":400000,"cacheWriteInputTokens":0,"aiCredits":56,"aiCreditsUsd":0.56,"foundryByokUsd":0,"localSavedUsd":0.23,"billedAiCredits":56}],"circuitBreaker":{"state":"closed","openTrips":[]},"warnings":[]}`.

**MC7, setting a budget.** Seeds.

- `set_branch_budget` with `{"aiCredits":200}`: the result is `{"repository":"swigerb/copilot-ledger","branch":"feature/ledger","budget":{"aiCredits":200,"spentAiCredits":176.5,"exceeded":false},"circuitBreaker":{"state":"closed","openTrips":[]},"warnings":[]}`. `GetBranchBudgetsAsync()` then has one row: that branch, budget 200, `SetAt` `NOW`.
- With `{"aiCredits":150.5,"repository":"swigerb/copilot-ledger","branch":"feature/ledger"}`: `budget` is `{"aiCredits":150.5,"spentAiCredits":176.5,"exceeded":true}`, and `warnings` is `["WARNING: swigerb/copilot-ledger @ feature/ledger has spent 176.50 of its 150.50 AI credit budget."]`.
- With `{"aiCredits":176.5}`: `budget` is `{"aiCredits":176.5,"spentAiCredits":176.5,"exceeded":false}`, and `warnings` is `[]`, because spend exactly at the budget doesn't exceed it.
- With `aiCredits` set to `JsonDocument.Parse("1e2").RootElement`: `budget` is `{"aiCredits":100,"spentAiCredits":176.5,"exceeded":true}`.

**MC8, invalid arguments** (theory). Each returns `IsError` true, one `TextContentBlock` with exactly the message, and a null `StructuredContent`. After the `set_branch_budget` cases, `GetBranchBudgetsAsync()` is empty.

| Tool | Arguments | Message |
|---|---|---|
| `get_branch_ledger` | `{"repository":"a/b"}` | `Pass repository and branch together, or neither.` |
| `get_branch_ledger` | `{"branch":"main"}` | `Pass repository and branch together, or neither.` |
| `get_branch_ledger` | `{"repository":"","branch":"main"}` | `repository must be a non-empty string.` |
| `get_branch_ledger` | `{"repository":"   ","branch":"main"}` | `repository must be a non-empty string.` |
| `get_branch_ledger` | `{"repository":"a/b","branch":5}` | `branch must be a non-empty string.` |
| `get_branch_ledger` | `{"extra":1,"another":2}` | `Unknown argument: another.` |
| `get_agent_breakdown` | `{"limit":5}` | `Unknown argument: limit.` |
| `get_top_expensive_prompts` | `{"limit":0}`, `{"limit":21}`, `{"limit":5.5}`, `{"limit":"5"}` | `limit must be an integer from 1 to 20.` |
| `get_top_expensive_prompts` | `{"orderBy":"Cost"}`, `{"orderBy":null}` | `orderBy must be cost, outputTokens, or failedToolCalls.` |
| `set_branch_budget` | `{}` | `aiCredits is required.` |
| `set_branch_budget` | `{"aiCredits":0}`, `{"aiCredits":-1}`, `{"aiCredits":1000000.01}`, `{"aiCredits":0.001}`, `{"aiCredits":"5"}` | `aiCredits must be a number greater than 0 and at most 1000000, with at most 2 decimal places.` |
| `set_branch_budget` | `{"aiCredits":5,"repository":"a/b"}` | `Pass repository and branch together, or neither.` |

**MC9, finding the branch.** Seeds, with `WorkingDirectory = <root>`:

- With no environment, `get_branch_ledger` `{}` returns `IsError` true and exactly `No Git repository at <root>. Pass repository and branch.`
- With `env["COPILOT_SESSION_CWD"] = <root>/repo`, the result is `BRANCH`.
- With `COPILOT_SESSION_CWD` set to `repo` (relative) or to `<root>/missing`, it's the same error as with no environment.

**MC10, an unknown tool.** `Client.CallToolAsync("nope", null)` throws `McpProtocolException` with `ErrorCode` `McpErrorCode.InvalidParams` and a `Message` containing `Unknown tool: nope` (the client's whole message is `Request failed (remote): Unknown tool: nope`).

**MC11, the `mcp` command.** Seeds and a `CliHarness`. Start `GhLedgerCli.InvokeAsync(["mcp"], harness.Context with { StandardInput = toServer.Reader.AsStream(), StandardOutput = toClient.Writer.AsStream(), WorkingDirectory = <root>/repo, Out = output, Error = error })`, where `output` and `error` are `StringWriter`s, and connect a client to the pipes as `McpTestHarness` does. `ListToolsAsync()` returns 4 tools, the client's `ServerInfo` (in a local, checked with `Assert.NotNull`) has `Version` equal to `GhLedgerCli.Version`, and `get_branch_ledger` `{}` returns `repository` `swigerb/copilot-ledger` and `branch` `feature/ledger`. Dispose the client and complete `toServer.Writer`: the command returns 0, and `output` is empty.

**MC12, an unavailable ledger.** Create the directory `<root>/dbdir` and use it as `DatabasePath`. `get_branch_ledger` with `{"repository":"a/b","branch":"main"}` returns `IsError` true, with text starting `The ledger at <root>/dbdir is unavailable: `.

### Task 5 tests

#### Install: `InstallCommandTests` (changed and new)

`RUN_VALUE` is `@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CopilotLedger"`. `AUTOSTART` is `<root>/home/.config/autostart/copilot-ledger.desktop`, and `AGENT` is `<root>/home/Library/LaunchAgents/io.github.swigerb.copilot-ledger.plist`.

Changed cases:

- **IN1.** The expected output gains `gh-ledger will start when you sign in.` between the `sh.exe` warning and the daemon line. Also assert `platform.StartupCommandWrites` equals `[("CopilotLedger", "\"C:\\tools\\gh-ledger.exe\" daemon start")]` and the manifest's `startupItem` is `RUN_VALUE`.
- **IN2.** The same line comes between `Set gh alias: …` and the daemon line, with the same two assertions.
- **IN3.** The second install's `Out` also contains `gh-ledger already starts when you sign in.`, and `StartupCommandWrites` has exactly one entry.
- **IN9.** The expected output gains `Would start gh-ledger when you sign in.` between the `sh.exe` warning and `Would start the gh-ledger daemon.`, and `StartupCommandWrites` is empty.
- **IN10.** `after-windows` also asserts `Out` contains `gh-ledger will no longer start when you sign in.` and `StartupCommandWrites[^1]` is `("CopilotLedger", null)`. `after-unix` also asserts `Out` contains that line and `AUTOSTART` doesn't exist.

New cases, using the existing `WindowsPlatform()` and `UnixPlatform(<root>)` helpers:

**IN11, Linux autostart.** `UnixPlatform(<root>)`.

- `install`: `Out` contains `gh-ledger will start when you sign in.`, and `File.ReadAllText(AUTOSTART)` is exactly this, plus a final `\n`:

  ```ini
  [Desktop Entry]
  Type=Application
  Name=CopilotLedger
  Comment=Starts the CopilotLedger daemon
  Exec="/opt/bin/gh-ledger" daemon start
  Terminal=false
  ```

  The manifest's `startupItem` is `AUTOSTART`.
- A second `install`: `Out` contains `gh-ledger already starts when you sign in.`
- `uninstall`: `Out` contains `gh-ledger will no longer start when you sign in.`, and `AUTOSTART` doesn't exist.

**IN12, `XDG_CONFIG_HOME`** (theory). `UnixPlatform(<root>)` with `ProcessVariables["XDG_CONFIG_HOME"]` set. With `<root>/xdg`, `install` writes `<root>/xdg/autostart/copilot-ledger.desktop`, and `<root>/home/.config` doesn't exist. With `relative/xdg`, it writes `AUTOSTART`.

**IN13, the macOS LaunchAgent.** `UnixPlatform(<root>)` with `IsMacOS = true`. `install`: `Out` contains `gh-ledger will start when you sign in.`, and `File.ReadAllText(AGENT)` is Task 5's property list with `EXE` = `/opt/bin/gh-ledger` and `LOG` = `SecurityElement.Escape(<root>/data/daemon.log)`, after `.ReplaceLineEndings("\n")`, plus a final `\n`. The manifest's `startupItem` is `AGENT`. `uninstall`: `Out` contains `gh-ledger will no longer start when you sign in.`, and `AGENT` doesn't exist.

**IN14, items that can't be registered** (theory).

- `windows-too-long`: `WindowsPlatform()` with `ProcessPath = @"C:\" + new string('a', 250) + @"\gh-ledger.exe"` (a 267-character path, so a 282-character command). `Out` contains `Warning: the gh-ledger path is too long for a sign-in command, so gh-ledger won't start when you sign in. Run gh-ledger daemon start after you sign in.`, `StartupCommandWrites` is empty, and the manifest's `startupItem` is `null`.
- `linux-characters`: `UnixPlatform(<root>)` with `ProcessPath = "/opt/my$tools/gh-ledger"`. `Out` contains `Warning: the gh-ledger path has characters that a desktop autostart entry can't run, so gh-ledger won't start when you sign in. Run gh-ledger daemon start after you sign in.`, `AUTOSTART` doesn't exist, and the manifest's `startupItem` is `null`.

Both installs exit 0.

**IN15, `COPILOT_LEDGER_HOME`.** `WindowsPlatform()` with `ProcessVariables["COPILOT_LEDGER_HOME"] = <root>/data`. `install` exits 0, and `Out` contains `Warning: COPILOT_LEDGER_HOME is set. The daemon that starts when you sign in uses it only if your sign-in environment sets it too.` before `gh-ledger will start when you sign in.`

**IN16, sign-in items that fail** (theory over a case name). Each `install` and `uninstall` exits 0.

- `windows-register-fails`: `WindowsPlatform()` with `StartupCommandException = new UnauthorizedAccessException("Access to the registry key is denied.")`. `install`'s `Out` contains `Warning: could not register the sign-in item: Access to the registry key is denied.` and `gh-ledger daemon started (pid <Environment.ProcessId>) on http://localhost:4318.`, and doesn't contain `gh-ledger will start when you sign in.`. `StartupCommandWrites` is empty, and the manifest's `startupItem` is `null`.
- `linux-register-fails`: `UnixPlatform(<root>)`, plus the directory `<root>/home/.config` holding an empty **file** named `autostart`, so creating the autostart folder throws `IOException`. `install`'s `Out` has a line starting with `Warning: could not register the sign-in item: ` (the rest is the OS's message), the manifest's `startupItem` is `null`, and `<root>/home/.config/autostart` is still a file.
- `windows-remove-fails`: `WindowsPlatform()`. `install` registers the item. Then set `StartupCommandException = new System.Security.SecurityException("Requested registry access is not allowed.")` and run `uninstall`: `Out` contains `Warning: could not remove the sign-in item: Requested registry access is not allowed.`, `StartupCommands` still holds `CopilotLedger`, and `Out` ends with `CopilotLedger is uninstalled. Your ledger data is still in <root>/data.` and `\n`.

#### Version

- **DM1.** The expected `version` in `daemon.json` is `0.3.0`.
- **`ReportCommandTests`.** The expected `status` text's first line is `gh-ledger 0.3.0`.

## Deferred to later sprints

- **Reconciliation with `nano_aiu`** (carried from Sprint 2). Store each `chat` span's `github.copilot.nano_aiu` and report drift per model, never the root `invoke_agent` total, which left out a subagent's spend. Protobuf carries it as an IEEE `double`, so this needs a decimal-only design.
- **`gh ledger` on Windows without Git for Windows** (carried from Sprint 2). A precompiled gh extension needs a `gh-*` repository with per-platform release assets. Creating it is the user's decision, paired with Sprint 4's release work.
- **A tool gate.** A user-level `preToolUse` hook that runs a small `gh-ledger` command and denies tools while the breaker is open would stop an agent even when it never calls a ledger tool. It needs the hook contract verified live.
- **Webhooks** as another alert channel.
- **Breaker state in `status`.** `status` doesn't show the breaker yet, so that its tested output stays unchanged.
- **MCP registration in `install`.** `install` could run `copilot mcp add` or edit `~/.copilot/mcp-config.json`, with `uninstall` undoing it.
- **`COPILOT_LEDGER_HOME` in sign-in items.** Pass the data directory to the sign-in item (a launchd `EnvironmentVariables` entry, an `env` prefix in the desktop entry, or a CLI option) instead of warning.
- **Headless Linux.** A `systemd --user` unit for machines without a desktop session.
- **Smarter retry churn.** Telling test and build commands from others needs the command text, which Copilot exports only with content capture. Revisit if Copilot adds a command attribute.
- **Carried from Sprint 2:** the binary location, a custom port in `install`, rates follow-ups, BYOK, local, and VS Code Local captures, file-exporter ingestion, Native AOT, and overflow-safe rollups, as described in `.github/plans/archive/sprint-2.md`.
- **Sprint 4,** per the roadmap: PR receipts, the GitHub Action, and the rates update command.
