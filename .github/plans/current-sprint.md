# Sprint 2: Zero-Docker OTLP Receiver & gh-ledger CLI

| | |
|---|---|
| Architect | `ledger-architect` (Claude Opus 5.5) |
| Builder | `ledger-builder` (MAI Code 2) |
| Target | .NET 10 (`net10.0`), SDK 10.0.x, zero Docker |
| Plan written | 2026-11-20, revised against merged Sprint 1 (`main` at `1809dc8`) |
| Status | Current sprint. Sprint 1 is complete and archived at `.github/plans/archive/sprint-1.md`. |

## How to use this plan

`ledger-builder` implements one task per run: the first unchecked `Task N:` line below. It follows that task's specification section, runs the verification commands, changes the line to `- [x]`, and stops. Tasks are sequential, and each builds on the ones before it.

Every task ships with its tests, listed under "Test specifications". A task is done only when `dotnet test` runs the whole suite, including its new cases, with 0 failed and 0 skipped.

## Tasks

- [ ] Task 1: OTLP Trace Decoding & gen_ai Span Normalization (CopilotLedger.Otlp). Create `src/CopilotLedger.Otlp`. Add `GenAiSpanLink` to Core, a dependency-free protobuf reader and a System.Text.Json reader for OTLP `ExportTraceServiceRequest`, and `GenAiSpanNormalizer`, which applies the gen_ai.* extraction rules to turn OTLP spans into `NormalizedGenAiSpan`s (inference spans only) and `GenAiSpanLink`s (trace structure). Tests use golden protobuf bytes, Copilot-shaped JSON, and malformed payloads.
- [ ] Task 2: Ledger Ingestion, Session Attribution & Rates Refresh (CopilotLedger.Otlp, Storage & Core). Add Migration 2 (`span_links`) and store methods for span links, agent back-fill, pruning, and prompt chains; `CachingGitContextResolver`; `CopilotSessionWorkspaceLocator`, which finds a Copilot session's working directory; and `LedgerIngestor`. Price every text model on GitHub's Copilot price table in `rates.json` (29 more models, with long-context tiers and promotional windows), and add the local provider names. Tests replay real Copilot trace shapes, including custom agents, reconcile with GitHub's own nano-AIU figures, and check every new price against an independent transcription of GitHub's table.
- [ ] Task 3: Rates Overlay Error Reporting (CopilotLedger.Core). Replace the rates loader's reflection-based deserialization with `RateCardReader`, a `JsonDocument` reader that owns every message. Validate the overlay by itself, so every error path points into the user's file, and label the two checks that need the built-in rates. A missing property gets its own path, and invalid JSON gets a line and column. Rates above 1,000,000 USD per 1M tokens are rejected, so `Calculate` never throws. Tests assert exact messages, and a test keeps the examples in `docs/configuration.md` identical to real output.
- [ ] Task 4: Kestrel OTLP Receiver & gh-ledger Daemon (CopilotLedger.Otlp & CopilotLedger.Cli). Add the loopback OTLP/HTTP receiver: `POST /v1/traces` as protobuf and JSON, with gzip, size limits, and spec status codes; `/v1/metrics` and `/v1/logs` are accepted and discarded. Create `src/CopilotLedger.Cli` (`gh-ledger`) on System.CommandLine with `daemon run|start|stop` and `rates init|validate`. Tests use `Microsoft.AspNetCore.TestHost`.
- [ ] Task 5: Reporting Commands, One-Command Install & Single-Binary Packaging (CopilotLedger.Cli). Add `status`, `branch`, `agents`, and `top-prompts` (tables and `--json`); `install` and `uninstall` (`COPILOT_OTEL_ENABLED`, the `gh ledger` alias, and the daemon); single-file self-contained publish settings; a CI publish smoke test; and `docs/cli.md`.

## Starting point

- Sprint 1 is complete. Tasks 1–3 merged in PRs #4, #5, and #6 (`main` at `1809dc8`). `dotnet test CopilotLedger.slnx` passes 143 tests (0 failed, 0 skipped), and CI is green on `ubuntu-latest` and `windows-latest`. Sprint 2 builds on the Core contracts, `JsonRateCardCalculator`, `FileSystemGitContextResolver`, `GitRemoteUrl`, `LedgerPaths`, `SqliteLedgerStore` with Migration 1, and the test support (`TempDirectory` with its `Path` property, `TestSpans.Chat(...)`, and `RateFixtures.ByokTestRatesJson`).
- Merged Sprint 1 behavior that this plan depends on (read from the code at `1809dc8`):
  - `SqliteLedgerStore.RecordAsync` validates the whole batch before writing. It throws `ArgumentException` if any entry has status `NotBillableOperation` or a negative token count, so nothing is written.
  - It then inserts every row in one transaction, converting `UsdCost` and `EstimatedCloudSavedUsd` with `PicoUsd.FromUsd`. That throws `ArgumentOutOfRangeException` for an amount above `long.MaxValue` pico-USD (about $9.22 million), which rolls back the whole batch.
  - `JsonRateCardCalculator.Calculate` can throw `OverflowException` when an overlay rate is extreme. That contradicts `ICreditCalculator`'s contract ("Throws only ArgumentNullException") and the archived Sprint 1 plan's "`Calculate` never throws" (Task 2 specification, `JsonRateCardCalculator`, step 1). `ValidateRate` bounds a rate's size only by requiring `rate × 1,000,000` to fit in a `decimal`, so a rate such as 10^22 per 1M tokens loads, and pricing 10,000,000 tokens with it overflows. The usage checks can't overflow `long`, so rate size is the only way `Calculate` throws. Task 3 restores the contract by rejecting rates above 1,000,000 USD per 1M tokens (decision 18). Task 2's ingestor, which lands first, catches `OverflowException`, and it keeps that catch afterward as defense in depth.
  - Inserts end with `ON CONFLICT (trace_id, span_id) DO NOTHING`, so re-sending a span never duplicates it.
  - Task 2's "Filtering and per-span errors" keeps every one of these failures away from `RecordAsync`.
  - Rates errors are `InvalidDataException`s whose message is `<file>: <JSON path>: <problem>`. The loader validates the *merged* document, so a path into the user's overlay uses merged indices: the overlay's `localOffload.models[0]` is reported as `localOffload.models[1]`, behind the built-in model. Errors that System.Text.Json finds pass its text through, with .NET type names and a ` Path: $.… | LineNumber: … | BytePositionInLine: ….` suffix, and a missing required property is reported at its object's path. Task 3 replaces all of this.
  - `TempDirectory.Dispose()` calls `SqliteConnection.ClearAllPools()` before deleting its folder. On Windows a pooled connection keeps the SQLite file open, so the folder can't be deleted otherwise. `Dispose` ignores delete failures, so the symptom would be leftover temp folders, not failing tests. The shared test rules extend this pattern to every new test.
- `.github/workflows/ci.yml` was added in Sprint 1 Task 1 as an architect amendment. It runs `dotnet build CopilotLedger.slnx` and `dotnet test CopilotLedger.slnx` on `ubuntu-latest` and `windows-latest` for pushes to `main`, pull requests, and manual runs. Task 5 extends it.
- Sprint 1's public contracts are fixed. Sprint 2 adds types and members. It changes only these Sprint 1 artifacts, each called out in its task:
  - Task 2: `src/CopilotLedger.Core/Pricing/rates.json` (29 models, three notes, and four local provider names) and `docs/configuration.md` (the Local row and the built-in prices);
  - Task 2: `LedgerMigrations` (Migration 2), and test S1's expected `PRAGMA user_version`, which changes from 1 to 2;
  - Task 2: `SqliteLedgerStore` (four new public methods; existing members unchanged);
  - Task 3: the rates loader's internals in `JsonRateCardCalculator` and `RateCardDocument` (its public API is unchanged, and it now rejects rates above 1,000,000), `docs/configuration.md` (the price-range sentence and the invalid-file paragraph), the Sprint 1 rates tests in `RateOverlayTests` and `RateCardLoadingTests` (they now assert exact messages), and the test project file (it copies the docs into the test output);
  - Tasks 4 and 5: `docs/configuration.md` (the data-file table and the rates commands).
- The test project uses xUnit v2 (`xunit` 2.9.3).
- When an acceptance criterion says `src/` contains no `double` or `float`, it means the C# keywords and the `GetDouble` and `TryGetDouble` calls. The enum member `OtlpValueKind.Double` names a protobuf wire type and is allowed.

## Verified facts (retrieved 2026-11-20)

### OTLP/HTTP

Source: [OTLP Specification 1.11.1](https://opentelemetry.io/docs/specs/otlp/).

- Clients `POST` to `/v1/traces`, `/v1/metrics`, and `/v1/logs`. The default port is 4318.
- Binary protobuf uses `Content-Type: application/x-protobuf`, and JSON uses `application/json`. "The server MUST use the same Content-Type in the response as it received in the request." Servers SHOULD accept both encodings on the same port.
- "All server components MUST support" no compression and gzip (`Content-Encoding: gzip`).
- The server "MUST limit the size of the request body when parsing it, including after decompression"; 64 MiB is RECOMMENDED. When the limit is exceeded, the server "MUST respond with `HTTP 413 Content Too Large`".
- Full success is `200 OK` with an `ExportTraceServiceResponse` whose `partial_success` is unset. An empty request SHOULD succeed.
- Partial success is `200 OK` with `partial_success.rejected_spans` and `error_message` set. The client MUST NOT retry it.
- Every 4xx and 5xx response body is a `google.rpc.Status` message. The spec calls it "Protobuf-encoded", and the same-Content-Type rule above applies, so CopilotLedger writes it as JSON for JSON requests. The server MAY omit `Status.code`, and `Status.message` SHOULD be a developer-facing message. Undecodable data gets `400 Bad Request`, which the client must not retry. `429`, `502`, `503`, and `504` are retryable, optionally with `Retry-After`.
- OTLP/JSON deviates from the standard proto3 JSON mapping:
  - `traceId` and `spanId` are case-insensitive hex strings;
  - enum values are integers;
  - receivers MUST ignore unknown fields;
  - keys are lowerCamelCase only, and the original field names "are not valid".

  64-bit integers are decimal strings, and "either numbers or strings are accepted when decoding".
- Decoders SHOULD replace invalid UTF-8 with U+FFFD. Clients may resend data, so duplicates are expected.

### OTLP protobuf schema

Sources: `open-telemetry/opentelemetry-proto` v1.11.1 (`trace/v1/trace.proto`, `common/v1/common.proto`, `resource/v1/resource.proto`, and `collector/trace/v1/trace_service.proto`) and `googleapis/google/rpc/status.proto`.

| Message | Fields used by CopilotLedger (number: name, wire type) |
|---|---|
| `ExportTraceServiceRequest` | 1: `resource_spans` (LEN, repeated) |
| `ResourceSpans` | 1: `resource` (LEN); 2: `scope_spans` (LEN, repeated); 3: `schema_url` is skipped |
| `Resource` | 1: `attributes` (LEN, repeated `KeyValue`) |
| `ScopeSpans` | 2: `spans` (LEN, repeated); 1: `scope` and 3: `schema_url` are skipped |
| `Span` | 1: `trace_id` (LEN, 16 bytes); 2: `span_id` (LEN, 8 bytes); 4: `parent_span_id` (LEN, 8 bytes); 5: `name` (LEN); 7: `start_time_unix_nano` (I64, `fixed64`); 8: `end_time_unix_nano` (I64); 9: `attributes` (LEN, repeated `KeyValue`) |
| `KeyValue` | 1: `key` (LEN); 2: `value` (LEN, `AnyValue`) |
| `AnyValue` (oneof) | 1: `string_value` (LEN); 2: `bool_value` (VARINT); 3: `int_value` (VARINT, `int64`); 4: `double_value` (I64); 5: `array_value` (LEN); 6: `kvlist_value` (LEN); 7: `bytes_value` (LEN) |
| `ExportTraceServiceResponse` | 1: `partial_success` (LEN, `ExportTracePartialSuccess`) |
| `ExportTracePartialSuccess` | 1: `rejected_spans` (VARINT, `int64`); 2: `error_message` (LEN) |
| `google.rpc.Status` | 1: `code` (VARINT, omitted); 2: `message` (LEN) |

Wire types: 0 = VARINT, 1 = I64, 2 = LEN, 5 = I32. Types 3 and 4 are the deprecated groups. A trace ID that is all zeros or not 16 bytes is invalid, and so is a span ID that is all zeros or not 8 bytes. An empty `parent_span_id` marks a root span.

### GitHub Copilot OpenTelemetry

Sources:

- GitHub Docs, [OpenTelemetry reference](https://docs.github.com/en/copilot/reference/enterprise-administrators/opentelemetry-reference);
- the Copilot CLI's built-in `copilot help monitoring` (version 1.0.112);
- GitHub Docs, the [Copilot CLI command reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference) and [configuration directory reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference);
- GitHub Docs, [Enterprise managed settings](https://docs.github.com/en/copilot/reference/enterprise-administrators/enterprise-managed-settings);
- VS Code, [Monitor agent usage with OpenTelemetry](https://code.visualstudio.com/docs/agents/guides/monitoring-agents).

The shared Copilot runtime serves Copilot CLI, the GitHub Copilot app, and VS Code Agent Host sessions:

- OTel is off by default. It turns on when `COPILOT_OTEL_ENABLED=true`, `OTEL_EXPORTER_OTLP_ENDPOINT`, or `COPILOT_OTEL_FILE_EXPORTER_PATH` is set.
- The default exporter is `otlp-http`. The default protocol is `http/json`, and `http/protobuf` is the alternative; `grpc` "warn[s] and fall[s] back". The default endpoint is `http://localhost:4318`.
- The GitHub Docs page warns that export to an `http://` endpoint is disabled. The CLI's own help narrows the condition: "When any of these TLS variables is set and the otlp-http exporter is in use but the endpoint resolves to http:// (including the default http://localhost:4318), export is disabled". The TLS variables are `OTEL_EXPORTER_OTLP_CERTIFICATE`, `OTEL_EXPORTER_OTLP_CLIENT_CERTIFICATE`, `OTEL_EXPORTER_OTLP_CLIENT_KEY`, and their `_TRACES_` variants. The live capture below confirms plain HTTP export when none of them is set.
- The OTLP exporter is a "native (Rust) HTTP transport".
- Personal configuration uses environment variables. The `telemetry` settings object exists only in device-wide enterprise managed settings.
- Content capture is off by default.
- `gen_ai.conversation.id` is the session identifier. `~/.copilot/session-state/` holds session data "organized by session ID", and `COPILOT_HOME` replaces `~/.copilot`.
- "To measure AI unit consumption, read `github.copilot.nano_aiu` from the root `invoke_agent` span only." The custom-agent capture below found a root span whose total left out a subagent's spend.
- For bring-your-own-key and local models, `providers[].modelProvider` in Copilot CLI's `providers.json` is "a provider label used only for telemetry". Its recognized values are `openai`, `anthropic`, `azure_openai`, `ollama`, `lm_studio`, `foundry_local`, and `llama_cpp`.
- VS Code's Local agent harness is configured differently:
  - `COPILOT_OTEL_ENDPOINT` takes precedence over `OTEL_EXPORTER_OTLP_ENDPOINT`, and environment variables take precedence over settings.
  - `COPILOT_OTEL_ENABLED=true` turns OTel on. The default endpoint is `http://localhost:4318` and the default protocol is `http/protobuf`.
  - `service.name` is `copilot-chat`.
  - `invoke_agent` spans carry `github.copilot.git.repository`, `github.copilot.git.branch`, and `github.copilot.git.commit_sha`. The legacy names are `copilot_chat.repo.remote_url`, `copilot_chat.repo.head_branch_name`, and `copilot_chat.repo.head_commit_hash`.
  - `chat` spans report `gen_ai.usage.cache_creation.input_tokens`.

### Live capture

On 2026-11-20 the architect ran a scratch loopback receiver on `localhost:4318`, outside the repository, and sent two prompts with Copilot CLI 1.0.112 on Windows 11 ARM64. Only `COPILOT_OTEL_ENABLED=true` was set for those processes. The first prompt used `mai-code-2`. The second used `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` and an `explore` subagent.

1. Copilot sent `POST /v1/traces` and `POST /v1/metrics` with `Host: localhost:4318` over HTTP/1.1, uncompressed (no `Content-Encoding`), as `application/json` by default or `application/x-protobuf`. The User-Agent was `OTel-OTLP-Exporter-Rust/0.33.1`.
2. In JSON, `intValue`, `startTimeUnixNano`, and `endTimeUnixNano` were strings, `kind` was an integer, and a root span's `parentSpanId` was `""`. IDs were lowercase hex. Fields CopilotLedger doesn't use were present, such as `entityRefs`, `flags`, `events`, `links`, and `status`.
3. Resource attributes were `service.name` (`github-copilot`), `service.version`, and `service.instance.id`. The scope was `github.copilot`.
4. On `chat` spans:
   - There was no `server.address` or `server.port` for GitHub-hosted models.
   - `gen_ai.provider.name` was `github`, and the request and response models were `mai-code-2` and `gpt-6-luna`.
   - `gen_ai.usage.cache_read.input_tokens` and `gen_ai.usage.cache_write.input_tokens` appeared only when non-zero.
   - There was no `gen_ai.agent.name`.
   - `github.copilot.nano_aiu` and `github.copilot.cost` were `doubleValue`.
5. The default top-level agent's `invoke_agent` span had no `gen_ai.agent.name`, only `github.copilot.agent.type: builtin`. The subagent's `invoke_agent` span had `gen_ai.agent.name: explore` and was a child of an `execute_tool task` span. The subagent's `execute_tool` spans also carried `gen_ai.agent.name`.
6. Spans are exported as they end. `chat` spans arrived before their `invoke_agent` ancestors, and the root `invoke_agent` arrived in a later request.
7. `gen_ai.conversation.id` equaled the `--session-id` passed to the CLI. `~/.copilot/session-state/<id>/workspace.yaml` held a top-level `cwd:` line with the session's working directory as a plain YAML scalar. The GitHub Copilot app's sessions (`client_name: github/autopilot`) have the same file.
8. **Billing reconciliation.** For each `chat` span, `nano_aiu / 10^9` equaled the AI credits from Sprint 1's formula at GitHub's published rates: `mai-code-2` 0.69071, 0.13053, and 0.69766; `gpt-6-luna` 0.047955 and 0.0051795. Copilot's `--usage-output-file` totals of 690,710,000 and 881,324,500 nano-AIU agree.
9. `input_tokens` includes cache reads and writes. For example, the subagent's `gpt-6-luna` input of 7,429 = 6 uncached + 3,662 cache read + 3,761 cache write.

### Live capture: custom agents

On 2026-11-20 the architect repeated the capture for custom agents. It used Copilot CLI 1.0.112 with `mai-code-2-flash`, the scratch receiver, and a scratch Git repository outside this one, with `COPILOT_OTEL_ENABLED=true` set only for those processes. User-level Copilot configuration wasn't changed. The probe agent `.github/agents/ledger-probe.agent.md` declared `name: Ledger Probe Display`; a second probe, `ledger-probe-noname.agent.md`, declared no `name`. The three runs cost 0.103028 AI credits: `--usage-output-file` totals of 24,095,000, 62,080,000, and 16,853,000 nano-AIU.

1. **Run A,** `copilot --agent ledger-probe`. The root span was `invoke_agent Ledger Probe Display`, with `gen_ai.agent.name` = `Ledger Probe Display` (the agent's `name`, not its file ID), `gen_ai.agent.description`, and `github.copilot.agent.type` = `custom`. Its one `chat` span was the root's child and had no `gen_ai.agent.name`.
2. **Run B,** the default agent calling the probe agent with the `task` tool. The root `invoke_agent` had no `gen_ai.agent.name`, and `github.copilot.agent.type` was `builtin`. The main agent's two `chat` spans were the root's children. So was `execute_tool task`, which carried `gen_ai.transfer.target.name` = `Ledger Probe Display`. Its child was `invoke_agent Ledger Probe Display`, with `gen_ai.agent.name` = `Ledger Probe Display` and type `custom`, and that span's one `chat` child again had no agent name.
3. **Run C,** `copilot --agent ledger-probe-noname`. `gen_ai.agent.name` was `ledger-probe-noname`: the file name without `.agent.md`.
4. Each run's spans arrived in one export request. Every `chat` span's `nano_aiu / 10^9` equaled Sprint 1's formula at `mai-code-2-flash`'s promotional price: 0.024095 (run A); 0.02885, 0.028335, and 0.004895 (run B); and 0.016853 (run C).
5. Run B's root `invoke_agent` reported `github.copilot.nano_aiu` 57,185,000, which covers only the main agent's two `chat` spans. The subagent's 4,895,000 appeared only on its own `invoke_agent` and `chat` spans. The usage file's total of 62,080,000 is the sum of all three `chat` spans. So Copilot's advice to read the root span "only" would have missed the custom subagent's spend. CopilotLedger prices `chat` spans, so it counts that spend.

The design already matches these runs. The normalizer reads `gen_ai.agent.name` only into links, and back-fill gives each `chat` row the name of its nearest `invoke_agent` ancestor without crossing a nested one. A custom agent's spend therefore appears under its `name`, or its file ID when it has no `name`, and the default agent's spend appears under `(none)`. This repository's agents declare `name: ledger-architect` and `name: ledger-builder`, so their spend appears under those names. IG9 replays runs A and B.

### GitHub price table

Source: [Models and pricing for GitHub Copilot](https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing), retrieved 2026-11-20. Prices are USD per 1M tokens.

The architect transcribed the page twice, once by parsing its HTML and once from a separate Markdown rendering, and the two transcriptions agree on all 48 rows below. RR1's expected values were computed from that transcription with decimal math, independently of `rates.json`. They agree with a second computation, and with the merged Sprint 1 calculator loading the Task 2 `rates.json`.

The table lists every text model; the page's one image model, GPT Image 2.5 Flare, is left out, and its "Fine-tuned (GitHub)" table is empty. **Cache write** "none" means the page gives no cache-write price, so cache writes bill at the input price, which is Sprint 1's rule for a null `cacheWritePer1M`. **ID source** is explained after the table. **Built in** says which sprint prices the model.

| Provider | Model on the page | Model ID | ID source | Built in | Tier | Threshold | Input | Cached input | Cache write | Output |
|---|---|---|---|---|---|---|---|---|---|---|
| OpenAI | GPT-5.3-Codex | `gpt-5.3-codex` | model picker | Task 2 | Default | Not applicable | 1.75 | 0.175 | none | 14.00 |
| OpenAI | GPT-5.4 nano | `gpt-5.4-nano` | display name | Task 2 | Default | Not applicable | 0.20 | 0.02 | none | 1.25 |
| OpenAI | GPT-5.6 Luna | `gpt-5.6-luna` | model picker | Task 2 | Default | ≤ 200K | 0.20 | 0.02 | 0.25 | 1.20 |
| OpenAI | GPT-5.6 Luna | `gpt-5.6-luna` | model picker | Task 2 | Long context | > 200K | 0.40 | 0.04 | 0.50 | 1.80 |
| OpenAI | GPT-5.6 Sol | `gpt-5.6-sol` | model picker | Task 2 | Default | ≤ 272K | 4.00 | 0.40 | 5.00 | 20.00 |
| OpenAI | GPT-5.6 Sol | `gpt-5.6-sol` | model picker | Task 2 | Long context | > 272K | 8.00 | 0.80 | 10.00 | 30.00 |
| OpenAI | GPT-6 Astra | `gpt-6-astra` | model picker | Sprint 1 | Default | ≤ 272K | 10.00 | 1.00 | 12.50 | 50.00 |
| OpenAI | GPT-6 Astra | `gpt-6-astra` | model picker | Sprint 1 | Long context | > 272K | 20.00 | 2.00 | 25.00 | 75.00 |
| OpenAI | GPT-6 Luna | `gpt-6-luna` | model picker | Task 2 | Default | ≤ 272K | 0.10 | 0.01 | 0.125 | 0.50 |
| OpenAI | GPT-6 Luna | `gpt-6-luna` | model picker | Task 2 | Long context | > 272K | 0.20 | 0.02 | 0.25 | 0.75 |
| OpenAI | GPT-6 Sol | `gpt-6-sol` | model picker | Task 2 | Default | ≤ 272K | 2.00 | 0.20 | 2.50 | 10.00 |
| OpenAI | GPT-6 Sol | `gpt-6-sol` | model picker | Task 2 | Long context | > 272K | 4.00 | 0.40 | 5.00 | 15.00 |
| OpenAI | GPT-6.1 Sol | `gpt-6.1-sol` | model picker | Sprint 1 | Default | ≤ 272K | 2.00 | 0.10 | 2.50 | 10.00 |
| OpenAI | GPT-6.1 Sol | `gpt-6.1-sol` | model picker | Sprint 1 | Long context | > 272K | 4.00 | 0.20 | 5.00 | 15.00 |
| OpenAI | GPT-6.1 Terra | `gpt-6.1-terra` | model picker | Task 2 | Default | ≤ 272K | 1.00 | 0.05 | 1.25 | 5.00 |
| OpenAI | GPT-6.1 Terra | `gpt-6.1-terra` | model picker | Task 2 | Long context | > 272K | 2.00 | 0.10 | 2.50 | 7.50 |
| OpenAI | GPT-6.2 Astra | `gpt-6.2-astra` | model picker | Task 2 | Default | ≤ 272K | 8.00 | 0.40 | 10.00 | 40.00 |
| OpenAI | GPT-6.2 Astra | `gpt-6.2-astra` | model picker | Task 2 | Long context | > 272K | 16.00 | 0.80 | 20.00 | 60.00 |
| OpenAI | GPT-6.2 Sol | `gpt-6.2-sol` | model picker | Task 2 | Default | ≤ 272K | 2.00 | 0.10 | 2.50 | 10.00 |
| OpenAI | GPT-6.2 Sol | `gpt-6.2-sol` | model picker | Task 2 | Long context | > 272K | 4.00 | 0.20 | 5.00 | 15.00 |
| Anthropic | Claude Haiku 4.5 | `claude-haiku-4.5` | model picker | Task 2 | Default | Not applicable | 1.00 | 0.10 | 1.25 | 5.00 |
| Anthropic | Claude Haiku 5.5 | `claude-haiku-5.5` | model picker | Task 2 | Default | ≤ 100K | 0.10 | 0.01 | 0.125 | 0.50 |
| Anthropic | Claude Haiku 5.5 | `claude-haiku-5.5` | model picker | Task 2 | Long context | > 100K | 0.50 | 0.05 | 0.625 | 2.50 |
| Anthropic | Claude Sonnet 4 | `claude-sonnet-4` | display name | Task 2 | Default | Not applicable | 3.00 | 0.30 | 3.75 | 15.00 |
| Anthropic | Claude Sonnet 4.6 | `claude-sonnet-4.6` | CLI reference | Task 2 | Default | Not applicable | 3.00 | 0.30 | 3.75 | 15.00 |
| Anthropic | Claude Opus 4.8 | `claude-opus-4.8` | display name | Task 2 | Default | Not applicable | 5.00 | 0.50 | 6.25 | 25.00 |
| Anthropic | Claude Opus 5.5 | `claude-opus-5.5` | model picker | Sprint 1 | Default | Not applicable | 4.00 | 0.20 | 5.00 | 20.00 |
| Anthropic | Claude Sonnet 5 | `claude-sonnet-5` | model picker | Task 2 | Default | Not applicable | 2.00 | 0.20 | 2.50 | 10.00 |
| Anthropic | Claude Sonnet 5.5 | `claude-sonnet-5.5` | model picker | Sprint 1 | Default | Not applicable | 2.00 | 0.10 | 2.50 | 10.00 |
| Anthropic | Claude Opus 4.8 (fast mode) (preview) | none | unknown | no | Default | Not applicable | 10.00 | 1.00 | 12.50 | 50.00 |
| Anthropic | Claude Fable 5 | `claude-fable-5` | display name | Task 2 | Default | Not applicable | 10.00 | 1.00 | 12.50 | 50.00 |
| Anthropic | Claude Fable 5.1 | `claude-fable-5.1` | display name | Task 2 | Default | Not applicable | 10.00 | 0.25 | 12.50 | 50.00 |
| Anthropic | Claude Fable 5.2 | `claude-fable-5.2` | display name | Task 2 | Default | Not applicable | 8.00 | 0.20 | 10.00 | 40.00 |
| Google | Gemini 3.8 Flash | `gemini-3.8-flash` | model picker | Task 2 | Default | Not applicable | 0.75 | 0.075 | none | 3.75 |
| Google | Gemini 4 Argon | `gemini-4-argon` | model picker | Task 2 | Default | Not applicable | 2.00 | 0.10 | none | 10.00 |
| Google | Gemini 4 Carbon | `gemini-4-carbon` | model picker | Task 2 | Default | Not applicable | 2.00 | 0.10 | none | 10.00 |
| Google | Gemini 4 Hydrogen | `gemini-4-hydrogen` | model picker | Task 2 | Default | Not applicable | 1.50 | 0.075 | none | 7.50 |
| Google | Gemini 4 Neon | `gemini-4-neon` | display name | Task 2 | Default | Not applicable | 3.00 | 0.15 | none | 15.00 |
| Microsoft | MAI-Code-1.1-Flash | `mai-code-1.1-flash` | model picker | Task 2 | Default | Not applicable | 0.20 | 0.02 | none | 1.20 |
| Microsoft | MAI Code 2 | `mai-code-2` | model picker | Sprint 1 | Default | Not applicable | 0.50 | 0.025 | none | 2.50 |
| Microsoft | MAI Code 2 Flash | `mai-code-2-flash` | model picker | Task 2 | Default | Not applicable | 0.05 | 0.005 | none | 0.25 |
| xAI | Grok 4.5 | `grok-4.5` | display name | Task 2 | Default | ≤ 200K | 2.00 | 0.50 | none | 6.00 |
| xAI | Grok 4.5 | `grok-4.5` | display name | Task 2 | Long context | > 200K | 4.00 | 1.00 | none | 12.00 |
| xAI | Grok 4.6 | `grok-4.6` | model picker | Task 2 | Default | ≤ 200K | 2.00 | 0.50 | none | 6.00 |
| xAI | Grok 4.6 | `grok-4.6` | model picker | Task 2 | Long context | > 200K | 4.00 | 1.00 | none | 12.00 |
| xAI | Grok 4.7 | `grok-4.7` | model picker | Task 2 | Default | ≤ 200K | 2.00 | 0.50 | none | 6.00 |
| xAI | Grok 4.7 | `grok-4.7` | model picker | Task 2 | Long context | > 200K | 4.00 | 1.00 | none | 12.00 |
| Moonshot AI | Kimi K3 | `kimi-k3` | display name | Task 2 | Default | Not applicable | 3.00 | 0.30 | none | 15.00 |

Promotional prices, quoted from the page's footnotes (input / cached input / output):

| Model ID | Page text | Window in `rates.json` |
|---|---|---|
| `gemini-3.8-flash` | 0.75 / 0.075 / 3.75 "through December 31, 2026". No later price. | Until `2027-01-01T00:00:00Z`; nothing after |
| `gemini-4-argon` | 2.00 / 0.10 / 10.00 "until December 1, 2026". No later price. | Until `2026-12-01T00:00:00Z`; nothing after |
| `gemini-4-carbon` | 2.00 / 0.10 / 10.00 "until December 1, 2026. Starting December 1, 2026, pricing is" 4.00 / 0.20 / 20.00. | Until `2026-12-01T00:00:00Z`, then the later price |
| `mai-code-2` (Sprint 1) | 0.50 / 0.025 / 2.50 "until May 17, 2027. Starting May 17, 2027, pricing is" 1.00 / 0.05 / 5.00. | Until `2027-05-17T00:00:00Z`, then the later price |
| `mai-code-2-flash` | 0.05 / 0.005 / 0.25 "until May 17, 2027. Starting May 17, 2027, pricing is" 0.10 / 0.01 / 0.50. | Until `2027-05-17T00:00:00Z`, then the later price |

The page gives dates without a time or time zone. Its own wording makes "until December 1" and "Starting December 1" meet at the start of that day, and Sprint 1 ended `mai-code-2`'s promotion at `2027-05-17T00:00:00Z`. Every window here ends at 00:00:00Z on the date the page names (assumption A4). "Through December 31, 2026" includes that day, so it ends at `2027-01-01T00:00:00Z`. The page's HTML also wraps the Argon and Carbon notes in `<!-- expires 2026-12-01 -->` and the MAI notes in `<!-- expires 2027-05-17 -->`.

Model IDs:

- **Model picker (24).** The coordinator read these from the GitHub Copilot app's model picker on the user's machine: `claude-sonnet-5.5`, `claude-sonnet-5`, `claude-opus-5.5`, `claude-haiku-5.5`, `claude-haiku-4.5`, `gpt-6.1-sol`, `gpt-6.1-terra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-6-astra`, `gpt-5.6-sol`, `gpt-5.6-luna`, `gpt-5.3-codex`, `mai-code-1.1-flash`, `gemini-4-argon`, `gemini-3.8-flash`, `grok-4.7`, `grok-4.6`, `gemini-4-carbon`, `gemini-4-hydrogen`, `gpt-6.2-astra`, `gpt-6.2-sol`, `mai-code-2-flash`, and `mai-code-2`.
- **CLI reference (1).** The Copilot CLI command reference lists `claude-sonnet-4.6` as the default model of the built-in `code-review`, `general-purpose`, and `security-review` agents. The same table lists `gpt-5.6-luna` for `explore` and `task` (with `task` falling back to `claude-haiku-4.5`) and `claude-haiku-4.5` for `research`. The live capture showed `explore` running `gpt-6-luna` on CLI 1.0.112.
- **Display name (9).** No source lists the other IDs. The Copilot CLI 1.0.112 binary carries no model catalog. The GitHub Copilot app caches only the models enabled for the signed-in account. The supported-models page lists display names only. Each of the 25 verified IDs equals its display name in lowercase with spaces as `-`, which is what Sprint 1's model normalization produces. So `gpt-5.4-nano`, `claude-sonnet-4`, `claude-opus-4.8`, `claude-fable-5`, `claude-fable-5.1`, `claude-fable-5.2`, `gemini-4-neon`, `grok-4.5`, and `kimi-k3` use the same rule. Claude Sonnet 4 is on the pricing page but not on the supported-models page; it's included because the pricing page is what GitHub bills.
- **Left out (1).** For Claude Opus 4.8 (fast mode) (preview), the rule gives `claude-opus-4.8-(fast-mode)-(preview)`, which isn't a plausible ID. The app's model cache has `gpt-5.6-sol-fast`, which hints at a `-fast` suffix, but no fast-mode Claude ID was seen. Users can price it with an overlay.
- **Not on the pricing page.** The CLI reference also names `gpt-5.4` and `gemini-3.7-flash`, and the app's cache has `gpt-5.6-sol-fast` and `gpt-5.6-terra`. GitHub publishes no price for them, so they stay `unknown_model`.

### GitHub CLI

Sources: `gh alias set --help` (gh 2.103.3), and `cli/cli` `trunk`: `pkg/cmd/extension/manager.go` and `pkg/findsh/find_windows.go`. The alias behavior was checked against a temporary `GH_CONFIG_DIR`.

- `gh alias set ledger <expansion> --shell --clobber` stores a shell alias. Shell aliases run through `sh`, and extra arguments reach the expansion as `"$@"`.
  - Without `--clobber`, setting an existing alias fails with exit code 1.
  - `gh alias delete ledger` exits 0. When the alias is missing, it prints `no such alias ledger` and exits 1.
- On Windows, gh needs `sh.exe` to run shell aliases and local (non-binary) extensions. It searches `PATH` for `sh`, then `<git dir>\..\bin\sh.exe`, then `<git dir>\..\apps\git\current\bin\sh.exe`. Without one it fails with `unable to locate sh to execute the shell alias with. The sh.exe interpreter is typically distributed with Git for Windows.` The user's Windows ARM64 machine has only Copilot's bundled MinGit (`usr\bin\sh.exe`), which gh doesn't find.
- Extensions must live in repositories named `gh-*`. Precompiled extensions install from release assets; local extensions installed with `gh extension install .` run through `sh` on Windows.

### .NET and packages

The architect verified these with scratch builds in the session folder, outside the repository.

- The machine's configured NuGet feed resolves System.CommandLine 2.0.15, Microsoft.AspNetCore.TestHost 10.0.15, and Microsoft.Data.Sqlite 10.0.15. Newer majors also exist (System.CommandLine 3.0.0, TestHost 11.0.0, and Microsoft.Data.Sqlite 11.0.0), so the plan pins versions.
- These work together: `WebApplication.CreateEmptyBuilder(new WebApplicationOptions())`, `builder.WebHost.UseKestrelCore()`, `builder.Services.AddRoutingCore()`, and `ConfigureKestrel(k => k.ListenLocalhost(port))`.
  - `ListenLocalhost` binds `127.0.0.1` and `::1`.
  - `CreateEmptyBuilder` registers `ConsoleLifetime`.
  - A port conflict throws `System.IO.IOException` (`Failed to bind to address http://127.0.0.1:N: address already in use.`) with an inner `Microsoft.AspNetCore.Connections.AddressInUseException`.
- A `Microsoft.NET.Sdk` project with `<FrameworkReference Include="Microsoft.AspNetCore.App" />` gets no ASP.NET Core implicit usings, so files need explicit `using` directives such as `Microsoft.AspNetCore.Builder` and `Microsoft.AspNetCore.Http`.
- Under `UseTestServer()`, requests carry `Host: localhost` and `Connection.RemoteIpAddress` is null. Kestrel's `MaxRequestBodySize` isn't enforced. An unmapped path returns 404, and a mapped path with the wrong method returns 405.
- System.CommandLine 2.0.15 APIs:
  - `new Option<T>("--name") { Description = ..., DefaultValueFactory = _ => ... }`;
  - `option.Validators.Add(r => r.AddError(...))` and `command.Validators.Add(...)`;
  - `command.SetAction((ParseResult, CancellationToken) => Task<int>)` and `parseResult.GetValue(option)`;
  - `rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration { Output = ..., Error = ..., ProcessTerminationTimeout = null }, cancellationToken)`.

  Parse and validation errors write the error and help to `Error` and return 1.
- A single-file, self-contained, compressed `win-arm64` publish of Kestrel, System.CommandLine, and Microsoft.Data.Sqlite produced one 48.2 MB `gh-ledger.exe`. It served requests and opened SQLite in WAL mode; the native `e_sqlite3` library is extracted at run time.
- SQLite 3.53.3 (Microsoft.Data.Sqlite 10.0.15) accepts `STRICT, WITHOUT ROWID` and `WITH RECURSIVE ... UPDATE`. The Task 2 back-fill and prompt-chain SQL in this plan was run against the plan's seeds and produced the expected rows.
- System.Text.Json on .NET 10, for Task 3:
  - `JsonElement.TryGetDecimal`, `TryGetInt32`, `TryGetInt64`, and `TryGetDateTimeOffset` accept and reject exactly what Sprint 1's serializer accepted and rejected with `JsonNumberHandling.Strict`, with equal values:
    - `1e2` reads as the decimal 100, `1e-30` reads as 0, and `79228162514264337593543950336` and `1e30` fail;
    - `1.0`, `1e0`, and `2e5` fail as integers;
    - `2027-05-17` and `2027-05-17T00:00:00` read as local time, as before, and `2027-05-17 00:00:00Z` fails.
  - `JsonDocument.Parse` reports a syntax error with 0-based `LineNumber` and `BytePositionInLine`; `\r\n` counts as one line break. An empty input is line 0, byte 0. A trailing comma is reported at the closing `}` or `]`.
  - `JsonElement.TryGetProperty` returns the last of repeated property names, and `EnumerateObject` returns all of them in document order.
  - For an escaped lone surrogate such as `"\ud800"`, `JsonDocument.Parse` succeeds, but `GetString()` and `JsonProperty.Name` throw `InvalidOperationException` and `TryGetDateTimeOffset` returns false.
- A reference implementation of Task 3, including the rate bound, was built against `1809dc8` with the `rates.json` and `docs/configuration.md` that this plan's Task 2 and Task 3 blocks produce. It passes all 143 Sprint 1 tests, every Task 3 test, and RR1–RR4: 253 tests. Run against the merged loader, 54 of the Task 3 test cases fail.

### Assumptions (not verified)

- **A1.** Copilot CLI's bring-your-own provider labels appear as `gen_ai.provider.name` on `chat` spans. No BYOK provider was available to capture.
- **A2.** The `cwd:` line in `workspace.yaml` is undocumented. If it's missing or unreadable, CopilotLedger records the span without Git context; it never fails.
- **A3.** VS Code's Local harness was not captured live. Its behavior here comes from the VS Code documentation.
- **A4.** GitHub's pricing page gives promotion dates without a time or time zone. Windows end at 00:00:00Z on the named date, as Sprint 1's `mai-code-2` window does. If GitHub bills by another time zone, a request within a day of a boundary is priced at the neighboring window's rate.
- **A5.** Nine built-in model IDs are derived from display names (see "GitHub price table"). If Copilot reports a different ID for one of them, its requests are recorded as `unknown_model`, as they would be without the entry, until an overlay alias or a rates update maps them.

## Design decisions

1. **Transport: plain HTTP on loopback port 4318.**
   - The receiver serves OTLP/HTTP without TLS on `localhost:4318`, using Kestrel's `ListenLocalhost(4318)`. That binds `127.0.0.1` and `::1` and nothing else.
   - Copilot's default endpoint is `http://localhost:4318`, and `localhost` can resolve to `::1` before `127.0.0.1`, so binding both loopback addresses avoids failed IPv6 attempts.
   - TLS isn't needed. Copilot disables `http://` export only when TLS variables are set, and the live capture exported over plain HTTP.
   - Every request must also carry a loopback `Host` header (`localhost`, `127.0.0.1`, or `[::1]`). That blocks DNS-rebinding pages from writing to the ledger.
2. **Copilot integration: one variable, `COPILOT_OTEL_ENABLED=true`.**
   - `install` sets only this variable. The Copilot runtime and VS Code's Local harness both then export to their default endpoint, `http://localhost:4318`.
   - It doesn't set `OTEL_EXPORTER_OTLP_ENDPOINT`. That variable is read by every OpenTelemetry SDK, so setting it for the whole user account would redirect unrelated applications.
   - Conflicting settings produce warnings, not changes (Task 5).
3. **Protobuf decoding: a hand-written reader, with no package.**
   - `OtlpTraceDecoder` reads only the seven request messages in the schema table above, about 150 lines over `ReadOnlySpan<byte>`. The three response messages are only written. Unknown fields are skipped, which the protobuf format guarantees is safe.
   - These options were rejected:
     - Code generated from vendored `opentelemetry-proto` files with `Grpc.Tools`, because it runs the native `protoc` binaries during every build. That's a policy risk on locked-down corporate machines, and a failure the builder can't work around.
     - Google.Protobuf's `JsonParser`, because OTLP/JSON isn't standard proto3 JSON: IDs are hex, not base64.
     - Adding `Google.Protobuf` for its wire primitives alone isn't worth a dependency.
   - The reader never reads `double` values. It recognizes `double_value` and skips its 8 bytes, so `src/` stays free of `double`. It's also trimming- and AOT-safe.
   - Correctness comes from golden bytes produced by an independent reference encoder. Its decoder correctly decoded real Copilot payloads from the Rust exporter (`prost`) during the live capture.
4. **JSON decoding: `JsonDocument`** with the OTLP deviations: hex IDs, integer enums, and 64-bit numbers as strings or numbers. Numbers are read with `TryGetInt64` and `TryGetUInt64`, never `double`. Unknown and snake_case keys are ignored. A string that System.Text.Json can't materialize, such as an escaped lone surrogate, counts as an invalid value for its field instead of failing the request.
5. **gen_ai extraction rules (Task 1).**
   - Only inference spans become ledger entries: `gen_ai.operation.name` of `chat`, `text_completion`, or `generate_content`.
   - Token attributes must be non-negative `intValue`s. A token attribute with any other type rejects the span, so a wrong value is never priced silently. A missing attribute counts as 0.
   - `gen_ai.usage.cache_write.input_tokens` falls back to `gen_ai.usage.cache_creation.input_tokens`.
   - Sprint 1's deferred normalization applies: when cache read plus cache write is greater than input, input becomes input + cache read + cache write.
   - After that, a count above 1,000,000,000 rejects the span (`GenAiSpanNormalizer.MaxTokenCount`). The largest context windows are about 10 million tokens, so a count 100 times larger is a telemetry bug. With the cap, one span priced at built-in rates costs at most $100,000 (10^17 pico-USD: 10^9 cache-write tokens at $25 and 10^9 output tokens at $75 per 1M, on `gpt-6-astra` `long_context`). SQLite's integer `SUM` would need 93 such spans to overflow.
   - IDs are lowercase hex, and times are Unix nanoseconds converted to `DateTimeOffset`. A span with an invalid ID or a zero start time is rejected and reported as an OTLP partial success.
6. **Agent and conversation inheritance through `span_links`.**
   - Copilot's `chat` spans never carry `gen_ai.agent.name`, and their `invoke_agent` ancestors arrive later, often in another request. Inheritance therefore can't happen in memory at ingestion time.
   - Each received span that has `gen_ai.operation.name`, or comes from a Copilot resource, is indexed in `span_links`. That table holds IDs, operation, agent name, conversation ID, and Git slug, branch, and SHA, and never content.
   - After each request, a recursive SQL back-fill copies the nearest `invoke_agent` ancestor's agent name and conversation ID onto ledger rows that lack them. It never crosses a nested `invoke_agent`, so subagent spans get the subagent's name.
   - `span_links` rows are pruned after 7 days.
   - The Copilot runtime's default agent reports no name, so its spend appears under `(none)`. A custom agent reports its `name`, both at the top level and as a subagent, or its file ID when it has no `name` ("Live capture: custom agents").
7. **Working directory and Git context.**
   - A span's working directory is the resource attribute `process.working_directory`, from the OTel semantic conventions, which users can set with `OTEL_RESOURCE_ATTRIBUTES`. Otherwise it's the `cwd:` line of `<COPILOT_HOME>/session-state/<gen_ai.conversation.id>/workspace.yaml`. Neither is persisted.
   - Git context comes from Sprint 1's `FileSystemGitContextResolver`, wrapped in a 5-second time-to-live cache. Sprint 1 suggested a `FileSystemWatcher` instead, but a short cache is deterministic to test and immune to watcher limits. A stale branch name lasts at most 5 seconds.
   - VS Code's `github.copilot.git.*` attributes on `invoke_agent` spans fill in repository slug, branch, and commit only for rows that have no file-system Git context.
8. **Prompt chains are traces.** Copilot emits one trace per user message, with the `invoke_agent` root and every subagent in it, so `top-prompts` groups ledger rows by `trace_id`. This needs no parent links, so it works before late spans arrive. Prompt text is never stored, so a chain is identified by its start time, repository, agents, models, and trace ID.
9. **HTTP status mapping.** Every response body uses the request's encoding: protobuf, or JSON when the request is JSON or its type is unsupported.

   | Condition | Status |
   |---|---|
   | Success, nothing rejected | 200 with an empty response |
   | Success with rejected spans | 200 with `partial_success` |
   | `Host` isn't loopback | 400 |
   | Undecodable payload or invalid gzip | 400 |
   | Body over the limit, raw or decompressed | 413 |
   | Unsupported `Content-Type` or `Content-Encoding` | 415 |
   | Ledger write failure | 503 with `Retry-After: 5` |
10. **Daemon lifecycle with files, not a control port.**
    - `~/.copilot-ledger/daemon.lock` is held open exclusively while the daemon runs, which guarantees a single instance.
    - `daemon.json` records the process ID, endpoint, start time, version, and database path.
    - `gh-ledger daemon stop` creates `daemon.stop`. The daemon checks for it every 500 ms and shuts down gracefully.
    - There's no HTTP control endpoint, so nothing new is attack surface. It works the same way on Windows, where there are no POSIX signals.
    - `daemon start` launches `gh-ledger daemon run --log-file <data>/daemon.log` as a detached process with no console window. On Unix the daemon ignores `SIGHUP`, so closing the terminal doesn't stop it.
11. **CLI contract.**
    - The CLI is one System.CommandLine root command. Exit code 0 means success, and 1 means a usage or validation error or a failed command.
    - Tables use invariant formatting. Credits and USD are rounded for display to 2 places with `MidpointRounding.AwayFromZero`.
    - `--json` writes unrounded amounts: exact decimals formatted with `0.############`.
    - Periods (`--since`) use the local time zone.
    - `COPILOT_LEDGER_HOME` overrides the data directory, which defaults to `~/.copilot-ledger`.
12. **`install` and `uninstall`.**
    - `install`:
      - sets `COPILOT_OTEL_ENABLED=true`: in the Windows user environment, or with a marked block in the shell profile on macOS and Linux;
      - sets the `gh ledger` shell alias to the running binary;
      - starts the daemon;
      - records each change in `~/.copilot-ledger/install.json`.
    - `uninstall` reverses exactly what that file records, restores any previous value, and keeps the ledger data.
    - These are left out: `PATH` edits, copying the binary, and starting at login. See "Deferred".
    - On Windows without Git for Windows, gh can't run shell aliases, so `install` sets the alias anyway and warns. `gh-ledger` itself always works.
13. **Single binary: single-file, self-contained, and compressed, one per runtime identifier.**
    - Native AOT is rejected for three reasons:
      - The CLI reads and writes `daemon.json` and `install.json` with reflection-based System.Text.Json, which fails AOT and trimming analysis under `TreatWarningsAsErrors`. Task 3 removes reflection from the rates loader, so these two files are what's left.
      - Microsoft.Data.Sqlite's native library still ships as a separate file under AOT.
      - AOT needs a platform C toolchain on every build machine.
    - Trimming is off for the same reflection reason.
    - Verified: the binary is 48.2 MB on `win-arm64`.
    - `InvariantGlobalization` removes the ICU dependency on Linux. Every format and parse already uses `CultureInfo.InvariantCulture`.
14. **Rates refresh: GitHub's whole text-model table.**
    - Task 2 prices every text model on GitHub's pricing page as of 2026-11-20: 29 models join Sprint 1's five, for 34 built-in GitHub models. A model that isn't priced is recorded at $0 as `unknown_model`, so every missing model understates spend. The page's one image model is skipped, because it's billed per image token and Copilot's image-generation telemetry hasn't been captured.
    - Model IDs follow "GitHub price table": 25 are verified, and 9 are derived from the display name with Sprint 1's normalization, which reproduces every verified ID (A5). A wrong derived ID costs nothing beyond what leaving the model out would: its requests stay `unknown_model`. Claude Opus 4.8 (fast mode) is left out, because no plausible ID can be derived for it.
    - Promotional windows end at 00:00:00Z on the date the page gives (A4). Gemini 4 Argon (ending 2026-12-01) and Gemini 3.8 Flash (ending 2027-01-01) have no published later price, so their windows simply end. After those dates their requests are recorded as `no_rate_for_date`, at $0 but counted as unpriced in every report, until a rates update or an overlay window adds the price. CopilotLedger never invents a price; carrying the promotional price forward or borrowing Carbon's later price would both be guesses. Three new `notes` in `rates.json` record these rules.
    - Task 2 also adds the local runtime labels `ollama`, `lm_studio`, `foundry_local`, and `llama_cpp` to `localOffload.providerNames` (A1). `openai`, `anthropic`, and `azure_openai` are left out because GitHub-hosted models might report the same labels.
    - `ratesVersion` stays `2026-11-20`, because every price is from that day's retrieval.
15. **Privacy.**
    - The ledger never stores prompt or response content, remote URLs, or a session's working directory; the only local path stored is Sprint 1's repository root. VS Code repository URLs are reduced to an `owner/repo` slug after credentials are removed.
    - `span_links` holds only IDs, operation and agent names, conversation IDs, and Git slug, branch, and SHA.
    - Rejection messages name attribute keys, never values.
16. **One bad span never fails a request.**
    - Sprint 1's `RecordAsync` validates and inserts a whole batch in one transaction, so one bad entry would lose every span in the request. Worse, the receiver answers a failed write with a retryable 503. The exporter would re-send the same request, fail the same way, and finally drop the batch.
    - So `RecordAsync` only ever receives entries that can't fail its checks:
      - the normalizer passes only inference spans with valid IDs, a start time, and non-negative counts within the cap;
      - the ingestor prices each span on its own and drops a span whose cost or savings doesn't fit in pico-USD. Before Task 3, an extreme overlay rate can also make `Calculate` overflow `decimal`; the ingestor drops that span too, and keeps the check afterward as defense in depth. Git context is best effort: if it can't be read, the span is recorded without it.
    - Dropped spans are reported as an OTLP partial success (200), which clients must not retry because the result would be the same.
    - 503 is kept for store failures such as a locked or full database. Those are transient, and a retry is safe because inserts ignore duplicates.
    - Task 2's "Filtering and per-span errors" has the full table.
17. **Rates overlay errors point into the user's file, and Core owns every message (Task 3).**
    - A review of Sprint 1 Task 2 (#5) found that overlay errors use merged-document indices. Verified on `1809dc8` with Task 2's rates: an overlay's `localOffload.models[0]` is reported as `localOffload.models[1]`, a new GitHub model as `githubAiCredits.models[34]`, and the first Foundry host suffix as `foundryByok.hostSuffixes[3]`. One alias collision was reported at the *built-in* model's path, which isn't in the user's file at all.
    - Decision: every path points into the file that contains the problem, using its own indexes. The overlay is validated by itself before merging. Only two checks need the built-in rates: a model identity that a remaining built-in model already uses, and `localOffload.baselineModel`. Their messages contain `in the merged rates`, which `docs/configuration.md` defines, and a collision names the built-in model.
    - Core owns every message. System.Text.Json's messages contain .NET type names such as ``System.Nullable`1[System.Int32]`` and the `$.` path suffix, and they report a missing required property at its object's path. A small `JsonDocument` reader (`RateCardReader`) replaces reflection-based deserialization. It reads the same values: `TryGetDecimal`, `TryGetInt32`, `TryGetInt64`, and `TryGetDateTimeOffset` gave the same results as the Sprint 1 serializer on every probe (verified on .NET 10). The reader also rejects repeated property names, which the serializer silently resolved by keeping the last one.
    - Invalid JSON is reported by line and column, counting from 1, with no System.Text.Json text, which is developer-facing ("Change the reader options", "isFinalBlock").
    - The loader reports the first problem it finds, in an order Task 3 specifies. The tests assert every message exactly, and a test checks that the examples in `docs/configuration.md` match real output.
    - The CLI prints Core's message unchanged after its own prefix. This replaces the CLI-side formatter in the first version of this plan.
18. **Rates are at most 1,000,000 USD per 1M tokens, so `Calculate` never throws (Task 3).**
    - `ICreditCalculator.Calculate` promises to throw only `ArgumentNullException`, but Sprint 1 accepts any rate whose `rate × 1,000,000` fits in a `decimal`, and such a rate can overflow when it's multiplied by a token count ("Starting point").
    - Task 3 rejects any rate above 1,000,000, in the built-in rates and in an overlay, with `must be less than or equal to 1000000`. The most expensive price GitHub publishes is $75 per 1M tokens, so the bound leaves room for any real price. Token counts are `long`, at most about 9.22 × 10^18. Each cost component, `tokens × rate / 1,000,000`, then has an intermediate product under 10^25 and a result under 9.3 × 10^18 USD. The four components sum to under 3.7 × 10^19 USD, which is under 3.7 × 10^21 AI credits. All of these are far inside `decimal`'s 7.9 × 10^28, so `Calculate` can't overflow, whatever the usage.
    - A rate of exactly 1,000,000 still loads. Its cost can exceed pico-USD's `long.MaxValue` (about $9.22 million), so the ingestor's `cost out of range` check (decision 16) stays necessary; IG6 tests it with an in-bound rate. The ingestor's `OverflowException` catch stays too, as defense in depth for any other `ICreditCalculator`, and IG8 tests it with a fake calculator.
    - The bound check runs before the 6-decimal-place check, which therefore can't overflow, so its `is too large` branch is removed. The reader's `is too large` remains for numbers outside `decimal`'s range, such as `1e30`.

## Task 1 specification: OTLP Trace Decoding & gen_ai Span Normalization

### Files

Create:

```text
src/CopilotLedger.Core/Telemetry/GenAiSpanLink.cs
src/CopilotLedger.Otlp/CopilotLedger.Otlp.csproj
src/CopilotLedger.Otlp/Decoding/OtlpModel.cs
src/CopilotLedger.Otlp/Decoding/OtlpDecodeException.cs
src/CopilotLedger.Otlp/Decoding/OtlpTraceDecoder.cs
src/CopilotLedger.Otlp/Decoding/ProtoReader.cs                 (internal)
src/CopilotLedger.Otlp/Decoding/OtlpProtobufTraceReader.cs     (internal)
src/CopilotLedger.Otlp/Decoding/OtlpJsonTraceReader.cs         (internal)
src/CopilotLedger.Otlp/Normalization/GenAiAttributeNames.cs
src/CopilotLedger.Otlp/Normalization/NormalizedTraceBatch.cs
src/CopilotLedger.Otlp/Normalization/GenAiSpanNormalizer.cs
tests/CopilotLedger.Tests/TestSupport/OtlpProtoWriter.cs
tests/CopilotLedger.Tests/TestSupport/OtlpFixtures.cs
tests/CopilotLedger.Tests/TestSupport/OtlpRequests.cs
tests/CopilotLedger.Tests/Otlp/OtlpProtobufDecodingTests.cs
tests/CopilotLedger.Tests/Otlp/OtlpJsonDecodingTests.cs
tests/CopilotLedger.Tests/Otlp/GenAiSpanNormalizerTests.cs
```

Also update `CopilotLedger.slnx` and `tests/CopilotLedger.Tests/CopilotLedger.Tests.csproj` (through the commands below).

### Steps

**Step 1.** From the repository root, run:

```shell
dotnet new classlib -n CopilotLedger.Otlp -o src/CopilotLedger.Otlp
dotnet sln CopilotLedger.slnx add src/CopilotLedger.Otlp/CopilotLedger.Otlp.csproj
dotnet add tests/CopilotLedger.Tests/CopilotLedger.Tests.csproj reference src/CopilotLedger.Otlp/CopilotLedger.Otlp.csproj
```

**Step 2.** Delete the template's `Class1.cs`, and replace `src/CopilotLedger.Otlp/CopilotLedger.Otlp.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\CopilotLedger.Core\CopilotLedger.Core.csproj" />
  </ItemGroup>
</Project>
```

**Step 3.** Add the contracts below exactly as written. Members ending in `;` instead of a body are implemented in this task as described. Types marked internal in the file list stay internal, and `CopilotLedger.Otlp` has no other public types.

**Step 4.** Implement the protobuf, JSON, and normalization rules below, then write the "Task 1 tests" under "Test specifications".

### Contracts

```csharp
// ---- src/CopilotLedger.Core/Telemetry/GenAiSpanLink.cs
namespace CopilotLedger.Core.Telemetry;

// One received span's place in its trace, used to inherit agent, conversation, and Git attributes from invoke_agent ancestors.
public sealed record GenAiSpanLink
{
    public required string TraceId { get; init; }             // 32 lowercase hex chars
    public required string SpanId { get; init; }              // 16 lowercase hex chars
    public string? ParentSpanId { get; init; }                // 16 lowercase hex chars; null for a root span
    public string? OperationName { get; init; }               // gen_ai.operation.name
    public string? AgentName { get; init; }                   // gen_ai.agent.name
    public string? ConversationId { get; init; }              // gen_ai.conversation.id
    public string? GitRepositorySlug { get; init; }           // "owner/repo" from github.copilot.git.repository (legacy copilot_chat.repo.remote_url)
    public string? GitBranch { get; init; }                   // github.copilot.git.branch (legacy copilot_chat.repo.head_branch_name)
    public string? GitCommitSha { get; init; }                // github.copilot.git.commit_sha (legacy copilot_chat.repo.head_commit_hash); lowercase 40 or 64 hex
    public required DateTimeOffset StartTime { get; init; }
}

// ---- src/CopilotLedger.Otlp/Decoding/OtlpModel.cs
namespace CopilotLedger.Otlp.Decoding;

public enum OtlpValueKind
{
    Empty,          // no value field
    String,
    Bool,
    Int,
    Double,         // recognized; the value isn't kept
    Bytes,          // recognized; the value isn't kept
    Array,          // recognized; the value isn't kept
    KeyValueList,   // recognized; the value isn't kept
}

public sealed record OtlpAnyValue
{
    public static OtlpAnyValue Empty { get; } = new() { Kind = OtlpValueKind.Empty };
    public required OtlpValueKind Kind { get; init; }
    public string? StringValue { get; init; }   // set when Kind is String
    public bool BoolValue { get; init; }        // set when Kind is Bool
    public long IntValue { get; init; }         // set when Kind is Int
}

public sealed record OtlpAttribute(string Key, OtlpAnyValue Value);

public sealed record OtlpSpan
{
    public string? TraceId { get; init; }                  // 32 lowercase hex chars; null when missing, the wrong length, not hex, or all zeros
    public string? SpanId { get; init; }                   // 16 lowercase hex chars; null when missing, the wrong length, not hex, or all zeros
    public string? ParentSpanId { get; init; }             // 16 lowercase hex chars; null when empty, missing, invalid, or all zeros
    public string Name { get; init; } = "";
    public ulong StartTimeUnixNano { get; init; }          // 0 when missing or invalid
    public ulong EndTimeUnixNano { get; init; }            // 0 when missing or invalid
    public IReadOnlyList<OtlpAttribute> Attributes { get; init; } = [];
}

public sealed record OtlpResourceSpans(
    IReadOnlyList<OtlpAttribute> ResourceAttributes,
    IReadOnlyList<OtlpSpan> Spans);                        // the spans of every ScopeSpans entry, in order

public sealed record OtlpTraceRequest(IReadOnlyList<OtlpResourceSpans> ResourceSpans)
{
    public int SpanCount => ResourceSpans.Sum(r => r.Spans.Count);
}

// ---- src/CopilotLedger.Otlp/Decoding/OtlpDecodeException.cs
namespace CopilotLedger.Otlp.Decoding;

public sealed class OtlpDecodeException : Exception
{
    public OtlpDecodeException(string message) : base(message) { }
    public OtlpDecodeException(string message, Exception innerException) : base(message, innerException) { }
}

// ---- src/CopilotLedger.Otlp/Decoding/OtlpTraceDecoder.cs
namespace CopilotLedger.Otlp.Decoding;

public static class OtlpTraceDecoder
{
    // Binary protobuf ExportTraceServiceRequest. Throws OtlpDecodeException for malformed input.
    public static OtlpTraceRequest DecodeProtobuf(ReadOnlyMemory<byte> payload);

    // OTLP/JSON ExportTraceServiceRequest in UTF-8. Throws OtlpDecodeException for malformed input.
    public static OtlpTraceRequest DecodeJson(ReadOnlyMemory<byte> utf8Json);
}

// ---- src/CopilotLedger.Otlp/Normalization/GenAiAttributeNames.cs
namespace CopilotLedger.Otlp.Normalization;

public static class GenAiAttributeNames
{
    public const string OperationName = "gen_ai.operation.name";
    public const string ProviderName = "gen_ai.provider.name";
    public const string RequestModel = "gen_ai.request.model";
    public const string ResponseModel = "gen_ai.response.model";
    public const string AgentName = "gen_ai.agent.name";
    public const string ConversationId = "gen_ai.conversation.id";
    public const string InputTokens = "gen_ai.usage.input_tokens";
    public const string OutputTokens = "gen_ai.usage.output_tokens";
    public const string CacheReadInputTokens = "gen_ai.usage.cache_read.input_tokens";
    public const string CacheWriteInputTokens = "gen_ai.usage.cache_write.input_tokens";
    public const string LegacyCacheCreationInputTokens = "gen_ai.usage.cache_creation.input_tokens";
    public const string ServerAddress = "server.address";
    public const string ServerPort = "server.port";
    public const string GitRepository = "github.copilot.git.repository";
    public const string GitBranch = "github.copilot.git.branch";
    public const string GitCommitSha = "github.copilot.git.commit_sha";
    public const string LegacyRepoRemoteUrl = "copilot_chat.repo.remote_url";
    public const string LegacyRepoHeadBranchName = "copilot_chat.repo.head_branch_name";
    public const string LegacyRepoHeadCommitHash = "copilot_chat.repo.head_commit_hash";
    public const string ServiceName = "service.name";                          // resource attribute
    public const string ProcessWorkingDirectory = "process.working_directory"; // resource attribute
}

// ---- src/CopilotLedger.Otlp/Normalization/NormalizedTraceBatch.cs
using CopilotLedger.Core.Telemetry;

namespace CopilotLedger.Otlp.Normalization;

public sealed record NormalizedTraceBatch
{
    public required IReadOnlyList<NormalizedGenAiSpan> InferenceSpans { get; init; }  // accepted inference spans, in request order
    public required IReadOnlyList<GenAiSpanLink> Links { get; init; }                  // accepted indexed spans, in request order
    public required int ReceivedSpanCount { get; init; }
    public required int RejectedSpanCount { get; init; }
    public string? FirstRejectionReason { get; init; }                                 // null when RejectedSpanCount is 0
}

// ---- src/CopilotLedger.Otlp/Normalization/GenAiSpanNormalizer.cs
using CopilotLedger.Otlp.Decoding;

namespace CopilotLedger.Otlp.Normalization;

public static class GenAiSpanNormalizer
{
    // Largest token count accepted after normalization; a larger count rejects the span.
    public const long MaxTokenCount = 1_000_000_000;

    // Throws ArgumentNullException for a null request; otherwise never throws.
    public static NormalizedTraceBatch Normalize(OtlpTraceRequest request);
}
```

### Protobuf decoding rules

`internal ref struct ProtoReader` reads a `ReadOnlySpan<byte>` from a position. Each failure throws `OtlpDecodeException` with a short message.

1. `ReadVarint()` reads up to 10 bytes, 7 bits each, least significant group first. It fails if the input ends mid-varint, or if a 10th byte is greater than `0x01`.
2. `ReadTag()` reads a varint. The field number is `tag >> 3`, and the wire type is `tag & 7`. It fails if the tag exceeds `uint.MaxValue` or the field number is 0. `TryReadTag` returns false at the end of the span.
3. `ReadFixed64()` and `ReadFixed32()` read 8 and 4 little-endian bytes with `BinaryPrimitives`. They fail if the input is truncated.
4. `ReadLengthDelimited()` reads a varint length, then that many bytes as a slice. It fails if the length exceeds the remaining bytes.
5. `Skip(wireType)` skips VARINT (0), I64 (1), LEN (2), or I32 (5) values. Wire types 3, 4, 6, and 7 fail with `unsupported wire type N`.

`OtlpProtobufTraceReader` parses each message from its own length-delimited slice:

1. It reads exactly the fields in the schema table under "Verified facts", with the listed wire types. It skips every other field, and any listed field that arrives with a different wire type, using `Skip`.
2. For a singular field the last occurrence wins. That includes `resource`, whose attributes are replaced. `AnyValue` takes the kind of the last value field it reads.
3. Values become `OtlpAnyValue`s:
   - `string_value` becomes `String`.
   - `bool_value` becomes `Bool`, true when the varint isn't 0.
   - `int_value` becomes `Int`, using the varint's two's-complement value: `(long)varint`.
   - `double_value`, `array_value`, `kvlist_value`, and `bytes_value` become `Double`, `Array`, `KeyValueList`, and `Bytes`, and their contents are skipped. `double_value` is skipped as 8 bytes and never read as a `double`.
   - Any other field, such as `string_value_strindex`, is skipped and doesn't change the kind.
   - An `AnyValue` with no recognized field, or a `KeyValue` with no `value`, is `OtlpAnyValue.Empty`.
4. Strings are decoded with `Encoding.UTF8.GetString`, which replaces invalid sequences with U+FFFD.
5. A `KeyValue` with a missing or empty key is dropped. Attributes keep their wire order, including duplicate keys.
6. `trace_id` is kept only if it's 16 bytes and not all zeros, and `span_id` and `parent_span_id` only if they're 8 bytes and not all zeros. Kept IDs are converted with `Convert.ToHexStringLower`. Anything else becomes null.
7. An empty payload is a valid request with no `ResourceSpans`.

### JSON decoding rules

`OtlpJsonTraceReader`:

1. Skip a leading UTF-8 byte order mark (`EF BB BF`).
2. Parse with `JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false })`. Wrap any `JsonException` in `OtlpDecodeException("Invalid JSON: " + ex.Message, ex)`.
3. The root must be an object.
4. Containers:
   - `resourceSpans` on the root, `scopeSpans` on each resource-spans entry, `spans` on each scope-spans entry, and `attributes` on `resource` and on each span: missing or `null` is empty, an array is read, and anything else throws `OtlpDecodeException`.
   - Elements of `resourceSpans`, `scopeSpans`, and `spans` must be objects; anything else throws.
   - `resource` may be missing or `null` (no attributes) or an object; anything else throws.
5. An attribute element that isn't an object, or whose `key` isn't a non-empty string, is skipped. Its `value` is read as an `AnyValue` if it's an object. A missing, `null`, or non-object value is `OtlpAnyValue.Empty`.
6. To read an `AnyValue` object, go through its properties in document order and use the first one named `stringValue`, `boolValue`, `intValue`, `doubleValue`, `arrayValue`, `kvlistValue`, or `bytesValue`. Ignore later ones.
   - `stringValue` that's a JSON string becomes `String`; any other JSON type becomes `Empty`.
   - `boolValue` that's `true` or `false` becomes `Bool`; any other JSON type becomes `Empty`.
   - `intValue` becomes `Int` when it's a string that `long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v)` accepts, or a number for which `TryGetInt64` succeeds. Anything else becomes `Empty`.
   - `doubleValue`, `arrayValue`, `kvlistValue`, and `bytesValue` become `Double`, `Array`, `KeyValueList`, and `Bytes` whatever their JSON type.
   - No recognized property means `Empty`.
7. Span fields:
   - `traceId`, `spanId`, and `parentSpanId` are kept only if they're strings of exactly 32, 16, and 16 hex characters (either case) that aren't all `0`. Kept IDs are lower-cased with `ToLowerInvariant()`; anything else, including `""`, becomes null.
   - `name` is used if it's a string; otherwise it's `""`.
   - `startTimeUnixNano` and `endTimeUnixNano` come from a string that `ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v)` accepts, or from a number for which `TryGetUInt64` succeeds. Otherwise they're 0.
8. Property names are case-sensitive, and unknown properties are ignored everywhere, including snake_case names such as `trace_id`.
9. Never call `GetDouble` or `TryGetDouble`.
10. Read every JSON string through one helper, `TryGetString(JsonElement, out string)`, which returns false when `GetString()` throws `InvalidOperationException`. System.Text.Json throws that for an escaped lone surrogate such as `\ud800`, which `JsonDocument.Parse` accepts; JavaScript's `JSON.stringify` writes one for a Windows file name with an unpaired surrogate. An unreadable string counts as an invalid value for its field, so one odd attribute never fails the request:
    - an attribute whose `key` can't be read is skipped, and an unreadable `stringValue` is `Empty`;
    - an unreadable `traceId`, `spanId`, or `parentSpanId` is null, and an unreadable `name` is `""`;
    - an unreadable `intValue`, `startTimeUnixNano`, or `endTimeUnixNano` string is `Empty` or 0.

    Match property names with `TryGetProperty` or `JsonProperty.NameEquals`, never by reading `JsonProperty.Name`, which throws the same way. Verified on .NET 10: `GetString()`, `JsonProperty.Name`, and `ValueEquals` throw for these strings, while `TryGetProperty` and `NameEquals` don't.

### Normalization rules

`GenAiSpanNormalizer.Normalize` processes resources in order, then the spans of each in order.

Per resource:

- It is a **Copilot resource** when its first `service.name` attribute is a `String` equal to `github-copilot` or `copilot-chat` (ordinal).
- Its **working directory** is the first `process.working_directory` attribute that is a `String`, isn't blank, and passes `Path.IsPathFullyQualified`. Otherwise it's null.

Per span:

1. Increment `ReceivedSpanCount`.
2. Reject the span if `TraceId` is null (`invalid traceId`), then if `SpanId` is null (`invalid spanId`), then if `StartTimeUnixNano` is 0 (`missing startTimeUnixNano`).
3. Look up attributes by key, using the first occurrence (ordinal). `Text(key)` is the attribute's string when its kind is `String` and the value isn't null or whitespace; otherwise it's null. Values aren't trimmed.
4. `operation = Text("gen_ai.operation.name")`.
5. If `GenAiOperationNames.IsInference(operation)`, read the token counts in this order: input, output, cache read, then cache write.
   - `Token(key)` is 0 when the key is absent, and `IntValue` when the kind is `Int` and the value is at least 0. Otherwise, reject the span with `<key> is not a non-negative integer`.
   - Cache write uses `gen_ai.usage.cache_write.input_tokens` when that key is present, whatever its value. Otherwise it uses `gen_ai.usage.cache_creation.input_tokens`.
   - In a `checked` context, if `cacheRead + cacheWrite > input`, set `input = input + cacheRead + cacheWrite`. An `OverflowException` rejects the span with `token counts overflow`.
   - Then, if any of the four counts is greater than `MaxTokenCount`, reject the span with `token count exceeds 1000000000`.
6. A rejected span increments `RejectedSpanCount` and sets `FirstRejectionReason` if it's still null. It produces no link and no inference span.
7. Times:
   - `FromUnixNano(ns)` is `DateTimeOffset.UnixEpoch.AddTicks((long)(ns / 100))`.
   - `start = FromUnixNano(StartTimeUnixNano)`.
   - `end = EndTimeUnixNano >= StartTimeUnixNano ? FromUnixNano(EndTimeUnixNano) : start`.
8. If `operation` isn't null, or the resource is a Copilot resource, add a `GenAiSpanLink`:
   - `TraceId`, `SpanId`, and `ParentSpanId` from the span; `OperationName = operation`;
   - `AgentName = Text(gen_ai.agent.name)` and `ConversationId = Text(gen_ai.conversation.id)`;
   - `GitRepositorySlug`: when `url = Text(github.copilot.git.repository) ?? Text(copilot_chat.repo.remote_url)` isn't null, `GitRemoteUrl.GetRepositorySlug(GitRemoteUrl.Sanitize(url))`; otherwise null;
   - `GitBranch = Text(github.copilot.git.branch) ?? Text(copilot_chat.repo.head_branch_name)`;
   - `GitCommitSha = Text(github.copilot.git.commit_sha) ?? Text(copilot_chat.repo.head_commit_hash)`, kept only if it's 40 or 64 hex characters, and lower-cased with `ToLowerInvariant()`;
   - `StartTime = start`.
9. If the span is an inference span, add a `NormalizedGenAiSpan`:
   - `TraceId`, `SpanId`, and `ParentSpanId` from the span; `OperationName = operation`; `StartTime = start` and `EndTime = end`;
   - `ProviderName`, `RequestModel`, `ResponseModel`, `AgentName`, `ConversationId`, and `ServerAddress` as `Text` of `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.response.model`, `gen_ai.agent.name`, `gen_ai.conversation.id`, and `server.address`;
   - `ServerPort` as `(int)IntValue` when `server.port` is an `Int` from 1 to 65535, otherwise null;
   - `WorkingDirectory` as the resource's working directory;
   - the four token counts from step 5.

Spans that are neither inference nor indexed, such as an HTTP span from another application, are counted as received and otherwise ignored.

### Acceptance criteria

- `CopilotLedger.slnx` contains `CopilotLedger.Core`, `CopilotLedger.Storage`, `CopilotLedger.Otlp`, and `CopilotLedger.Tests`. The Otlp `.csproj` matches Step 2.
- The public surface of `CopilotLedger.Otlp` is exactly the contracts above, and `GenAiSpanLink` exists in Core as written.
- `src/` contains no `double`, `float`, `GetDouble`, `TryGetDouble`, or `System.Diagnostics.Process`, and no NuGet package was added.
- `dotnet build CopilotLedger.slnx` succeeds with 0 warnings, and `dotnet test CopilotLedger.slnx` passes the Sprint 1 cases and PB1–PB7, JS1–JS8, and NZ1–NZ11 with 0 failed and 0 skipped.

### Verify

```shell
dotnet build CopilotLedger.slnx
dotnet test CopilotLedger.slnx
```

Report the passed, failed, and skipped counts.

## Task 2 specification: Ledger Ingestion, Session Attribution & Rates Refresh

### Files

Create:

```text
src/CopilotLedger.Core/Git/CachingGitContextResolver.cs
src/CopilotLedger.Storage/PromptChainRow.cs
src/CopilotLedger.Otlp/Attribution/CopilotSessionWorkspaceLocator.cs
src/CopilotLedger.Otlp/Ingestion/ILedgerIngestor.cs
src/CopilotLedger.Otlp/Ingestion/IngestResult.cs
src/CopilotLedger.Otlp/Ingestion/LedgerIngestor.cs
tests/CopilotLedger.Tests/TestSupport/ManualTimeProvider.cs
tests/CopilotLedger.Tests/TestSupport/CopilotTraces.cs
tests/CopilotLedger.Tests/TestSupport/LedgerSeeds.cs
tests/CopilotLedger.Tests/Git/CachingGitContextResolverTests.cs
tests/CopilotLedger.Tests/Otlp/CopilotSessionWorkspaceLocatorTests.cs
tests/CopilotLedger.Tests/Otlp/LedgerIngestorTests.cs
tests/CopilotLedger.Tests/Storage/SpanLinkStoreTests.cs
tests/CopilotLedger.Tests/Pricing/RatesRefreshTests.cs
```

Modify:

```text
src/CopilotLedger.Core/Pricing/rates.json
src/CopilotLedger.Storage/LedgerMigrations.cs
src/CopilotLedger.Storage/SqliteLedgerStore.cs
src/CopilotLedger.Otlp/CopilotLedger.Otlp.csproj        (through the command below)
tests/CopilotLedger.Tests/Storage/SqliteLedgerStoreTests.cs
docs/configuration.md
```

Run:

```shell
dotnet add src/CopilotLedger.Otlp/CopilotLedger.Otlp.csproj reference src/CopilotLedger.Storage/CopilotLedger.Storage.csproj
```

In `SqliteLedgerStoreTests.S1_InitializeAsync_CreatesWalDatabaseAndIsIdempotent`, change the `PRAGMA user_version` assertion from `Assert.Equal(1L, ...)` to `Assert.Equal(2L, ...)`. That is the only change to Sprint 1 tests. No other Sprint 1 test reads `user_version` or lists tables.

### rates.json changes

Change `src/CopilotLedger.Core/Pricing/rates.json` in exactly three places. Change nothing else; in particular, `ratesVersion` stays `2026-11-20`, and the five Sprint 1 models don't change.

**Models.** Append these 29 entries, in this order, to `githubAiCredits.models`, after `mai-code-2`. They're the models whose **Built in** column says Task 2 in "GitHub price table", in the page's order. Indent them like the existing entries.

```json
[
  {
    "id": "gpt-5.3-codex",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 1.75, "cachedInputPer1M": 0.175, "cacheWritePer1M": null, "outputPer1M": 14.00 }
        ]
      }
    ]
  },
  {
    "id": "gpt-5.4-nano",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 0.20, "cachedInputPer1M": 0.02, "cacheWritePer1M": null, "outputPer1M": 1.25 }
        ]
      }
    ]
  },
  {
    "id": "gpt-5.6-luna",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": 200000, "inputPer1M": 0.20, "cachedInputPer1M": 0.02, "cacheWritePer1M": 0.25, "outputPer1M": 1.20 },
          { "name": "long_context", "maxInputTokens": null, "inputPer1M": 0.40, "cachedInputPer1M": 0.04, "cacheWritePer1M": 0.50, "outputPer1M": 1.80 }
        ]
      }
    ]
  },
  {
    "id": "gpt-5.6-sol",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": 272000, "inputPer1M": 4.00, "cachedInputPer1M": 0.40, "cacheWritePer1M": 5.00, "outputPer1M": 20.00 },
          { "name": "long_context", "maxInputTokens": null, "inputPer1M": 8.00, "cachedInputPer1M": 0.80, "cacheWritePer1M": 10.00, "outputPer1M": 30.00 }
        ]
      }
    ]
  },
  {
    "id": "gpt-6-luna",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": 272000, "inputPer1M": 0.10, "cachedInputPer1M": 0.01, "cacheWritePer1M": 0.125, "outputPer1M": 0.50 },
          { "name": "long_context", "maxInputTokens": null, "inputPer1M": 0.20, "cachedInputPer1M": 0.02, "cacheWritePer1M": 0.25, "outputPer1M": 0.75 }
        ]
      }
    ]
  },
  {
    "id": "gpt-6-sol",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": 272000, "inputPer1M": 2.00, "cachedInputPer1M": 0.20, "cacheWritePer1M": 2.50, "outputPer1M": 10.00 },
          { "name": "long_context", "maxInputTokens": null, "inputPer1M": 4.00, "cachedInputPer1M": 0.40, "cacheWritePer1M": 5.00, "outputPer1M": 15.00 }
        ]
      }
    ]
  },
  {
    "id": "gpt-6.1-terra",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": 272000, "inputPer1M": 1.00, "cachedInputPer1M": 0.05, "cacheWritePer1M": 1.25, "outputPer1M": 5.00 },
          { "name": "long_context", "maxInputTokens": null, "inputPer1M": 2.00, "cachedInputPer1M": 0.10, "cacheWritePer1M": 2.50, "outputPer1M": 7.50 }
        ]
      }
    ]
  },
  {
    "id": "gpt-6.2-astra",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": 272000, "inputPer1M": 8.00, "cachedInputPer1M": 0.40, "cacheWritePer1M": 10.00, "outputPer1M": 40.00 },
          { "name": "long_context", "maxInputTokens": null, "inputPer1M": 16.00, "cachedInputPer1M": 0.80, "cacheWritePer1M": 20.00, "outputPer1M": 60.00 }
        ]
      }
    ]
  },
  {
    "id": "gpt-6.2-sol",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": 272000, "inputPer1M": 2.00, "cachedInputPer1M": 0.10, "cacheWritePer1M": 2.50, "outputPer1M": 10.00 },
          { "name": "long_context", "maxInputTokens": null, "inputPer1M": 4.00, "cachedInputPer1M": 0.20, "cacheWritePer1M": 5.00, "outputPer1M": 15.00 }
        ]
      }
    ]
  },
  {
    "id": "claude-haiku-4.5",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 1.00, "cachedInputPer1M": 0.10, "cacheWritePer1M": 1.25, "outputPer1M": 5.00 }
        ]
      }
    ]
  },
  {
    "id": "claude-haiku-5.5",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": 100000, "inputPer1M": 0.10, "cachedInputPer1M": 0.01, "cacheWritePer1M": 0.125, "outputPer1M": 0.50 },
          { "name": "long_context", "maxInputTokens": null, "inputPer1M": 0.50, "cachedInputPer1M": 0.05, "cacheWritePer1M": 0.625, "outputPer1M": 2.50 }
        ]
      }
    ]
  },
  {
    "id": "claude-sonnet-4",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 3.00, "cachedInputPer1M": 0.30, "cacheWritePer1M": 3.75, "outputPer1M": 15.00 }
        ]
      }
    ]
  },
  {
    "id": "claude-sonnet-4.6",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 3.00, "cachedInputPer1M": 0.30, "cacheWritePer1M": 3.75, "outputPer1M": 15.00 }
        ]
      }
    ]
  },
  {
    "id": "claude-opus-4.8",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 5.00, "cachedInputPer1M": 0.50, "cacheWritePer1M": 6.25, "outputPer1M": 25.00 }
        ]
      }
    ]
  },
  {
    "id": "claude-sonnet-5",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 2.00, "cachedInputPer1M": 0.20, "cacheWritePer1M": 2.50, "outputPer1M": 10.00 }
        ]
      }
    ]
  },
  {
    "id": "claude-fable-5",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 10.00, "cachedInputPer1M": 1.00, "cacheWritePer1M": 12.50, "outputPer1M": 50.00 }
        ]
      }
    ]
  },
  {
    "id": "claude-fable-5.1",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 10.00, "cachedInputPer1M": 0.25, "cacheWritePer1M": 12.50, "outputPer1M": 50.00 }
        ]
      }
    ]
  },
  {
    "id": "claude-fable-5.2",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 8.00, "cachedInputPer1M": 0.20, "cacheWritePer1M": 10.00, "outputPer1M": 40.00 }
        ]
      }
    ]
  },
  {
    "id": "gemini-3.8-flash",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": "2027-01-01T00:00:00Z",
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 0.75, "cachedInputPer1M": 0.075, "cacheWritePer1M": null, "outputPer1M": 3.75 }
        ]
      }
    ]
  },
  {
    "id": "gemini-4-argon",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": "2026-12-01T00:00:00Z",
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 2.00, "cachedInputPer1M": 0.10, "cacheWritePer1M": null, "outputPer1M": 10.00 }
        ]
      }
    ]
  },
  {
    "id": "gemini-4-carbon",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": "2026-12-01T00:00:00Z",
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 2.00, "cachedInputPer1M": 0.10, "cacheWritePer1M": null, "outputPer1M": 10.00 }
        ]
      },
      {
        "effectiveFrom": "2026-12-01T00:00:00Z",
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 4.00, "cachedInputPer1M": 0.20, "cacheWritePer1M": null, "outputPer1M": 20.00 }
        ]
      }
    ]
  },
  {
    "id": "gemini-4-hydrogen",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 1.50, "cachedInputPer1M": 0.075, "cacheWritePer1M": null, "outputPer1M": 7.50 }
        ]
      }
    ]
  },
  {
    "id": "gemini-4-neon",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 3.00, "cachedInputPer1M": 0.15, "cacheWritePer1M": null, "outputPer1M": 15.00 }
        ]
      }
    ]
  },
  {
    "id": "mai-code-1.1-flash",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 0.20, "cachedInputPer1M": 0.02, "cacheWritePer1M": null, "outputPer1M": 1.20 }
        ]
      }
    ]
  },
  {
    "id": "mai-code-2-flash",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": "2027-05-17T00:00:00Z",
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 0.05, "cachedInputPer1M": 0.005, "cacheWritePer1M": null, "outputPer1M": 0.25 }
        ]
      },
      {
        "effectiveFrom": "2027-05-17T00:00:00Z",
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 0.10, "cachedInputPer1M": 0.01, "cacheWritePer1M": null, "outputPer1M": 0.50 }
        ]
      }
    ]
  },
  {
    "id": "grok-4.5",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": 200000, "inputPer1M": 2.00, "cachedInputPer1M": 0.50, "cacheWritePer1M": null, "outputPer1M": 6.00 },
          { "name": "long_context", "maxInputTokens": null, "inputPer1M": 4.00, "cachedInputPer1M": 1.00, "cacheWritePer1M": null, "outputPer1M": 12.00 }
        ]
      }
    ]
  },
  {
    "id": "grok-4.6",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": 200000, "inputPer1M": 2.00, "cachedInputPer1M": 0.50, "cacheWritePer1M": null, "outputPer1M": 6.00 },
          { "name": "long_context", "maxInputTokens": null, "inputPer1M": 4.00, "cachedInputPer1M": 1.00, "cacheWritePer1M": null, "outputPer1M": 12.00 }
        ]
      }
    ]
  },
  {
    "id": "grok-4.7",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": 200000, "inputPer1M": 2.00, "cachedInputPer1M": 0.50, "cacheWritePer1M": null, "outputPer1M": 6.00 },
          { "name": "long_context", "maxInputTokens": null, "inputPer1M": 4.00, "cachedInputPer1M": 1.00, "cacheWritePer1M": null, "outputPer1M": 12.00 }
        ]
      }
    ]
  },
  {
    "id": "kimi-k3",
    "aliases": [],
    "windows": [
      {
        "effectiveFrom": null,
        "effectiveUntil": null,
        "tiers": [
          { "name": "default", "maxInputTokens": null, "inputPer1M": 3.00, "cachedInputPer1M": 0.30, "cacheWritePer1M": null, "outputPer1M": 15.00 }
        ]
      }
    ]
  }
]
```

**Notes.** Insert these three strings into `notes`, in this order, right after `"mai-code-2 promotional pricing ends 2027-05-17T00:00:00Z."`:

```json
[
  "Promotional windows end at 00:00:00Z on the date the pricing page gives: gemini-4-argon and gemini-4-carbon on 2026-12-01, mai-code-2-flash on 2027-05-17, and gemini-3.8-flash on 2027-01-01 (the page says through December 31, 2026).",
  "gemini-4-argon and gemini-3.8-flash have no published price after their promotions, so later requests are recorded as no_rate_for_date until a price is added.",
  "Model ids are the ids Copilot reports. gpt-5.4-nano, claude-sonnet-4, claude-opus-4.8, claude-fable-5, claude-fable-5.1, claude-fable-5.2, gemini-4-neon, grok-4.5, and kimi-k3 are derived from the pricing page's display names. Claude Opus 4.8 (fast mode) is omitted because its id is unknown."
]
```

**Local provider names.** Change `"localOffload": { ..., "providerNames": [] }` to `"providerNames": ["ollama", "lm_studio", "foundry_local", "llama_cpp"]`.

### docs/configuration.md changes

- In the "How usage is priced" table, replace the Local row's "How it's detected" cell with:

  ```text
  An endpoint of `localhost`, `127.x.x.x`, `::1`, or a host you list; a local provider name (`ollama`, `lm_studio`, `foundry_local`, or `llama_cpp`, which Copilot CLI reports for models you bring or run yourself); or a known local model, such as Qwen3.8-27B, with no endpoint
  ```

- Replace the sentence that lists the built-in prices, `The built-in prices are GitHub's November 2026 rates for ...`, with this text:

  ```text
  The built-in prices are GitHub's rates for every text model on [Models and pricing for GitHub Copilot](https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing) as of 2026-11-20: 34 models, with their long-context tiers and promotional prices. A promotional price ends at 00:00 UTC on the date GitHub gives. Two models need your attention:

  - Claude Opus 4.8 (fast mode) isn't built in, because the model name Copilot reports for it isn't known yet. If you use it, add it under `githubAiCredits.models` with the model name Copilot reports.
  - GitHub hasn't published the prices that follow the promotions for Gemini 4 Argon (ending 2026-12-01) and Gemini 3.8 Flash (ending after 2026-12-31). Later requests for them are recorded with status `no_rate_for_date` until you add a price window or a CopilotLedger update adds the price.
  ```

### Migration 2 (copy exactly)

Append `(2, <this SQL>)` to the `LedgerMigrations` list:

```sql
CREATE TABLE span_links (
    trace_id            TEXT    NOT NULL,
    span_id             TEXT    NOT NULL,
    parent_span_id      TEXT,
    operation_name      TEXT,
    agent_name          TEXT,
    conversation_id     TEXT,
    git_repository_slug TEXT,
    git_branch          TEXT,
    git_commit_sha      TEXT,
    start_time_unix_ms  INTEGER NOT NULL,
    recorded_at_unix_ms INTEGER NOT NULL,
    PRIMARY KEY (trace_id, span_id)
) STRICT, WITHOUT ROWID;

CREATE INDEX ix_span_links_parent ON span_links (trace_id, parent_span_id);
CREATE INDEX ix_span_links_start  ON span_links (start_time_unix_ms);
CREATE INDEX ix_spans_trace       ON spans (trace_id);
```

### Storage contracts

```csharp
// ---- src/CopilotLedger.Storage/PromptChainRow.cs
namespace CopilotLedger.Storage;

public sealed record PromptChainRow
{
    public required string TraceId { get; init; }
    public string? ConversationId { get; init; }
    public required DateTimeOffset StartTime { get; init; }            // earliest span start
    public required DateTimeOffset EndTime { get; init; }              // latest span end
    public required string Repository { get; init; }                   // same key as the Repository rollup
    public required string Branch { get; init; }                       // same key as the Branch rollup
    public required IReadOnlyList<string> Agents { get; init; }        // distinct non-null agent names, ordinal order
    public required IReadOnlyList<string> Models { get; init; }        // distinct non-null model ids, ordinal order
    public required long SpanCount { get; init; }
    public required long UnpricedSpanCount { get; init; }
    public required long InputTokens { get; init; }
    public required long OutputTokens { get; init; }
    public required long CacheReadInputTokens { get; init; }
    public required long CacheWriteInputTokens { get; init; }
    public required decimal AiCredits { get; init; }                   // AiCreditMath.UsdToAiCredits(AiCreditsUsd)
    public required decimal AiCreditsUsd { get; init; }                // GitHubAiCredits channel
    public required decimal FoundryByokUsd { get; init; }
    public required decimal LocalSavedUsd { get; init; }
}

// ---- additions to src/CopilotLedger.Storage/SqliteLedgerStore.cs (existing members unchanged)
public sealed class SqliteLedgerStore
{
    // Inserts links in one transaction and returns the number inserted. Existing (trace_id, span_id) rows are kept unchanged.
    // Throws InvalidOperationException if InitializeAsync hasn't run.
    public Task<int> RecordSpanLinksAsync(IEnumerable<GenAiSpanLink> links, CancellationToken cancellationToken = default);

    // For each trace, copies each invoke_agent link's agent name, conversation id, and Git slug/branch/sha onto spans rows below it,
    // without crossing a nested invoke_agent. Returns the total rows changed by all UPDATE statements. One transaction.
    // Throws InvalidOperationException if InitializeAsync hasn't run.
    public Task<int> BackfillFromAgentSpansAsync(IEnumerable<string> traceIds, CancellationToken cancellationToken = default);

    // Deletes span_links rows whose start_time_unix_ms < startedBefore.ToUnixTimeMilliseconds(). Returns the rows deleted.
    public Task<int> PruneSpanLinksAsync(DateTimeOffset startedBefore, CancellationToken cancellationToken = default);

    // Prompt chains (one per trace_id), most expensive first. Throws ArgumentOutOfRangeException unless 1 <= limit <= 1000.
    public Task<IReadOnlyList<PromptChainRow>> GetTopPromptChainsAsync(LedgerFilter filter, int limit, CancellationToken cancellationToken = default);
}
```

Reuse the merged private helpers of `SqliteLedgerStore`. Every new method calls `EnsureInitialized()` first, so each one throws `InvalidOperationException` before `InitializeAsync` has run. Open connections with `OpenConnectionAsync(cancellationToken)`, which sets `PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;`, and pass null parameters through `OrDbNull`. SQL is always parameterized.

**RecordSpanLinksAsync.** Insert each link with `INSERT INTO span_links (trace_id, span_id, parent_span_id, operation_name, agent_name, conversation_id, git_repository_slug, git_branch, git_commit_sha, start_time_unix_ms, recorded_at_unix_ms) VALUES (...) ON CONFLICT (trace_id, span_id) DO NOTHING;`. `start_time_unix_ms` is `StartTime.ToUnixTimeMilliseconds()`, `recorded_at_unix_ms` comes from the store's `TimeProvider`, and the result is the sum of rows affected.

**BackfillFromAgentSpansAsync.** Use one connection and one transaction. For each distinct trace ID in ordinal order:

1. Read `SELECT span_id, agent_name, conversation_id, git_repository_slug, git_branch, git_commit_sha FROM span_links WHERE trace_id = @trace AND operation_name = 'invoke_agent' ORDER BY span_id;`.
2. For each row `A`:
   - When `A.agent_name` or `A.conversation_id` isn't null, run the **agent statement** with `@agentSpanId = A.span_id`, `@agentName = A.agent_name`, and `@conversationId = A.conversation_id`.
   - When `A.git_repository_slug` isn't null, run the **Git statement** with `@slug`, `@branch`, and `@sha` from `A`.
3. Add each statement's rows affected to the result.

Agent statement (copy exactly):

```sql
WITH RECURSIVE sub(span_id) AS (
    SELECT span_id FROM span_links
     WHERE trace_id = @trace AND parent_span_id = @agentSpanId
       AND (operation_name IS NULL OR operation_name <> 'invoke_agent')
    UNION
    SELECT l.span_id FROM span_links AS l JOIN sub ON l.parent_span_id = sub.span_id
     WHERE l.trace_id = @trace
       AND (l.operation_name IS NULL OR l.operation_name <> 'invoke_agent')
)
UPDATE spans
   SET agent_name      = COALESCE(agent_name, @agentName),
       conversation_id = COALESCE(conversation_id, @conversationId)
 WHERE trace_id = @trace
   AND span_id IN (SELECT span_id FROM sub)
   AND ((agent_name IS NULL AND @agentName IS NOT NULL)
     OR (conversation_id IS NULL AND @conversationId IS NOT NULL));
```

Git statement (copy exactly; same `sub` CTE):

```sql
WITH RECURSIVE sub(span_id) AS (
    SELECT span_id FROM span_links
     WHERE trace_id = @trace AND parent_span_id = @agentSpanId
       AND (operation_name IS NULL OR operation_name <> 'invoke_agent')
    UNION
    SELECT l.span_id FROM span_links AS l JOIN sub ON l.parent_span_id = sub.span_id
     WHERE l.trace_id = @trace
       AND (l.operation_name IS NULL OR l.operation_name <> 'invoke_agent')
)
UPDATE spans
   SET repo_slug       = @slug,
       branch_name     = @branch,
       head_commit_sha = @sha
 WHERE trace_id = @trace
   AND span_id IN (SELECT span_id FROM sub)
   AND repo_root IS NULL AND repo_slug IS NULL;
```

`UNION`, not `UNION ALL`, makes recursion stop even when parent links form a cycle. File-system Git context, which has a non-null `repo_root`, is never overwritten.

**GetTopPromptChainsAsync.**

Query 1 (copy exactly). The four filter predicates are the ones from Sprint 1's rollup query.

```sql
SELECT trace_id,
       MIN(start_time_unix_ms)                                                            AS start_ms,
       MAX(end_time_unix_ms)                                                              AS end_ms,
       MAX(conversation_id)                                                               AS conversation_id,
       MAX(COALESCE(repo_slug, repo_root, '(none)'))                                      AS repository,
       MAX(COALESCE(branch_name, '(none)'))                                               AS branch,
       COUNT(*)                                                                           AS span_count,
       SUM(CASE WHEN pricing_status IN ('priced', 'local_offload') THEN 0 ELSE 1 END)     AS unpriced_span_count,
       SUM(input_tokens)                                                                  AS input_tokens,
       SUM(output_tokens)                                                                 AS output_tokens,
       SUM(cache_read_input_tokens)                                                       AS cache_read_input_tokens,
       SUM(cache_write_input_tokens)                                                      AS cache_write_input_tokens,
       SUM(CASE WHEN billing_channel = 'github_ai_credits' THEN cost_usd_pico ELSE 0 END) AS github_usd_pico,
       SUM(CASE WHEN billing_channel = 'foundry_byok' THEN cost_usd_pico ELSE 0 END)      AS byok_usd_pico,
       SUM(saved_usd_pico)                                                                AS saved_usd_pico
FROM spans
WHERE (@from IS NULL OR start_time_unix_ms >= @from)
  AND (@to IS NULL OR start_time_unix_ms < @to)
  AND (@repository IS NULL OR COALESCE(repo_slug, repo_root, '(none)') = @repository)
  AND (@branch IS NULL OR COALESCE(branch_name, '(none)') = @branch)
GROUP BY trace_id
ORDER BY github_usd_pico + byok_usd_pico DESC, saved_usd_pico DESC, start_ms DESC, trace_id ASC
LIMIT @limit;
```

Query 2, when query 1 returns rows:

- `SELECT DISTINCT trace_id, agent_name, model_id FROM spans WHERE <the same four predicates> AND trace_id IN (@t0, @t1, ...);`
- Parameter names `@t0`, `@t1`, and so on are generated from the row index. Never interpolate values into SQL.
- `Agents` and `Models` are each trace's distinct non-null `agent_name` and `model_id` values, sorted with `StringComparer.Ordinal`.

Build each row:

- `StartTime` and `EndTime` with `DateTimeOffset.FromUnixTimeMilliseconds`.
- Money with `PicoUsd.ToUsd`, and `AiCredits = AiCreditMath.UsdToAiCredits(AiCreditsUsd)`.
- Return rows in query 1's order.

### CachingGitContextResolver

```csharp
namespace CopilotLedger.Core.Git;

public sealed class CachingGitContextResolver : IGitContextResolver
{
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromSeconds(5);

    // null timeProvider uses TimeProvider.System; null timeToLive uses DefaultTimeToLive.
    // Throws ArgumentNullException for a null inner, and ArgumentOutOfRangeException when timeToLive <= TimeSpan.Zero.
    public CachingGitContextResolver(IGitContextResolver inner, TimeProvider? timeProvider = null, TimeSpan? timeToLive = null);

    public GitWorkspaceContext? Resolve(string workingDirectory);
}
```

1. A null or whitespace `workingDirectory` returns `inner.Resolve(workingDirectory)`, uncached.
2. The cache key is `workingDirectory` exactly as given. Compare keys with `StringComparer.OrdinalIgnoreCase` on Windows (`OperatingSystem.IsWindows()`) and `StringComparer.Ordinal` elsewhere.
3. Each entry caches the inner result, including null, with `expiresAt = now + timeToLive`. It's a hit while `now < expiresAt`, where `now = timeProvider.GetUtcNow()`.
4. The resolver is thread-safe; use one `lock`.

### CopilotSessionWorkspaceLocator

```csharp
namespace CopilotLedger.Otlp.Attribution;

public sealed class CopilotSessionWorkspaceLocator
{
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromSeconds(60);

    // COPILOT_HOME when it's set and not blank; otherwise Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot").
    public static string DefaultCopilotHome { get; }

    // Throws ArgumentException when copilotHome is null or blank.
    public CopilotSessionWorkspaceLocator(string copilotHome, TimeProvider? timeProvider = null, TimeSpan? timeToLive = null);

    public string CopilotHome { get; }

    // The session's working directory from <CopilotHome>/session-state/<conversationId>/workspace.yaml, or null.
    public string? GetWorkingDirectory(string? conversationId);
}
```

`DefaultCopilotHome` reads the environment each time it's accessed. `GetWorkingDirectory` never throws:

1. Return null when `conversationId` is null or doesn't match `^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$`. That blocks path traversal from telemetry.
2. Return a cached result, including null, if one hasn't expired. Use an ordinal key and the same time-to-live logic as `CachingGitContextResolver`.
3. Read `Path.Combine(CopilotHome, "session-state", conversationId, "workspace.yaml")` with `File.ReadLines`. If the file is missing, or reading it throws `IOException` or `UnauthorizedAccessException`, the result is null.
4. Find the first line that starts with `cwd:` at column 0 (ordinal). If there isn't one, the result is null. Otherwise `raw = line[4..].Trim()`.
5. Unquote `raw`:
   - **Double-quoted** (starts and ends with `"`, length ≥ 2): take the inside and scan it left to right. `\\` becomes `\`, `\"` becomes `"`, and every other character, including any other backslash, is kept as is.
   - **Single-quoted** (starts and ends with `'`, length ≥ 2): take the inside and replace `''` with `'`.
   - **Plain:** cut at the first ` #` (space and hash), if any, then `TrimEnd()`.
6. The result is the unquoted value if it isn't empty and `Path.IsPathFullyQualified` accepts it; otherwise null. Cache it and return it.

### Ingestion contracts

```csharp
// ---- src/CopilotLedger.Otlp/Ingestion/ILedgerIngestor.cs
using CopilotLedger.Otlp.Decoding;

namespace CopilotLedger.Otlp.Ingestion;

public interface ILedgerIngestor
{
    Task<IngestResult> IngestAsync(OtlpTraceRequest request, CancellationToken cancellationToken = default);
}

// ---- src/CopilotLedger.Otlp/Ingestion/IngestResult.cs
namespace CopilotLedger.Otlp.Ingestion;

public sealed record IngestResult
{
    public required int ReceivedSpans { get; init; }
    public required int RejectedSpans { get; init; }     // normalizer rejections plus cost rejections
    public string? FirstRejectionReason { get; init; }   // null when RejectedSpans is 0
    public required int InferenceSpans { get; init; }   // entries passed to RecordAsync
    public required int RecordedSpans { get; init; }    // new ledger rows; duplicates excluded
}

// ---- src/CopilotLedger.Otlp/Ingestion/LedgerIngestor.cs
namespace CopilotLedger.Otlp.Ingestion;

public sealed class LedgerIngestor : ILedgerIngestor
{
    // The caller initializes the store. Throws ArgumentNullException for null arguments.
    public LedgerIngestor(SqliteLedgerStore store, ICreditCalculator calculator, IGitContextResolver gitResolver, CopilotSessionWorkspaceLocator sessionLocator);

    public Task<IngestResult> IngestAsync(OtlpTraceRequest request, CancellationToken cancellationToken = default);
}
```

`IngestAsync`:

1. Throw `ArgumentNullException` for a null request. Then `batch = GenAiSpanNormalizer.Normalize(request)`.
2. Serialize steps 3–6 with a `SemaphoreSlim(1, 1)` owned by the ingestor.
3. For each span in `batch.InferenceSpans`, in order:
   - `cwd = span.WorkingDirectory ?? sessionLocator.GetWorkingDirectory(span.ConversationId)`;
   - `attributed = span with { WorkingDirectory = cwd }`;
   - price it, and check that both stored amounts fit in pico-USD:

     ```csharp
     CreditCalculationResult calculation;
     try
     {
         calculation = calculator.Calculate(attributed);
         _ = PicoUsd.FromUsd(calculation.UsdCost);
         _ = PicoUsd.FromUsd(calculation.EstimatedCloudSavedUsd);
     }
     catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
     {
         costRejected++;   // "cost out of range": this span gets no ledger entry
         continue;
     }
     ```

   - `git = cwd is null ? null : gitResolver.Resolve(cwd)`. Git context is best effort: if `Resolve` throws `IOException`, `UnauthorizedAccessException`, or `ArgumentException` (for example, `Path.GetFullPath` on a corrupt `gitdir:` line), use null and record the span without Git context;
   - add `new LedgerEntry(attributed, calculation, git)`.
4. `recorded = entries.Count == 0 ? 0 : await store.RecordAsync(entries, ct)`.
5. If `batch.Links` isn't empty, `await store.RecordSpanLinksAsync(batch.Links, ct)`. The links of cost-rejected spans are included.
6. Collect the distinct trace IDs of `batch.Links` and `batch.InferenceSpans`. If there are any, `await store.BackfillFromAgentSpansAsync(traceIds, ct)`.
7. Return the result:
   - `ReceivedSpans = batch.ReceivedSpanCount` and `RejectedSpans = batch.RejectedSpanCount + costRejected`;
   - `FirstRejectionReason = batch.FirstRejectionReason ?? (costRejected > 0 ? "cost out of range" : null)`. Normalization runs before pricing, so a normalizer reason wins even if its span comes later in the request;
   - `InferenceSpans = entries.Count` and `RecordedSpans = recorded`.

   Store exceptions propagate to the caller, which the receiver maps to 503.

### Filtering and per-span errors

Sprint 1's `RecordAsync` fails the whole batch for one bad entry (see "Starting point"). So it only ever receives entries for billable inference spans, and every entry has:

- an inference operation (`chat`, `text_completion`, or `generate_content`), so its status is never `NotBillableOperation`;
- token counts from 0 to `MaxTokenCount`, with cache read plus cache write no greater than input, so its status is never `InvalidUsage`;
- a `UsdCost` and `EstimatedCloudSavedUsd` that `PicoUsd.FromUsd` accepts.

Its batch checks therefore never throw for ingested data. If `RecordAsync` throws `ArgumentException` anyway, that's a bug, and it propagates like any other store exception.

| What's wrong | Found by | What happens | Receiver response |
|---|---|---|---|
| Not an inference span, such as `invoke_agent`, `execute_tool`, a Copilot span with no operation, or another application's HTTP span | Normalizer | Never priced and never a ledger row. Spans with an operation, and every span from a Copilot resource, become links. | Counted as received, not rejected |
| Invalid trace or span ID, or a zero start time | Normalizer | Rejected: no entry and no link | 200 partial success |
| A token count that isn't a non-negative integer, overflows, or exceeds `MaxTokenCount` | Normalizer | Rejected: no entry and no link | 200 partial success |
| `PicoUsd.FromUsd` throws `ArgumentOutOfRangeException` for a cost or savings amount above about $9.22 million; or `Calculate` throws `OverflowException`, which only an extreme overlay rate can cause, and only before Task 3 | Ingestor | Rejected as `cost out of range`: no entry, but its link is kept | 200 partial success |
| A pricing status that bills nothing, such as `unknown_model` or `byok_unpriced` | Calculator | Recorded with that status, its tokens, and $0.00 | 200 success |
| Git context can't be read: the resolver throws `IOException`, `UnauthorizedAccessException`, or `ArgumentException` | Ingestor | Recorded without Git context, under `(none)` | 200 success |
| The same span again | Store | Skipped by `ON CONFLICT DO NOTHING`, and not counted in `RecordedSpans` | 200 success |
| Any exception from `RecordAsync`, `RecordSpanLinksAsync`, or `BackfillFromAgentSpansAsync`, such as a `SqliteException` for a busy, locked, or full database | Store | `IngestAsync` throws. Earlier steps that committed stay, and a retry is idempotent. | 503 with `Retry-After: 5` |

- A cost-rejected span keeps its link because it's structurally valid. A descendant may need that link to reach its `invoke_agent` ancestor during back-fill.
- Around pricing, only `OverflowException` and `ArgumentOutOfRangeException` are caught, and around Git resolution only the three types above. Any other exception is a bug, and it fails the request with 503.
- Once Task 3 bounds rates, `JsonRateCardCalculator.Calculate` can't throw `OverflowException`. Keep the catch anyway, as defense in depth for any `ICreditCalculator` (decision 18). IG8 tests it with a fake calculator, and IG6 tests the pico-USD limit with an in-bound rate.
- The receiver reports `RejectedSpans` as an OTLP partial success (Task 4, receiver step 9). Clients must not retry a partial success, which is right here: the same spans would be rejected again.

### Acceptance criteria

- `rates.json` differs from Sprint 1 only by the 29 appended models, the three inserted notes, and the four `localOffload.providerNames`, exactly as "rates.json changes" lists them. `LoadEmbedded().RatesVersion` is still `2026-11-20`.
- Migration 2 matches this plan exactly. A Sprint 1 database (`user_version` 1) migrates to 2 and keeps its rows.
- The new public surfaces match this section exactly. `docs/configuration.md` has the two changes above.
- `LedgerIngestor` follows "Filtering and per-span errors": it catches only `OverflowException` and `ArgumentOutOfRangeException` around pricing, and only `IOException`, `UnauthorizedAccessException`, and `ArgumentException` around Git resolution. One bad span never fails the other spans in its request.
- Tests never read or write the real `~/.copilot` or `~/.copilot-ledger`; they pass temp paths. `src/` contains no `double`, `float`, or `System.Diagnostics.Process`.
- `dotnet build CopilotLedger.slnx` succeeds with 0 warnings, and `dotnet test CopilotLedger.slnx` passes all Sprint 1 cases (S1 now expects 2), all Task 1 cases, and RR1–RR4 (RR1 has 48 cases), CG1–CG3, WL1–WL4, SL1–SL7, and IG1–IG9, with 0 failed and 0 skipped.

### Verify

```shell
dotnet build CopilotLedger.slnx
dotnet test CopilotLedger.slnx
```

Report the passed, failed, and skipped counts.

## Task 3 specification: Rates Overlay Error Reporting

### Files

Create:

```text
src/CopilotLedger.Core/Pricing/RateCardReader.cs               (internal)
```

Modify:

```text
src/CopilotLedger.Core/Pricing/JsonRateCardCalculator.cs
src/CopilotLedger.Core/Pricing/RateCardDocument.cs
tests/CopilotLedger.Tests/CopilotLedger.Tests.csproj
tests/CopilotLedger.Tests/Pricing/RateOverlayTests.cs
tests/CopilotLedger.Tests/Pricing/RateCardLoadingTests.cs
docs/configuration.md
```

No package is added. The public API of `JsonRateCardCalculator` doesn't change: `EmbeddedResourceName`, `RatesVersion`, `LoadEmbedded()`, `LoadEmbeddedWithOverlay(string)`, `LoadDefault()`, `Parse(string, string?)`, and `Calculate`. Its exceptions don't change either:

- `FileNotFoundException` for a missing overlay file;
- `InvalidDataException("<file>: could not read rates overlay.")` when reading the file throws `IOException` or `UnauthorizedAccessException`;
- `InvalidDataException` for every invalid document, with the messages below.

Valid documents load exactly as before, with the same values. The one exception is a rate above 1,000,000 USD per 1M tokens, which is now rejected (decision 18).

Implement `RateCardReader` and the validation changes below, update `docs/configuration.md` and the test project file, and then change the tests as "Task 3 tests" under "Test specifications" describes.

### Message format

| Form | Used for |
|---|---|
| `<source>: <location>: <problem>` | An overlay |
| `<source>: <problem>` | An overlay whose root isn't an object, or has an unreadable property name |
| `<location>: <problem>` | The built-in rates: the embedded file, or the `json` argument of `Parse` |
| `<problem>` | The built-in rates, in the same two cases |

- `<source>` is the path passed to `LoadEmbeddedWithOverlay`, exactly as given, or `overlay` for `Parse(json, overlayJson)`.
- `<location>` is a path into the document that contains the problem. Property names are joined with `.`, and array indexes are in brackets, counting from 0, as in `foundryByok.models[0].windows[0].tiers[0].outputPer1M`. There's no `$` and no quoting; names appear exactly as they are in the file. For invalid JSON, `<location>` is `line L, column C`.
- `<problem>` is exactly one of the texts in "Message catalog".

### RateCardReader

```csharp
// ---- src/CopilotLedger.Core/Pricing/RateCardReader.cs
namespace CopilotLedger.Core.Pricing;

internal static class RateCardReader
{
    // Reads a rates document into the RateCardDocument classes. isBase selects which properties are required.
    // Throws InvalidDataException with a "<location>: <problem>" or "<problem>" message; the caller adds "<source>: ".
    public static RateCardDocument Read(string json, bool isBase);
}
```

`Read`:

1. Parse with `JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 })`. On `JsonException`, throw `line {LineNumber + 1}, column {BytePositionInLine + 1}: invalid JSON`, with `CultureInfo.InvariantCulture` and 0 for a null value. Dispose the document when reading ends.
2. If the root isn't an object, throw `must contain a JSON object`.
3. Check `schemaVersion` before anything else, with `root.TryGetProperty("schemaVersion", out value)`, which returns the last one if the name repeats:
   - missing or `null`: `schemaVersion: is required`;
   - not a number that `TryGetInt32` accepts: `schemaVersion: must be an integer`;
   - not 1: `schemaVersion: must be 1`.
4. Read the root object as "Reading an object" describes, and return the classes from `RateCardDocument.cs`. An absent optional property stays null, except `aliases`, which defaults to `[]` as in Sprint 1.

### Schema

The **Null** column says what JSON `null` means for that property. **Absent** means it's treated as if the property weren't there. **Error** means it's a value of the wrong kind. This matches Sprint 1: null is absent exactly where the C# property type in `RateCardDocument.cs` is nullable.

| Object | Property | Kind | Null | Required in the built-in rates | Required in an overlay |
|---|---|---|---|---|---|
| root | `schemaVersion` | integer (`int`) | absent | yes | yes |
| root | `ratesVersion` | string | absent | yes | no |
| root | `source` | source object | absent | yes | no |
| root | `notes` | array of strings | absent | no | no |
| root | `githubAiCredits` | GitHub section | absent | yes | no |
| root | `foundryByok` | Foundry section | absent | yes | no |
| root | `localOffload` | local section | absent | yes | no |
| source object | `url` | string | absent | yes | no |
| source object | `retrievedOn` | string | absent | yes | no |
| GitHub section | `hostSuffixes` | array of strings | absent | yes | no |
| GitHub section | `models` | array of priced models | absent | yes | no |
| Foundry section | `hostSuffixes` | array of strings | absent | yes | no |
| Foundry section | `providerNames` | array of strings | absent | yes | no |
| Foundry section | `models` | array of priced models | absent | yes | no |
| local section | `baselineModel` | string | absent | yes | no |
| local section | `models` | array of local models | absent | yes | no |
| local section | `additionalHosts` | array of strings | absent | yes | no |
| local section | `providerNames` | array of strings | absent | yes | no |
| priced model | `id` | string | error | yes | yes |
| priced model | `aliases` | array of strings | error | no | no |
| priced model | `windows` | array of windows | error | yes | yes |
| window | `effectiveFrom` | date and time | absent | no | no |
| window | `effectiveUntil` | date and time | absent | no | no |
| window | `tiers` | array of tiers | error | yes | yes |
| tier | `name` | string | error | yes | yes |
| tier | `maxInputTokens` | integer (`long`) | absent | no | no |
| tier | `inputPer1M` | number | error | yes | yes |
| tier | `cachedInputPer1M` | number | absent | no | no |
| tier | `cacheWritePer1M` | number | absent | no | no |
| tier | `outputPer1M` | number | error | yes | yes |
| local model | `id` | string | error | yes | yes |
| local model | `aliases` | array of strings | error | no | no |

Every element of an array of strings must be a string, and every element of an array of objects must be an object. A `null` element is a value of the wrong kind.

### Reading values

| Kind | Accepted | Otherwise |
|---|---|---|
| object | `JsonValueKind.Object` | `must be an object` |
| array | `JsonValueKind.Array` | `must be an array` |
| string | `JsonValueKind.String`, read with `GetString()` | `must be a string`. If `GetString()` throws `InvalidOperationException`: `is not valid text`. |
| integer | `JsonValueKind.Number` that `TryGetInt32` (for `schemaVersion`) or `TryGetInt64` (for `maxInputTokens`) accepts | `must be an integer` |
| number | `JsonValueKind.Number` that `TryGetDecimal` accepts | `is too large` for a number that `TryGetDecimal` rejects; `must be a number` for any other kind |
| date and time | `JsonValueKind.String` that `TryGetDateTimeOffset` accepts | `must be a date and time, such as 2027-05-17T00:00:00Z` |

These are the conversions Sprint 1's serializer made with `JsonNumberHandling.Strict` (see "Verified facts").

### Reading an object

An object at path `P` (empty for the root) is read in one pass:

1. Go through its properties in document order with `EnumerateObject()`. For each property:
   1. Read its name. If `JsonProperty.Name` throws `InvalidOperationException`, throw `P: contains a property name that is not valid text`, or just `contains a property name that is not valid text` at the root.
   2. The property's path is `P.name`, or `name` at the root.
   3. If the object has no property with exactly that name (ordinal), throw `<path>: is not a known property`. If one of its properties matches ignoring case (`StringComparison.OrdinalIgnoreCase`), append `; did you mean <property>?` with the first such property in schema order.
   4. If the name appeared earlier in this object, throw `<path>: is defined more than once`.
   5. Read the value by the property's kind and Null rule. Read objects and arrays recursively; array elements have the path `<path>[i]`.
2. Then go through the object's required properties in schema order. For the first one that's absent, throw `P.name: is required` (`name: is required` at the root). A property whose Null rule is absent and whose value is `null` counts as absent.

So the reported problem is the first one found in document order, except that `schemaVersion` is checked first, and a missing property is found when its object ends.

### Validation order

`JsonRateCardCalculator.Create(baseJson, overlayJson, overlayName)` becomes:

```text
1. baseDocument = RateCardReader.Read(baseJson, isBase: true)
2. builtIn = Validate(baseDocument)                     // Sprint 1's checks, unchanged
3. if overlayJson is null, return builtIn
4. try
     a. overlay = RateCardReader.Read(overlayJson, isBase: false)
     b. ValidateOverlay(overlay)
     c. merged = Merge(baseDocument, overlay)
     d. ValidateMergedRates(baseDocument, overlay, merged)
   catch (InvalidDataException ex)
     throw new InvalidDataException($"{overlayName}: {ex.Message}", ex)
5. return Validate(merged)                              // can't fail after steps 2 and 4
```

`ValidateOverlay` runs Sprint 1's existing validators on the overlay's own lists, with paths into the overlay. It goes in this order, skipping any property the overlay doesn't have:

1. `githubAiCredits.models`: `ValidateModels(models, BillingChannel.GitHubAiCredits, "githubAiCredits.models")`. That covers every per-model rule, and duplicate identities within the list.
2. `foundryByok.models`: `ValidateModels(models, BillingChannel.FoundryByok, "foundryByok.models")`.
3. `localOffload.models`: `ValidateLocalModels(models, "localOffload.models")`.
4. `localOffload.baselineModel`: `localOffload.baselineModel: must not be blank` when `NormalizeModel` returns null.
5. `githubAiCredits.hostSuffixes`, then `foundryByok.hostSuffixes`: `ValidateHosts`.
6. `foundryByok.providerNames`, then `localOffload.additionalHosts`, then `localOffload.providerNames`: `ValidateNames`.

`ValidateMergedRates` runs the only two checks that need the built-in rates:

1. **Identity collisions,** for `githubAiCredits.models`, `foundryByok.models`, and then `localOffload.models`, when the overlay has that list:
   1. The built-in models that *remain* are those whose `NormalizeModel(id)` isn't the normalized id of any overlay model in the list. This is Sprint 1's replace rule, so a replaced built-in model frees its aliases.
   2. Map the normalized id and each normalized alias of every remaining built-in model to that model's normalized id.
   3. Go through the overlay's models in order. For each, check its normalized id, then its normalized aliases in order. At the first one in the map, throw `<list>[i].id` or `<list>[i].aliases[j]`, followed by `: duplicate model identity in the merged rates: <identity> (built-in model <owner>)`.
2. **Baseline.** Let `b = NormalizeModel(merged.LocalOffload.BaselineModel)`. If no model in the merged `githubAiCredits.models` has a normalized id or normalized alias equal to `b`, throw `localOffload.baselineModel: does not resolve to a githubAiCredits model in the merged rates: <b>`. An overlay can't remove a built-in model, so this check fails only when the overlay sets `baselineModel`.

Delete `SerializerOptions`, `Deserialize`, `ValidateOverlayShape` (and its call in `Merge`), both `ValidateOverlayModels` overloads, and `ValidateOptionalStringElements`. The reader now guarantees what they checked. In `RateCardDocument.cs`, remove every `[JsonPropertyName]` attribute and the `using System.Text.Json.Serialization;` line. The classes and their members stay as they are. `Validate`, `Merge`, `ValidateModels`, `ValidateLocalModels`, `ValidateHosts`, and `ValidateNames` keep their logic and messages. `ValidateRate` changes only as "Rate bound" says.

### Rate bound

Add a constant to `JsonRateCardCalculator`, and replace `ValidateRate` with this method:

```csharp
private const decimal MaxRatePer1M = 1_000_000m;

private static void ValidateRate(decimal rate, string path)
{
    if (rate < 0)
    {
        throw Invalid(null, path, "must be greater than or equal to 0");
    }

    // The bound keeps tokens * rate inside decimal for any long token count, so Calculate never throws.
    if (rate > MaxRatePer1M)
    {
        throw Invalid(null, path, "must be less than or equal to 1000000");
    }

    if (rate * 1_000_000m % 1m != 0m)
    {
        throw Invalid(null, path, "must have no more than 6 decimal places");
    }
}
```

The `try`/`catch (OverflowException)` around the decimal-places check goes away, because `rate * 1_000_000m` can't overflow once the rate is at most 1,000,000. Every rate in the built-in rates and in an overlay goes through `ValidateRate`, through `Validate` and `ValidateOverlay`, so each document reports the bound at a path into itself. `Calculate` doesn't change.

### Message catalog

Problems the reader reports:

| Problem | Reported at |
|---|---|
| `invalid JSON` | `line L, column C`, counting from 1. Columns count UTF-8 bytes. |
| `must contain a JSON object` | The document |
| `contains a property name that is not valid text` | The object that contains the name |
| `is not a known property` | An unknown property |
| `is not a known property; did you mean <property>?` | An unknown property that matches a known one ignoring case |
| `is defined more than once` | The second occurrence of a property |
| `is required` | A missing required property |
| `must be an object`, `must be an array`, `must be a string`, `must be an integer`, `must be a number` | A value of the wrong kind |
| `must be a date and time, such as 2027-05-17T00:00:00Z` | `effectiveFrom` or `effectiveUntil` |
| `is not valid text` | A string that can't be decoded |
| `is too large` | A number outside `decimal`'s range |
| `must be 1` | `schemaVersion` |

Sprint 1's rule texts stay exactly as they are, now at paths into the file that contains the problem:

- `must not be blank`;
- `duplicate model identity: <identity>`, for a duplicate within one file;
- `must contain at least one window` and `must contain at least one tier`;
- `must be later than effectiveFrom` and `windows must be sorted and must not overlap`;
- `must be unique within the window`;
- `the last tier must be unbounded`, `must be greater than 0 for every tier except the last`, and `must be strictly ascending`;
- `must be greater than or equal to 0` and `must have no more than 6 decimal places`;
- `must be a host suffix without a leading '.', '/', or ':'`;
- `ratesVersion: must not be blank` and `does not resolve to a githubAiCredits model: <model>`, which only the built-in rates can produce.

Sprint 1's `is too large` for a rate whose `rate × 1,000,000` overflows can no longer happen, because of the new rule:

| Problem | Reported at |
|---|---|
| `must be less than or equal to 1000000` | An `inputPer1M`, `cachedInputPer1M`, `cacheWritePer1M`, or `outputPer1M` above 1,000,000, in either document |

The two merged-rates problems, which only an overlay can produce:

| Problem | Reported at |
|---|---|
| `duplicate model identity in the merged rates: <identity> (built-in model <id>)` | The overlay model's `id` or alias |
| `does not resolve to a githubAiCredits model in the merged rates: <model>` | `localOffload.baselineModel` |

### Behavior changes

- Every message is now one of the texts above, and every overlay path points into the overlay.
- A property name that appears twice in one object is an error. Sprint 1 silently kept the last value.
- Elements of `notes` must be strings. Sprint 1 accepted `null`.
- A rate above 1,000,000 is rejected. Sprint 1 accepted any rate up to about 7.9 × 10^22.
- `schemaVersion` is checked first, so a file with `"schemaVersion": 2` gets `schemaVersion: must be 1` even when it also has properties this version doesn't know.

### docs/configuration.md changes

Make two changes.

1. In "Price your Foundry BYOK models", replace the sentence `Prices are USD per 1,000,000 tokens, with at most 6 decimal places.` with `Prices are USD per 1,000,000 tokens, from 0 to 1,000,000, with at most 6 decimal places.`
2. Replace the paragraph that starts `If the file is invalid` with the text below, exactly. The `<!-- rates-error-example -->` comments don't render on GitHub; the docs test uses them to find the examples.

````markdown
If the file is invalid, CopilotLedger refuses to load it and reports the first problem it finds. It never silently ignores a broken file. Each error starts with the file's path, then says where the problem is and what's wrong:

- Most errors give a path into your file, such as `foundryByok.models[0].windows[0].tiers[0].outputPer1M`. Indexes count from 0, and they count only the entries in your file.
- If the file isn't valid JSON, the error gives the line and column where reading stopped, counting from 1.
- Two checks compare your file with the built-in rates: a model `id` or alias that a built-in model already uses, and a `localOffload.baselineModel` that isn't a GitHub model. These errors say `in the merged rates`.
- Property names are case-sensitive, and each property can appear only once in its object.

For example, this file leaves out a tier's `outputPer1M`:

<!-- rates-error-example -->
```json
{
  "schemaVersion": 1,
  "foundryByok": {
    "models": [
      { "id": "gpt-6.1-sol", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1.75 } ] } ] }
    ]
  }
}
```

CopilotLedger reports:

```text
/home/me/.copilot-ledger/rates.json: foundryByok.models[0].windows[0].tiers[0].outputPer1M: is required
```
<!-- /rates-error-example -->

This file names a baseline model that doesn't exist:

<!-- rates-error-example -->
```json
{
  "schemaVersion": 1,
  "localOffload": { "baselineModel": "gpt-missing" }
}
```

CopilotLedger reports:

```text
/home/me/.copilot-ledger/rates.json: localOffload.baselineModel: does not resolve to a githubAiCredits model in the merged rates: gpt-missing
```
<!-- /rates-error-example -->

This file has a comma after its last property, which JSON doesn't allow:

<!-- rates-error-example -->
```json
{
  "schemaVersion": 1,
  "ratesVersion": "contoso-2026-11",
}
```

CopilotLedger reports:

```text
/home/me/.copilot-ledger/rates.json: line 4, column 1: invalid JSON
```
<!-- /rates-error-example -->
````

In `tests/CopilotLedger.Tests/CopilotLedger.Tests.csproj`, add this item group so the docs test can read the file from its output directory:

```xml
<ItemGroup>
  <None Include="..\..\docs\configuration.md" Link="docs\configuration.md" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

### Acceptance criteria

- The public members of `JsonRateCardCalculator` and their exceptions are unchanged, and no package was added. `src/CopilotLedger.Core` doesn't call `JsonSerializer`.
- The reader, the validation order, the rate bound, and every message match this section. Every overlay path points into the overlay. `ValidateRate` has no `try`/`catch`.
- `docs/configuration.md` has both changes, and `DocsExamples_MatchRealMessages` passes.
- The Sprint 1 rates tests changed only as "Task 3 tests" says.
- `dotnet build CopilotLedger.slnx` succeeds with 0 warnings, and `dotnet test CopilotLedger.slnx` passes everything from earlier tasks plus the 55 cases that "Task 3 tests" adds, with 0 failed and 0 skipped.

### Verify

```shell
dotnet build CopilotLedger.slnx
dotnet test CopilotLedger.slnx
```

Report the passed, failed, and skipped counts.

## Task 4 specification: Kestrel OTLP Receiver & gh-ledger Daemon

### Files

Create:

```text
src/CopilotLedger.Otlp/Receiver/OtlpReceiverOptions.cs
src/CopilotLedger.Otlp/Receiver/OtlpReceiverApp.cs
src/CopilotLedger.Otlp/Receiver/OtlpHttp.cs                    (internal)
src/CopilotLedger.Cli/CopilotLedger.Cli.csproj
src/CopilotLedger.Cli/Program.cs
src/CopilotLedger.Cli/GhLedgerCli.cs
src/CopilotLedger.Cli/CliContext.cs
src/CopilotLedger.Cli/ICliPlatform.cs
src/CopilotLedger.Cli/SystemCliPlatform.cs
src/CopilotLedger.Cli/Daemon/DaemonFiles.cs                    (internal)
src/CopilotLedger.Cli/Daemon/DaemonHost.cs                     (internal)
src/CopilotLedger.Cli/Daemon/DaemonCommands.cs                 (internal)
src/CopilotLedger.Cli/Rates/RatesCommands.cs                   (internal)
tests/CopilotLedger.Tests/TestSupport/FakeCliPlatform.cs
tests/CopilotLedger.Tests/TestSupport/CliHarness.cs
tests/CopilotLedger.Tests/Otlp/OtlpReceiverTests.cs
tests/CopilotLedger.Tests/Cli/DaemonCommandTests.cs
tests/CopilotLedger.Tests/Cli/RatesCommandTests.cs
```

Also modify `src/CopilotLedger.Otlp/CopilotLedger.Otlp.csproj`, `CopilotLedger.slnx`, `tests/CopilotLedger.Tests/CopilotLedger.Tests.csproj`, and `docs/configuration.md`.

### Steps

**Step 1.** Add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` in its own `ItemGroup` to `src/CopilotLedger.Otlp/CopilotLedger.Otlp.csproj`.

**Step 2.** From the repository root, run:

```shell
dotnet new console -n CopilotLedger.Cli -o src/CopilotLedger.Cli
dotnet add src/CopilotLedger.Cli/CopilotLedger.Cli.csproj package System.CommandLine --version 2.0.15
dotnet add src/CopilotLedger.Cli/CopilotLedger.Cli.csproj reference src/CopilotLedger.Otlp/CopilotLedger.Otlp.csproj
dotnet sln CopilotLedger.slnx add src/CopilotLedger.Cli/CopilotLedger.Cli.csproj
dotnet add tests/CopilotLedger.Tests/CopilotLedger.Tests.csproj reference src/CopilotLedger.Cli/CopilotLedger.Cli.csproj
dotnet add tests/CopilotLedger.Tests/CopilotLedger.Tests.csproj package Microsoft.AspNetCore.TestHost --version 10.0.15
```

**Step 3.** Replace `src/CopilotLedger.Cli/CopilotLedger.Cli.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>gh-ledger</AssemblyName>
    <RootNamespace>CopilotLedger.Cli</RootNamespace>
    <Version>0.2.0</Version>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <InvariantGlobalization>true</InvariantGlobalization>
    <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.0.15" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CopilotLedger.Otlp\CopilotLedger.Otlp.csproj" />
  </ItemGroup>
</Project>
```

Replace `Program.cs` with:

```csharp
using CopilotLedger.Cli;

return await GhLedgerCli.InvokeAsync(args, CliContext.CreateDefault(Console.Out, Console.Error));
```

**Step 4.** Implement the contracts and rules below, update `docs/configuration.md`, and write the "Task 4 tests". These packages are pinned: `System.CommandLine` 2.0.15 and `Microsoft.AspNetCore.TestHost` 10.0.15. Don't take a 3.x or 11.x version.

ASP.NET Core implicit usings aren't available in these projects, so add `using Microsoft.AspNetCore.Builder;`, `Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Http`, and `Microsoft.Extensions.DependencyInjection` where needed.

### Receiver contracts

```csharp
// ---- src/CopilotLedger.Otlp/Receiver/OtlpReceiverOptions.cs
namespace CopilotLedger.Otlp.Receiver;

public sealed record OtlpReceiverOptions
{
    public const int DefaultPort = 4318;
    public const long DefaultMaxRequestBodyBytes = 64L * 1024 * 1024;
    public int Port { get; init; } = DefaultPort;
    public long MaxRequestBodyBytes { get; init; } = DefaultMaxRequestBodyBytes;   // applies to raw and decompressed bodies
}

// ---- src/CopilotLedger.Otlp/Receiver/OtlpReceiverApp.cs
using CopilotLedger.Otlp.Ingestion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace CopilotLedger.Otlp.Receiver;

public static class OtlpReceiverApp
{
    // Serves the OTLP endpoints on localhost (127.0.0.1 and ::1) at options.Port.
    // configureWebHost runs after the defaults; tests pass builder => builder.UseTestServer().
    public static WebApplication Build(OtlpReceiverOptions options, ILedgerIngestor ingestor, Action<IWebHostBuilder>? configureWebHost = null);
}
```

`Build`:

1. Throw `ArgumentNullException` for null arguments, and `ArgumentOutOfRangeException` unless `1 <= Port <= 65535` and `MaxRequestBodyBytes >= 1`.
2. `var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());`
3. `builder.WebHost.UseKestrelCore();`
4. `builder.WebHost.ConfigureKestrel(k => { k.ListenLocalhost(options.Port); k.Limits.MaxRequestBodySize = options.MaxRequestBodyBytes; k.AddServerHeader = false; });`
5. `builder.Services.AddRoutingCore();`
6. `configureWebHost?.Invoke(builder.WebHost);`
7. `var app = builder.Build();` then map three endpoints with `app.MapPost`: `/v1/traces`, `/v1/metrics`, and `/v1/logs`. Return `app`.

Every endpoint handles a request in this order:

1. **Media type** is the `Content-Type` value before any `;`, trimmed. `application/x-protobuf` selects protobuf, and `application/json` selects JSON (ordinal, case-insensitive). Responses use the selected encoding, or JSON when the type is unsupported.
2. **Host.** If `context.Request.Host.Host` isn't `localhost`, `127.0.0.1`, `[::1]`, or `::1` (case-insensitive), return 400 `Host <host> is not a loopback name.`. Write `(none)` when the host is empty.
3. **Unsupported media type** returns 415 `Unsupported Content-Type <type>. Use application/x-protobuf or application/json.` Write `(none)` when the header is missing.
4. **Content-Encoding** (trimmed, case-insensitive): empty or `identity` means none, and `gzip` means gzip. Anything else returns 415 `Unsupported Content-Encoding <value>. Use gzip or no encoding.`
5. **Body**, with limit `max = options.MaxRequestBodyBytes`. Each of these returns 413 `Request body exceeds <max> bytes.`:
   - `Request.ContentLength > max`, checked before reading;
   - more than `max` bytes read, decompressed if gzip (wrap the body in `new GZipStream(body, CompressionMode.Decompress, leaveOpen: true)`, and copy in 81,920-byte chunks into a `MemoryStream`);
   - a `BadHttpRequestException` with `StatusCode == 413` from Kestrel.

   An `InvalidDataException` from gzip returns 400 `Invalid gzip body.`
6. **`/v1/metrics` and `/v1/logs`** return a 200 empty success and discard the body.
7. **Decode** with `OtlpTraceDecoder`. An `OtlpDecodeException` returns 400 `Invalid OTLP protobuf payload: <message>` or `Invalid OTLP JSON payload: <message>`.
8. **Ingest** with `await ingestor.IngestAsync(request, context.RequestAborted)`, even for 0 spans.
   - An `OperationCanceledException` while `RequestAborted` is canceled ends the request without writing a response.
   - Any other exception returns 503 `The ledger is unavailable; retry later.` with the header `Retry-After: 5`.
9. **Success.** When `RejectedSpans` is 0, return 200 with an empty success. Otherwise return 200 with a partial success: `rejected_spans = RejectedSpans`, and `error_message = $"{RejectedSpans} span(s) rejected: {FirstRejectionReason}"`. Task 2's "Filtering and per-span errors" lists what is rejected and why.

Response bodies. `Content-Type` is exactly `application/x-protobuf` or `application/json`.

| Response | Protobuf | JSON (written with `Utf8JsonWriter`) |
|---|---|---|
| Empty success | 0 bytes | `{}` |
| Partial success | `0x0A`, varint length, then the inner message: `0x08`, varint rejected count, `0x12`, varint byte length, UTF-8 message | `{"partialSuccess":{"rejectedSpans":"<n>","errorMessage":"<message>"}}`, with `rejectedSpans` as a string because it's an int64 |
| Error (`google.rpc.Status`, `code` omitted) | `0x12`, varint byte length, UTF-8 message | `{"message":"<message>"}` |

### CLI contracts

```csharp
// ---- src/CopilotLedger.Cli/ICliPlatform.cs
namespace CopilotLedger.Cli;

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

// What gh-ledger needs from the operating system beyond files in its data directory. Tests use a fake.
public interface ICliPlatform
{
    bool IsWindows { get; }
    bool IsMacOS { get; }
    string HomeDirectory { get; }
    string? ProcessPath { get; }                                           // Environment.ProcessPath
    string? ShellPath { get; }                                             // the SHELL environment variable
    string? GetEnvironmentVariable(string name);                           // current process
    string? GetUserEnvironmentVariable(string name);                       // Windows user scope; null on other platforms
    void SetUserEnvironmentVariable(string name, string? value);           // Windows user scope; null deletes; other platforms throw PlatformNotSupportedException
    bool FileExists(string path);
    string? FindOnPath(string fileName);                                   // full path of an executable on PATH, or null
    Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
    int StartDetached(string fileName, IReadOnlyList<string> arguments, string workingDirectory);   // returns the new process id
    bool IsProcessRunning(int processId);
    void KillProcess(int processId);
}

// ---- src/CopilotLedger.Cli/SystemCliPlatform.cs
namespace CopilotLedger.Cli;

public sealed class SystemCliPlatform : ICliPlatform { /* the real implementation; see the rules below */ }

// ---- src/CopilotLedger.Cli/CliContext.cs
using Microsoft.AspNetCore.Hosting;

namespace CopilotLedger.Cli;

public sealed record CliContext
{
    public required TextWriter Out { get; init; }
    public required TextWriter Error { get; init; }
    public required string DataDirectory { get; init; }
    public required string CopilotHome { get; init; }
    public required string WorkingDirectory { get; init; }
    public required TimeProvider TimeProvider { get; init; }
    public required ICliPlatform Platform { get; init; }
    public Action<IWebHostBuilder>? ConfigureWebHost { get; init; }                     // tests: builder => builder.UseTestServer()
    public TimeSpan DaemonPollInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan DaemonStartTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan DaemonStopTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public string DatabasePath => Path.Combine(DataDirectory, "ledger.db");
    public string RatesOverlayPath => Path.Combine(DataDirectory, "rates.json");

    // DataDirectory: COPILOT_LEDGER_HOME when set and not blank, else LedgerPaths.DataDirectory.
    // CopilotHome: CopilotSessionWorkspaceLocator.DefaultCopilotHome. WorkingDirectory: Environment.CurrentDirectory.
    // TimeProvider: TimeProvider.System. Platform: new SystemCliPlatform().
    public static CliContext CreateDefault(TextWriter output, TextWriter error);
}

// ---- src/CopilotLedger.Cli/GhLedgerCli.cs
namespace CopilotLedger.Cli;

public static class GhLedgerCli
{
    public static string Version { get; }   // AssemblyInformationalVersionAttribute: "0.2.0"

    // Builds a new RootCommand, parses args, and invokes it with
    // new InvocationConfiguration { Output = context.Out, Error = context.Error, ProcessTerminationTimeout = null }.
    public static Task<int> InvokeAsync(string[] args, CliContext context, CancellationToken cancellationToken = default);
}
```

The public surface of `CopilotLedger.Cli` is exactly these five types. Commands write through `context.Out` and `context.Error` only, never `Console`.

**SystemCliPlatform** is the only file in `src/` that may use `System.Diagnostics.Process`. It doesn't run `git`.

- `IsWindows` and `IsMacOS` are `OperatingSystem.IsWindows()` and `OperatingSystem.IsMacOS()`.
- `HomeDirectory` is `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)`.
- `ProcessPath` is `Environment.ProcessPath`, and `ShellPath` is `Environment.GetEnvironmentVariable("SHELL")`.
- `GetUserEnvironmentVariable` and `SetUserEnvironmentVariable` call `Environment.GetEnvironmentVariable` and `Environment.SetEnvironmentVariable` with `EnvironmentVariableTarget.User` on Windows. Setting writes `HKCU\Environment` and broadcasts the change.
- `FindOnPath` checks each non-empty `PATH` entry, split on `Path.PathSeparator`, for `fileName`. On Windows it appends `.exe` when `fileName` has no extension. It returns the first path where `File.Exists` is true.
- `RunAsync` starts the process with `UseShellExecute = false`, `CreateNoWindow = true`, standard output and error redirected as UTF-8, and arguments passed through `ArgumentList`. It reads both streams concurrently, then awaits `WaitForExitAsync(cancellationToken)`.
- `StartDetached` starts the process with `UseShellExecute = false`, `CreateNoWindow = true`, no redirection, `ArgumentList`, and `WorkingDirectory`. It returns `Process.Id` and disposes the `Process` object, which doesn't stop the process.
- `IsProcessRunning` and `KillProcess` use `Process.GetProcessById`. They treat `ArgumentException` and `InvalidOperationException` as "not running" and do nothing.

### Commands in this task

```text
gh-ledger daemon run [--port <1-65535>] [--log-file <path>]
gh-ledger daemon start [--port <1-65535>]
gh-ledger daemon stop
gh-ledger rates init [--force]
gh-ledger rates validate [--file <path>]
```

- The root description is `CopilotLedger: local AI spend ledger for GitHub Copilot`.
- `--port` defaults to 4318. A validator rejects other values with `--port must be from 1 to 65535.`
- Each command has a one-line description, and System.CommandLine supplies `--help` and `--version`.
- Exit codes: 0 means success, and 1 means a parse or validation error or a failed command.

### Daemon files and lifecycle

`DaemonFiles` uses these files in `context.DataDirectory`: `daemon.lock`, `daemon.json`, `daemon.stop`, and `daemon.log`.

- **Lock.** Acquire it with `new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)`; an `IOException` means another daemon holds it. `IsHeld` opens the lock the same way, returns false and closes it immediately if the open succeeds, and returns true on an `IOException`.
- **State file.** `daemon.json` is written with `System.Text.Json` using camelCase names and `WriteIndented = true`, through `daemon.json.tmp` and then `File.Move(tmp, path, overwrite: true)`:

```json
{
  "schemaVersion": 1,
  "processId": 4242,
  "startedAt": "2026-11-20T18:00:00.0000000+00:00",
  "endpoint": "http://localhost:4318",
  "version": "0.2.0",
  "databasePath": "/home/me/.copilot-ledger/ledger.db"
}
```

**`daemon run`.** Log lines are `{context.TimeProvider.GetUtcNow():O} {message}`. They go to `--log-file` when it's given (append, UTF-8 without BOM, `AutoFlush = true`, creating the directory), and to `context.Out` otherwise.

1. `Directory.CreateDirectory(DataDirectory)`.
2. Acquire the lock. If another daemon holds it, log `gh-ledger daemon is already running.` and return 1.
3. Delete a leftover `daemon.stop`.
4. Load the calculator. If `RatesOverlayPath` exists, use `JsonRateCardCalculator.LoadEmbeddedWithOverlay(RatesOverlayPath)`; otherwise use `LoadEmbedded()`. Never use `LoadDefault()`, because the data directory can be overridden. On `InvalidDataException`, log `Invalid rates overlay: <ex.Message>` and return 1.
5. Create `new SqliteLedgerStore(DatabasePath, TimeProvider)` and `await InitializeAsync`. On failure, log `Cannot open the ledger at <path>: <message>` and return 1.
6. Prune: `await store.PruneSpanLinksAsync(now - TimeSpan.FromDays(7))`.
7. Create the ingestor: `new LedgerIngestor(store, calculator, new CachingGitContextResolver(new FileSystemGitContextResolver(), TimeProvider), new CopilotSessionWorkspaceLocator(CopilotHome, TimeProvider))`.
8. Build the app: `OtlpReceiverApp.Build(new OtlpReceiverOptions { Port = port }, ingestor, context.ConfigureWebHost)`.
9. On platforms other than Windows, hold `PosixSignalRegistration.Create(PosixSignal.SIGHUP, c => c.Cancel = true)` for the rest of the run, so closing a terminal doesn't end the daemon.
10. `await app.StartAsync(ct)`. On `IOException`, log `Port <port> is in use. Stop the other program, or run gh-ledger daemon run --port <port>.` and return 1.
11. Write `daemon.json`, then log `gh-ledger <Version> daemon listening on http://localhost:<port> (pid <Environment.ProcessId>); ledger <DatabasePath>`.
12. Loop with `await Task.Delay(context.DaemonPollInterval, linkedToken)`, where `linkedToken` combines `ct` and `app.Lifetime.ApplicationStopping`. That token fires on Ctrl+C and SIGTERM through `ConsoleLifetime`.
    - Leave the loop when `daemon.stop` exists, after deleting it, or when the token is canceled.
    - Prune again whenever 6 hours have passed since the last prune.
13. In `finally`:
    - `await app.StopAsync(CancellationToken.None)`;
    - delete `daemon.json`;
    - dispose the app and release the lock;
    - log `gh-ledger daemon stopped`;
    - return 0.

Run steps 3–12 inside a `try`, so an early `return 1` still releases the lock and disposes whatever was created. Log `gh-ledger daemon stopped` only if the app started.

**`daemon start`.**

1. If the lock is held, print `gh-ledger daemon is already running (pid <pid>).`, taking the PID from `daemon.json`, or `gh-ledger daemon is already running.` if the file can't be read. Return 0.
2. Check the executable. `exe = Platform.ProcessPath`. Take its file name as the text after the last `/` or `\`, and drop a trailing `.exe` (case-insensitive). If that name isn't `gh-ledger` (case-insensitive), write `Run this command from the gh-ledger executable (current process: <exe or unknown>).` to `Error` and return 1.
3. Delete a stale `daemon.json`, then start the daemon: `pid = Platform.StartDetached(exe, ["daemon", "run", "--port", "<port>", "--log-file", Path.Combine(DataDirectory, "daemon.log")], DataDirectory)`.
4. Every 100 ms until `DaemonStartTimeout`:
   - When `daemon.json` exists with `processId == pid`, print `gh-ledger daemon started (pid <pid>) on http://localhost:<port>.` and return 0.
   - Stop polling early if `!Platform.IsProcessRunning(pid)`.
5. Write `gh-ledger daemon did not start; see <log path>.` to `Error` and return 1.

**`daemon stop`.**

1. If the lock isn't held, print `gh-ledger daemon is not running.` and return 0.
2. Read the PID from `daemon.json`, if possible. Create an empty `daemon.stop`.
3. Every 100 ms until `DaemonStopTimeout`: when the lock is released, print `gh-ledger daemon stopped.` and return 0.
4. If the timeout passes:
   - If the PID is known and `Platform.IsProcessRunning(pid)`, call `Platform.KillProcess(pid)`, print `gh-ledger daemon did not stop within <N> seconds; stopped process <pid>.`, and return 0.
   - Otherwise, write `gh-ledger daemon did not stop within <N> seconds.` to `Error` and return 1.

### Rates commands

**`rates init`.**

- If `RatesOverlayPath` exists and `--force` isn't given, write `<path> already exists. Use --force to replace it.` to `Error` and return 1.
- Otherwise create the directory and write this text exactly: UTF-8 without BOM, `\n` line endings, and a final newline.

```json
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
```

Then print `Created <path>. Add your prices, then run: gh-ledger rates validate` and return 0.

**`rates validate`.** The path is `Path.GetFullPath(<--file value>, context.WorkingDirectory)` when `--file` is given, and `RatesOverlayPath` otherwise.

- If the file doesn't exist and `--file` was given, write `<path> does not exist.` to `Error` and return 1.
- If the default file doesn't exist, print `No rates overlay at <path>; using the built-in rates (<LoadEmbedded().RatesVersion>).` and return 0.
- Otherwise call `JsonRateCardCalculator.LoadEmbeddedWithOverlay(path)`:
  - On success, print `<path> is valid. Rates version: <RatesVersion>.` and return 0.
  - On `InvalidDataException`, write `Invalid rates overlay: <ex.Message>` to `Error` and return 1. Task 3 defines the message, which starts with the path.

### docs/configuration.md changes

- In "Where CopilotLedger keeps its files", add these rows after the `rates.json` row:

  | Path | Purpose |
  |---|---|
  | `~/.copilot-ledger/daemon.json` | Written while the daemon runs: process ID, endpoint, start time, and database path |
  | `~/.copilot-ledger/daemon.lock` | Held while the daemon runs, so only one daemon runs at a time |
  | `~/.copilot-ledger/daemon.log` | The daemon's log when it's started with `gh-ledger daemon start` |

  After the table, add this sentence:

  ```text
  Set the `COPILOT_LEDGER_HOME` environment variable to keep these files in another directory.
  ```

- In "The rates overlay", after its first paragraph, add:

  ```text
  Run `gh-ledger rates init` to create a starter file, and `gh-ledger rates validate` to check it against the same rules the daemon uses.
  ```

### Test support

- `FakeCliPlatform : ICliPlatform` (tests only) has settable properties:
  - `IsWindows`, `IsMacOS`, `HomeDirectory`, `ProcessPath` (default `/opt/gh-ledger/gh-ledger`), and `ShellPath`;
  - dictionaries `ProcessVariables` and `UserVariables`, and the list `UserVariableWrites` of `(name, value)`, where a write also updates `UserVariables` (null removes);
  - `PathExecutables` (name → full path) for `FindOnPath`, and `ExistingFiles` for `FileExists`;
  - `ProcessRuns` with a `RunHandler`, which returns exit code 0 and empty output by default;
  - `DetachedStarts` with a `StartDetachedHandler`;
  - `RunningProcesses` and `KilledProcesses`. `IsProcessRunning(pid)` is `pid == Environment.ProcessId || RunningProcesses.Contains(pid)`.
- `CliHarness : IAsyncDisposable` (tests only) takes a root directory, an optional `FakeCliPlatform`, and an optional `TimeProvider` (default `TimeProvider.System`).
  - Its context uses `DataDirectory = <root>/data`, `CopilotHome = <root>/copilot-home`, and `WorkingDirectory = <root>`.
  - It sets `ConfigureWebHost = b => b.UseTestServer()`, `DaemonPollInterval = 50 ms`, and start and stop timeouts of 10 s.
  - `RunAsync(params string[] args)` and `RunInAsync(string workingDirectory, params string[] args)` return `CliRun(int ExitCode, string Out, string Error)`, using new `StringWriter`s and replacing `\r\n` with `\n`.
  - `StartInProcessDaemonAsync()` runs `daemon run` on a background task with its own writers and waits up to 10 s for `daemon.json`.
  - The default `StartDetachedHandler` does the same with the arguments after `daemon run`, and returns `Environment.ProcessId`.
  - `DisposeAsync` writes `daemon.stop` if an in-process daemon is running, then awaits it for up to 10 s.

### Acceptance criteria

- `CopilotLedger.slnx` lists Core, Storage, Otlp, Cli, and Tests. The Cli `.csproj` matches Step 3, and `Program.cs` matches.
- The public types are exactly those specified: in Otlp, `OtlpReceiverOptions` and `OtlpReceiverApp` are added; Cli has the five types above.
- Package versions are exactly System.CommandLine 2.0.15 and Microsoft.AspNetCore.TestHost 10.0.15.
- `System.Diagnostics.Process` appears only in `src/CopilotLedger.Cli/SystemCliPlatform.cs`. `src/` contains no `double` or `float`.
- No test opens a socket, starts a process, touches the network, or uses the real `~/.copilot-ledger` or `~/.copilot`. Receiver and daemon tests use `UseTestServer()`.
- `docs/configuration.md` has the changes above.
- `rates validate`, `daemon run`, and their tests use the overlay messages exactly as Task 3 produces them, after `Invalid rates overlay: `.
- `dotnet build CopilotLedger.slnx` succeeds with 0 warnings, and `dotnet test CopilotLedger.slnx` passes everything from earlier tasks plus RC1–RC12, DM1–DM5, and RT1–RT3, with 0 failed and 0 skipped.

### Verify

```shell
dotnet build CopilotLedger.slnx
dotnet test CopilotLedger.slnx
dotnet run --project src/CopilotLedger.Cli -- --help
```

Report the passed, failed, and skipped counts. The last command must list the `daemon` and `rates` commands.

## Task 5 specification: Reporting Commands, One-Command Install & Single-Binary Packaging

### Files

Create:

```text
src/CopilotLedger.Cli/Reporting/Period.cs                      (internal)
src/CopilotLedger.Cli/Reporting/LedgerFormat.cs                (internal)
src/CopilotLedger.Cli/Reporting/TableWriter.cs                 (internal)
src/CopilotLedger.Cli/Reporting/ReportCommands.cs              (internal)
src/CopilotLedger.Cli/Install/CopilotOtelEnvironment.cs        (internal)
src/CopilotLedger.Cli/Install/InstallManifest.cs               (internal)
src/CopilotLedger.Cli/Install/InstallCommands.cs               (internal)
docs/cli.md
tests/CopilotLedger.Tests/Cli/ReportCommandTests.cs
tests/CopilotLedger.Tests/Cli/InstallCommandTests.cs
```

Modify:

```text
src/CopilotLedger.Cli/CopilotLedger.Cli.csproj
src/CopilotLedger.Cli/GhLedgerCli.cs              (register the new commands)
.github/workflows/ci.yml
docs/configuration.md
```

No new public types are added.

### Commands in this task

```text
gh-ledger status [--json]
gh-ledger branch [--repo <key> | --all-repos] [--since <period>] [--json]
gh-ledger agents [--repo <key>] [--since <period>] [--json]
gh-ledger top-prompts [--repo <key>] [--since <period>] [--limit <1-100>] [--json]
gh-ledger install [--dry-run]
gh-ledger uninstall [--dry-run]
```

- `--repo` takes a repository key as the rollups produce it: an `owner/repo` slug, a repository root, or `(none)`.
- `--limit` defaults to 10. A validator rejects other values with `--limit must be from 1 to 100.`
- A `branch` validator rejects `--repo` with `--all-repos`: `Use either --repo or --all-repos, not both.`

### Periods (`--since`)

The value is trimmed and case-insensitive, and defaults to `30d`. `now = context.TimeProvider.GetUtcNow()`, `zone = context.TimeProvider.LocalTimeZone`, and `today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime)`. `Midnight(d)` is `new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(d.ToDateTime(TimeOnly.MinValue)))`.

| Value | Label | From (inclusive) |
|---|---|---|
| `today` | `today` | `Midnight(today)` |
| `<N>d`, where 1 ≤ N ≤ 3650 | `today` when N is 1, else `last <N> days` | `Midnight(today.AddDays(-(N - 1)))` |
| `all` | `all time` | null |
| `yyyy-MM-dd` (`DateOnly.TryParseExact`, invariant) | `since yyyy-MM-dd` | `Midnight(date)` |

`To` is always null. Any other value fails validation with `Invalid --since value <value>. Use today, all, <N>d (1-3650), or yyyy-MM-dd.`

### Formatting

- Credits: `Math.Round(v, 2, MidpointRounding.AwayFromZero).ToString("N2", CultureInfo.InvariantCulture)`.
- USD: `"$"` plus the same rounding and format.
- Counts: `v.ToString("N0", CultureInfo.InvariantCulture)`.
- Local time: `TimeZoneInfo.ConvertTime(t, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)`.
- JSON:
  - Write with `Utf8JsonWriter` and `Indented = true`.
  - Money is unrounded: `WritePropertyName(name)` then `WriteRawValue(v.ToString("0.############", CultureInfo.InvariantCulture))`.
  - Times are `t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)`, or JSON `null`.
  - Counts are JSON integers.
- Tables:
  - Each column has a header and an alignment. A column's width is the longer of its header and its longest cell.
  - Cells are padded (left-aligned with `PadRight`, right-aligned with `PadLeft`) and joined with two spaces, and each line is trimmed at the end.
  - Print the header line, then the rows, each with `WriteLine`. There's no separator line.
- Money columns, all right-aligned: `Spans`, `Unpriced`, `AI credits`, `GitHub USD`, `BYOK USD`, `Local saved`. Their cells are `SpanCount`, `UnpricedSpanCount`, `AiCredits`, `AiCreditsUsd`, `FoundryByokUsd`, and `LocalSavedUsd`, formatted as counts, credits, then USD.

Every reporting command opens `new SqliteLedgerStore(context.DatabasePath, context.TimeProvider)` and awaits `InitializeAsync()`, which creates an empty ledger if there isn't one.

### status

1. **Daemon line.** If the lock is held, print `running (pid <pid>) on <endpoint> since <local startedAt>` from `daemon.json`, or just `running` if the file can't be read. Otherwise print `not running; start it with: gh-ledger daemon start`.
2. **Rates line.** If `RatesOverlayPath` exists, load it with `LoadEmbeddedWithOverlay`. On success print its `RatesVersion`; on `InvalidDataException` print `invalid overlay: <ex.Message>`, which starts with the path. Without an overlay, print `LoadEmbedded().RatesVersion`. An invalid overlay doesn't change the exit code.
3. **Copilot line.** Look up each variable with `Platform.GetUserEnvironmentVariable(name) ?? Platform.GetEnvironmentVariable(name)`, then pick the first matching case:
   - `COPILOT_OTEL_ENABLED` is `true` (trimmed, case-insensitive): `OTel export enabled (COPILOT_OTEL_ENABLED=true)`.
   - `OTEL_EXPORTER_OTLP_ENDPOINT` is a local endpoint: `OTel export enabled (OTEL_EXPORTER_OTLP_ENDPOINT=<value>)`. A local endpoint is `http://localhost:4318` or `http://127.0.0.1:4318`, compared after trimming whitespace and trailing `/`, case-insensitive.
   - Otherwise: `OTel export not enabled; run: gh-ledger install`.
4. **Warnings** come from `CopilotOtelEnvironment.GetWarnings`, using the same lookup.
5. **Periods** are `today`, `7d`, and `30d`. Each total is the exact sum of the `GetRollupAsync(LedgerGrouping.Repository, new LedgerFilter { From = from })` rows.

Text output (one line per item, blank line before the table):

```text
gh-ledger <Version>
Daemon:  <daemon line>
Ledger:  <DatabasePath>
Rates:   <rates line>
Copilot: <copilot line>
Warning: <each warning, one line each>

<table: Period (left) + money columns; rows "Today", "Last 7 days", "Last 30 days">
```

JSON output:

```json
{
  "version": "0.2.0",
  "daemon": { "running": false, "processId": null, "endpoint": null, "startedAt": null },
  "databasePath": "/home/me/.copilot-ledger/ledger.db",
  "ratesVersion": "2026-11-20",
  "ratesError": null,
  "copilotOtelEnabled": false,
  "warnings": [],
  "periods": [
    {
      "label": "today",
      "from": "2026-11-21T05:00:00.0000000+00:00",
      "spans": 3,
      "unpricedSpans": 0,
      "inputTokens": 2600000,
      "outputTokens": 162000,
      "cacheReadInputTokens": 1200000,
      "cacheWriteInputTokens": 100000,
      "aiCredits": 342,
      "aiCreditsUsd": 3.42,
      "foundryByokUsd": 0,
      "localSavedUsd": 0.23
    }
  ]
}
```

`periods` holds three objects, labeled `today`, `last 7 days`, and `last 30 days`. `ratesVersion` is null when the overlay is invalid, and `ratesError` then holds `ex.Message`.

### branch, agents, and top-prompts

The **scope** is the repository key that filters a report:

- `--repo <key>` uses that key.
- `--all-repos` means every repository; it applies to `branch` only.
- Otherwise, `branch` resolves `context.WorkingDirectory` with `new FileSystemGitContextResolver()` and uses `RepositorySlug ?? RepositoryRoot`, or every repository if that resolves to nothing. `agents` and `top-prompts` use every repository.

A scope name is the key, or `all repositories`.

| Command | Rows | Title | Columns before the money columns | Empty message |
|---|---|---|---|---|
| `branch` | `GetRollupAsync(Branch, filter)` | `Branches in <scope> (<label>)` | `Branch` (left) for one repository; `Repository`, `Branch` (left) for all | `No spend recorded in <scope> (<label>).` |
| `agents` | `GetRollupAsync(Agent, filter)` | `Agents in <scope> (<label>)` | `Agent` (left) | `No spend recorded in <scope> (<label>).` |
| `top-prompts` | `GetTopPromptChainsAsync(filter, limit)` | `Top <n> prompts by cost in <scope> (<label>)`, or `Top 1 prompt by cost ...` when n is 1 | see below | `No prompts recorded in <scope> (<label>).` |

The filter is `new LedgerFilter { From = period.From, Repository = scope key or null }`. Text output is the title, a blank line, then the table, or only the empty message.

`top-prompts` uses its own column list:

- `#` (right; the rank, starting at 1);
- `Started` (left; local start time);
- `Repository` (left);
- `Agents` and `Models` (left; joined with `, `, or `-` when empty);
- `Spans`, `AI credits`, `GitHub USD`, `BYOK USD`, and `Local saved` (right);
- `Trace` (left; the full trace ID).

JSON output for all three: `{ "period": <label>, "from": <time or null>, "repository": <scope key or null>, "rows": [...] }`. Each row has:

- **branch:** `repository` and `branch`;
- **agents:** `agent`;
- **top-prompts:** `rank`, `traceId`, `conversationId`, `startTime`, `endTime`, `repository`, `branch`, `agents` (array), and `models` (array).

Every row then has `spans`, `unpricedSpans`, `inputTokens`, `outputTokens`, `cacheReadInputTokens`, `cacheWriteInputTokens`, `aiCredits`, `aiCreditsUsd`, `foundryByokUsd`, and `localSavedUsd`.

### CopilotOtelEnvironment

`internal static IReadOnlyList<string> GetWarnings(Func<string, string?> lookup)`. A variable is set when its value isn't null or whitespace. Values are trimmed before comparison. Warnings come in this order:

1. `OTEL_EXPORTER_OTLP_ENDPOINT` is set and isn't a local endpoint: `OTEL_EXPORTER_OTLP_ENDPOINT is <value>; Copilot exports there instead of to CopilotLedger at http://localhost:4318.`
2. `COPILOT_OTEL_FILE_EXPORTER_PATH` is set: `COPILOT_OTEL_FILE_EXPORTER_PATH is set; Copilot writes telemetry to that file instead of exporting it to CopilotLedger.`
3. `COPILOT_OTEL_EXPORTER_TYPE` is set and isn't `otlp-http` (case-insensitive): `COPILOT_OTEL_EXPORTER_TYPE is <value>; remove it or set it to otlp-http.`
4. For each set variable among `OTEL_EXPORTER_OTLP_CERTIFICATE`, `OTEL_EXPORTER_OTLP_CLIENT_CERTIFICATE`, `OTEL_EXPORTER_OTLP_CLIENT_KEY`, `OTEL_EXPORTER_OTLP_TRACES_CERTIFICATE`, `OTEL_EXPORTER_OTLP_TRACES_CLIENT_CERTIFICATE`, and `OTEL_EXPORTER_OTLP_TRACES_CLIENT_KEY`, in that order: `<name> is set; Copilot turns off export to http:// endpoints while TLS settings are set.`
5. For `OTEL_EXPORTER_OTLP_PROTOCOL`, then `OTEL_EXPORTER_OTLP_TRACES_PROTOCOL`, when the value is `grpc` (case-insensitive): `<name> is grpc; CopilotLedger accepts only http/protobuf and http/json.`

### install

The manifest `<DataDirectory>/install.json` is written with `System.Text.Json`, camelCase names, and `WriteIndented = true`. It deserializes into the internal `InstallManifest` record:

```json
{
  "schemaVersion": 1,
  "installedAt": "2026-11-21T15:00:00.0000000+00:00",
  "executablePath": "C:\\tools\\gh-ledger.exe",
  "windowsUserVariableSet": true,
  "windowsUserVariablePreviousValue": null,
  "profileFiles": [],
  "ghAliasSet": true
}
```

Steps, in output order. `--dry-run` prints the `Would ...` lines instead and changes nothing: no variable, no file, no process, and no manifest.

1. **Check the executable.** `exe = Platform.ProcessPath`. Take its file name as the text after the last `/` or `\`, and drop a trailing `.exe` (case-insensitive). If that isn't `gh-ledger`, write `Run install from the gh-ledger executable (current process: <exe or unknown>).` to `Error` and return 1 without changes.
2. **Warnings.** Print `Warning: <text>` for each `CopilotOtelEnvironment.GetWarnings(lookup)` result, using status's lookup.
3. **Read the old manifest.** Read `install.json` if it exists and parses; otherwise there's no previous manifest.
4. **Enable Copilot's OTel export.**
   - **Windows.** `current = Platform.GetUserEnvironmentVariable("COPILOT_OTEL_ENABLED")`.
     - If `current` is `true` (trimmed, case-insensitive), print `COPILOT_OTEL_ENABLED=true is already set for your user account.`
     - Otherwise call `Platform.SetUserEnvironmentVariable("COPILOT_OTEL_ENABLED", "true")` and print `Set COPILOT_OTEL_ENABLED=true for your user account.` In dry-run mode, print `Would set COPILOT_OTEL_ENABLED=true for your user account.` instead.

     The new manifest records `windowsUserVariableSet = true` when this run or a previous manifest set the variable. Its `windowsUserVariablePreviousValue` is the previous manifest's value when that manifest set the variable; otherwise it's `current`.
   - **Other platforms.** Pick the profile from the file name of `Platform.ShellPath`, the text after its last `/`: `zsh` uses `~/.zshrc`; `bash` uses `~/.bash_profile` on macOS and `~/.bashrc` elsewhere; anything else uses `~/.profile`. `~` is `Platform.HomeDirectory`.
     - If the profile already contains the line `# >>> copilot-ledger >>>`, print `<profile> already sets COPILOT_OTEL_ENABLED.`
     - Otherwise append a separator and the block below, then print `Added COPILOT_OTEL_ENABLED=true to <profile>.` (dry run: `Would add COPILOT_OTEL_ENABLED=true to <profile>.`). The separator is `""` when the file is missing or empty, `"\n"` when it ends with `\n`, and `"\n\n"` otherwise.

       ```text
       # >>> copilot-ledger >>>
       export COPILOT_OTEL_ENABLED=true
       # <<< copilot-ledger <<<
       ```

       The block is written with `\n` line endings and a final `\n`.

     `profileFiles` is the union of the previous manifest's files and this profile.
5. **gh alias.** `gh = Platform.FindOnPath("gh")`.
   - If `gh` is null, print `GitHub CLI (gh) was not found, so the gh ledger alias was not set. Run gh-ledger directly.`
   - Otherwise:
     - Build `expansion = Quote(target) + " \"$@\""`. `target` is `exe` with `\` replaced by `/` on Windows, and `Quote(s) = "'" + s.Replace("'", "'\\''") + "'"`.
     - Run `Platform.RunAsync(gh, ["alias", "set", "ledger", expansion, "--shell", "--clobber"], ct)`. Pass the expansion as one argument, never through stdin.
     - Exit code 0 prints `Set gh alias: gh ledger runs <exe>.` Any other result prints `Warning: could not set the gh alias: <trimmed stderr, or exit code N when stderr is empty>`.
     - Dry run prints `Would set gh alias: gh ledger runs <exe>.` instead.
     - Then, on Windows, if `FindSh()` is null, print `Warning: GitHub CLI needs sh.exe from Git for Windows to run shell aliases, so gh ledger won't work until Git for Windows is installed. Run gh-ledger directly.`

     `FindSh()` is `Platform.FindOnPath("sh")` if that isn't null. Otherwise, when `git = Platform.FindOnPath("git")` isn't null, it's the first of `<dir>\..\bin\sh.exe` and `<dir>\..\apps\git\current\bin\sh.exe` for which `Platform.FileExists` is true, where `<dir>` is `git` up to its last `\` or `/`. Build both paths by string concatenation with `\`. If neither exists, it's null.
   - `ghAliasSet` is true when this run set the alias or a previous manifest says it was set.
6. **Manifest.** Write `install.json`, except in dry-run mode.
7. **Daemon.** Run the `daemon start` logic on port 4318 and print its line. In dry-run mode, print `Would start the gh-ledger daemon.` instead. If it fails, install still finishes, and its exit code is 1.
8. **Last line.** Print `CopilotLedger is installed. Restart the GitHub Copilot app, VS Code, and open terminals so they pick up COPILOT_OTEL_ENABLED.`, or `Dry run: no changes made.` in dry-run mode. Return 0 unless step 7 failed.

### uninstall

1. If `install.json` is missing or can't be parsed, print `CopilotLedger is not installed; nothing to undo.` and return 0.
2. If the daemon is running, run the `daemon stop` logic and print its line, or `Would stop the gh-ledger daemon.`
3. **Windows variable.** Only when `windowsUserVariableSet` is true:
   - If the current user value is `true` (case-insensitive):
     - With no previous value, set it to null and print `Removed COPILOT_OTEL_ENABLED from your user account.`
     - Otherwise set it back to the previous value and print `Restored COPILOT_OTEL_ENABLED=<previous> for your user account.`
     - Dry run prints `Would remove ...` or `Would restore ...` instead.
   - Otherwise print `Left COPILOT_OTEL_ENABLED unchanged because it is no longer true.`
4. **Profiles.** For each existing file in `profileFiles`:
   - Split it on `\n`. Find the first line equal to `# >>> copilot-ledger >>>` at index `s`, and the first line equal to `# <<< copilot-ledger <<<` at index `e`.
   - If both exist and `s <= e`:
     - Remove lines `s` through `e`. If `s > 0` and line `s - 1` is empty, remove it too.
     - Join the remaining lines with `\n` and write the file back.
     - Print `Removed the CopilotLedger block from <file>.` (dry run: `Would remove ...`).
   - Otherwise print `Warning: no complete CopilotLedger block in <file>; edit it by hand.`
5. **gh alias.** Only when `ghAliasSet` is true, look for `gh`:
   - If it's missing, print `Warning: GitHub CLI (gh) was not found. Remove the alias with: gh alias delete ledger`
   - Otherwise run `["alias", "delete", "ledger"]`. Exit code 0, or stderr containing `no such alias`, prints `Removed gh alias: ledger.`; anything else prints `Warning: could not remove the gh alias: <stderr>`. Dry run prints `Would remove gh alias: ledger.`
6. **Finish.** Delete `install.json` and print `CopilotLedger is uninstalled. Your ledger data is still in <DataDirectory>.` In dry-run mode, print `Dry run: no changes made.` instead and delete nothing. Return 0.

### Packaging

Add this `PropertyGroup` to `src/CopilotLedger.Cli/CopilotLedger.Cli.csproj`, after the first one:

```xml
<PropertyGroup Condition="'$(RuntimeIdentifier)' != ''">
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
</PropertyGroup>
```

Publish one binary per runtime identifier, for example `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`:

```shell
dotnet publish src/CopilotLedger.Cli/CopilotLedger.Cli.csproj -c Release -r <rid> -o artifacts/gh-ledger
```

Without a runtime identifier, the build is a normal framework-dependent one. That keeps `dotnet build` and `dotnet test` unchanged.

### CI

Replace `.github/workflows/ci.yml` with:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
  workflow_dispatch:

permissions:
  contents: read

concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true

jobs:
  build-test:
    name: build-test (${{ matrix.os }})
    strategy:
      fail-fast: false
      matrix:
        include:
          - os: ubuntu-latest
            rid: linux-x64
          - os: windows-latest
            rid: win-x64
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.x
      - run: dotnet build CopilotLedger.slnx
      - run: dotnet test CopilotLedger.slnx
      - name: Publish single-file gh-ledger
        run: dotnet publish src/CopilotLedger.Cli/CopilotLedger.Cli.csproj -c Release -r ${{ matrix.rid }} -o artifacts/gh-ledger
      - name: Smoke-test the published binary
        shell: bash
        env:
          COPILOT_LEDGER_HOME: ${{ runner.temp }}/copilot-ledger
        run: |
          ./artifacts/gh-ledger/gh-ledger --version
          ./artifacts/gh-ledger/gh-ledger rates validate
          ./artifacts/gh-ledger/gh-ledger daemon start
          ./artifacts/gh-ledger/gh-ledger status
          ./artifacts/gh-ledger/gh-ledger daemon stop
```

The job names don't change, so required checks named `build-test (ubuntu-latest)` and `build-test (windows-latest)` still match. The smoke step binds port 4318 on the CI runner. It isn't part of `dotnet test`, which stays hermetic.

### docs

- **`docs/configuration.md`:** add this row to the files table, after the `daemon.log` row:

  ```text
  | `~/.copilot-ledger/install.json` | What `gh-ledger install` changed, so `gh-ledger uninstall` can undo it |
  ```
- **`docs/cli.md`:** write it with these sections, in order, stating the facts listed for each:
  1. **What gh-ledger does.** A local OTLP receiver on `http://localhost:4318` records Copilot CLI, GitHub Copilot app, and VS Code spend in `~/.copilot-ledger/ledger.db`, with no Docker and no cloud service.
  2. **Build the binary.** The publish command and runtime identifiers above. Keep the binary in a permanent folder, because `install` points the gh alias at it.
  3. **Install.**
     - Exactly what `install` changes: `COPILOT_OTEL_ENABLED=true` in the Windows user environment or in a marked block in the shell profile, the `gh ledger` alias, the daemon start, and `install.json`. It changes nothing else, and in particular not `PATH` or other OpenTelemetry variables.
     - `--dry-run`. Restart Copilot apps and terminals afterward.
     - The warnings and what they mean.
     - On Windows, `gh ledger` needs Git for Windows' `sh.exe`; `gh-ledger` works without it.
  4. **The daemon.**
     - `daemon run`, `start`, and `stop`; `daemon.json`, `daemon.lock`, and `daemon.log`.
     - It doesn't start at login yet, so run `gh-ledger daemon start` after you sign in.
     - Copilot drops telemetry while the daemon isn't running.
  5. **Reports.**
     - `status`, `branch`, `agents`, and `top-prompts`, with every option, the `--since` values, and `--json`.
     - Tables round to cents; JSON is exact.
     - `(none)` under Agent means no agent name was reported. Copilot's default agent reports none, and built-in subagents such as `explore` report their own names. A custom agent reports the `name` in its `.agent.md` file, both when you start it with `copilot --agent` and when another agent calls it as a subagent. A custom agent without a `name` reports its file name without `.agent.md`.
  6. **Rates.** `rates init` and `rates validate`, with a link to `configuration.md`.
  7. **Uninstall.** What it reverses, and that ledger data is kept.
  8. **Privacy.** No prompt or response content, no working directories, and no remote URLs are stored. Repositories are stored as `owner/repo` slugs or local roots, and credentials are stripped.
  9. **Troubleshooting.**
     - Port 4318 in use.
     - Nothing recorded: check `gh-ledger status` and its warnings, then restart Copilot.
     - Apps launched outside a terminal, such as from the macOS Dock or a Linux desktop launcher, may not read shell profiles, so they may not see the `COPILOT_OTEL_ENABLED` that `install` put there. If `status` shows nothing recorded after you use such an app, start it from a terminal.
     - `COPILOT_LEDGER_HOME`.

### Acceptance criteria

- The commands, outputs, files, and exit codes match this section. No public types were added. `System.Diagnostics.Process` still appears only in `SystemCliPlatform.cs`. `src/` contains no `double` or `float`.
- Install and uninstall tests change only temp directories and `FakeCliPlatform`. They never touch the real user environment, shell profile, gh configuration, or processes.
- The csproj, `ci.yml`, and both docs match this section.
- `dotnet build CopilotLedger.slnx` succeeds with 0 warnings, and `dotnet test CopilotLedger.slnx` passes everything from earlier tasks plus RP1–RP10 and IN1–IN10, with 0 failed and 0 skipped.
- `dotnet publish` for your machine's runtime identifier produces one `gh-ledger` executable (plus an optional `.pdb`). Running it with `--version` prints `0.2.0`, and with `rates validate` and `COPILOT_LEDGER_HOME` set to an empty temp folder it exits 0.

### Verify

```shell
dotnet build CopilotLedger.slnx
dotnet test CopilotLedger.slnx
dotnet publish src/CopilotLedger.Cli/CopilotLedger.Cli.csproj -c Release -r <your rid> -o artifacts/gh-ledger
```

Then run `artifacts/gh-ledger/gh-ledger --version`, and `artifacts/gh-ledger/gh-ledger rates validate` with `COPILOT_LEDGER_HOME` set to a temp folder. Report the test counts and both outputs. Don't run `install` on your own machine.

## Test specifications

Shared rules for every task, which extend Sprint 1's:

- **Money.** Assertions use `decimal`. In JSON output, read money with `JsonElement.GetDecimal()`; never use `GetDouble`. Decimal equality ignores trailing zeros.
- **Public API only.** Tests use the public API, with no `InternalsVisibleTo`. CLI behavior is tested through `GhLedgerCli.InvokeAsync`.
- **Isolation.**
  - Each test that writes files gets its own `TempDirectory`, called `<root>` below. A path written `<root>/a/b` means `Path.Combine(<root>, "a", "b")`, so it uses `\` on Windows. `Path.GetTempPath()` already expands Windows 8.3 short names (verified on .NET 10: a `TMP` of `C:\PROGRA~1` gives `C:\Program Files\`), so `Path.GetFullPath` leaves these paths unchanged.
  - Nothing touches the real `~/.copilot-ledger`, `~/.copilot`, gh configuration, user environment variables, or shell profiles.
  - Nothing touches the network or sockets, starts processes, or runs `git`.
  - Receivers and daemons use `UseTestServer()`.
- **SQLite and disposal.**
  - Every test that opens SQLite keeps the database under its `TempDirectory`. That includes opening it through a store, an ingestor, a receiver, a `CliHarness` command, or an in-process daemon. `TempDirectory.Dispose()` calls `SqliteConnection.ClearAllPools()` before deleting the folder, which Windows needs. Don't delete test folders any other way, and don't add another temp-folder helper.
  - Dispose everything that uses the folder before the `TempDirectory`. Declare the `TempDirectory` first, so C# disposes it last: `using var temp = new TempDirectory();`, then `await using var harness = new CliHarness(temp.Path);` or `await using var app = OtlpReceiverApp.Build(...);`.
  - That order stops any in-process daemon (`CliHarness.DisposeAsync`) and the receiver's host before the pools are cleared, so no connection is reopened afterward.
- **Text.** Compare text after replacing `\r\n` with `\n`. Convert hex with `Convert.FromHexString`. Expected command output is shown without its final newline. Commands write whole lines, so when a case says `Out` or `Error` *is* a text, compare with that text plus `\n`.
- **Git fixtures** follow Sprint 1's resolver tests: files end with `\n`, and `SHA1 = 0123456789abcdef0123456789abcdef01234567`.

### Task 1 tests

#### Fixtures (`OtlpFixtures`)

`PGold1Hex` is this 456-byte payload, on one line. `PGold1 = Convert.FromHexString(PGold1Hex)`.

```text
0ac5030a220a200a0c736572766963652e6e616d6512100a0e6769746875622d636f70696c6f7412f5020a100a0e6769746875622e636f70696c6f7412e0020a105b8efff798038103d269b633813fc60c1208eee19b7ec3c1b1742208eee19b7ec3c1b1732a0f63686174206d61692d636f64652d32300339c07e7b7387d4e9184140211fda87d4e9184a1f0a1567656e5f61692e6f7065726174696f6e2e6e616d6512060a04636861744a240a1467656e5f61692e726571756573742e6d6f64656c120c0a0a6d61692d636f64652d324a210a1967656e5f61692e75736167652e696e7075745f746f6b656e73120418aa86014a200a1a67656e5f61692e75736167652e6f75747075745f746f6b656e73120218054a2b0a2467656e5f61692e75736167652e63616368655f726561642e696e7075745f746f6b656e73120318801c4a240a176769746875622e636f70696c6f742e6e616e6f5f616975120921000000f8b295c4414a2c0a1e67656e5f61692e726573706f6e73652e66696e6973685f726561736f6e73120a2a080a060a0473746f707a008501010100001a2768747470733a2f2f6f70656e74656c656d657472792e696f2f736368656d61732f312e34352e30
```

It is one `ExportTraceServiceRequest` with one `ResourceSpans`:

- the resource attribute `service.name = "github-copilot"`;
- one `ScopeSpans`, whose scope name is `github.copilot`;
- `schema_url` `https://opentelemetry.io/schemas/1.45.0`;
- one span:
  - trace `5b8efff798038103d269b633813fc60c`, span `eee19b7ec3c1b174`, parent `eee19b7ec3c1b173`;
  - name `chat mai-code-2`, kind 3;
  - start `1795199604707000000` (2026-11-20T18:33:24.707Z), end `1795199606429000000` (2026-11-20T18:33:26.429Z);
  - an empty `status` and flags 257;
  - these attributes, in order:
    1. `gen_ai.operation.name` = `chat`;
    2. `gen_ai.request.model` = `mai-code-2`;
    3. `gen_ai.usage.input_tokens` = 17194;
    4. `gen_ai.usage.output_tokens` = 5;
    5. `gen_ai.usage.cache_read.input_tokens` = 3584;
    6. `github.copilot.nano_aiu` = the double 690710000.0, bits `0x41C495B2F8000000`;
    7. `gen_ai.response.finish_reasons` = an array holding `"stop"`.

`PGold1Json` encodes the same request as OTLP/JSON. It uses upper-case IDs, a numeric `endTimeUnixNano`, and a numeric `intValue`, all of which are legal:

```json
{
  "resourceSpans": [
    {
      "resource": { "attributes": [ { "key": "service.name", "value": { "stringValue": "github-copilot" } } ] },
      "scopeSpans": [
        {
          "scope": { "name": "github.copilot" },
          "spans": [
            {
              "traceId": "5B8EFFF798038103D269B633813FC60C",
              "spanId": "EEE19B7EC3C1B174",
              "parentSpanId": "EEE19B7EC3C1B173",
              "name": "chat mai-code-2",
              "kind": 3,
              "startTimeUnixNano": "1795199604707000000",
              "endTimeUnixNano": 1795199606429000000,
              "attributes": [
                { "key": "gen_ai.operation.name", "value": { "stringValue": "chat" } },
                { "key": "gen_ai.request.model", "value": { "stringValue": "mai-code-2" } },
                { "key": "gen_ai.usage.input_tokens", "value": { "intValue": "17194" } },
                { "key": "gen_ai.usage.output_tokens", "value": { "intValue": 5 } },
                { "key": "gen_ai.usage.cache_read.input_tokens", "value": { "intValue": "3584" } },
                { "key": "github.copilot.nano_aiu", "value": { "doubleValue": 690710000.0 } },
                { "key": "gen_ai.response.finish_reasons", "value": { "arrayValue": { "values": [ { "stringValue": "stop" } ] } } }
              ],
              "status": {},
              "flags": 257
            }
          ]
        }
      ],
      "schemaUrl": "https://opentelemetry.io/schemas/1.45.0"
    }
  ]
}
```

`CopilotChatTraceJson` is a trimmed copy of a real Copilot CLI 1.0.112 export (live capture, run 1), with synthetic IDs:

```json
{
  "resourceSpans": [
    {
      "resource": {
        "attributes": [
          { "key": "service.name", "value": { "stringValue": "github-copilot" } },
          { "key": "service.version", "value": { "stringValue": "1.0.112" } }
        ],
        "droppedAttributesCount": 0,
        "entityRefs": []
      },
      "scopeSpans": [
        {
          "scope": { "name": "github.copilot", "version": "1.0.112", "attributes": [], "droppedAttributesCount": 0 },
          "spans": [
            {
              "traceId": "5b8efff798038103d269b633813fc60c",
              "spanId": "eee19b7ec3c1b174",
              "traceState": "",
              "parentSpanId": "eee19b7ec3c1b173",
              "flags": 257,
              "name": "chat mai-code-2",
              "kind": 3,
              "startTimeUnixNano": "1795199604707000000",
              "endTimeUnixNano": "1795199606429000000",
              "attributes": [
                { "key": "gen_ai.operation.name", "value": { "stringValue": "chat" } },
                { "key": "gen_ai.provider.name", "value": { "stringValue": "github" } },
                { "key": "gen_ai.request.model", "value": { "stringValue": "mai-code-2" } },
                { "key": "gen_ai.conversation.id", "value": { "stringValue": "0f8fad5b-d9cb-469f-a165-70867728950e" } },
                { "key": "gen_ai.response.finish_reasons", "value": { "arrayValue": { "values": [ { "stringValue": "stop" } ] } } },
                { "key": "gen_ai.usage.input_tokens", "value": { "intValue": "17194" } },
                { "key": "gen_ai.usage.output_tokens", "value": { "intValue": "5" } },
                { "key": "gen_ai.usage.cache_read.input_tokens", "value": { "intValue": "3584" } },
                { "key": "gen_ai.response.model", "value": { "stringValue": "mai-code-2" } },
                { "key": "github.copilot.nano_aiu", "value": { "doubleValue": 690710000.0 } }
              ],
              "droppedAttributesCount": 0,
              "events": [],
              "droppedEventsCount": 0,
              "links": [],
              "droppedLinksCount": 0,
              "status": { "message": "", "code": 0 }
            },
            {
              "traceId": "5b8efff798038103d269b633813fc60c",
              "spanId": "eee19b7ec3c1b173",
              "traceState": "",
              "parentSpanId": "",
              "flags": 257,
              "name": "invoke_agent",
              "kind": 1,
              "startTimeUnixNano": "1795199604679000000",
              "endTimeUnixNano": "1795199606444000000",
              "attributes": [
                { "key": "gen_ai.operation.name", "value": { "stringValue": "invoke_agent" } },
                { "key": "gen_ai.provider.name", "value": { "stringValue": "github" } },
                { "key": "gen_ai.conversation.id", "value": { "stringValue": "0f8fad5b-d9cb-469f-a165-70867728950e" } },
                { "key": "github.copilot.agent.type", "value": { "stringValue": "builtin" } },
                { "key": "gen_ai.usage.input_tokens", "value": { "intValue": "17194" } },
                { "key": "gen_ai.usage.output_tokens", "value": { "intValue": "5" } },
                { "key": "github.copilot.turn_count", "value": { "intValue": "1" } }
              ],
              "droppedAttributesCount": 0,
              "events": [],
              "droppedEventsCount": 0,
              "links": [],
              "droppedLinksCount": 0,
              "status": { "message": "", "code": 0 }
            }
          ],
          "schemaUrl": "https://opentelemetry.io/schemas/1.45.0"
        }
      ],
      "schemaUrl": ""
    }
  ]
}
```

#### Helpers

- **`OtlpProtoWriter`** (tests only) is fluent; each method returns `this`, except `ToArray()`. Its methods are `Varint(int field, ulong value)`, `Fixed64(int field, ulong value)`, `Fixed32(int field, uint value)`, `Bytes(int field, ReadOnlySpan<byte> value)`, `String(int field, string value)`, `Message(int field, OtlpProtoWriter message)`, `Raw(params byte[] bytes)`, and `ToArray()`. Each field is written as its tag varint `(field << 3) | wireType`, followed by the value. Length-delimited values are prefixed with a varint length.
- **The `PGold1` recipe**, used by PB2. `kv(key, value)` is `new OtlpProtoWriter().String(1, key).Message(2, value)`.
  - The request is `Message(1, resourceSpans)`.
  - `resourceSpans` is `.Message(1, resource)`, `.Message(2, scopeSpans)`, and `.String(3, "https://opentelemetry.io/schemas/1.45.0")`.
  - `resource` is `.Message(1, kv("service.name", .String(1, "github-copilot")))`.
  - `scopeSpans` is `.Message(1, .String(1, "github.copilot"))` and `.Message(2, span)`.
  - `span` is:
    - `.Bytes(1, trace)`, `.Bytes(2, span id)`, `.Bytes(4, parent)`;
    - `.String(5, "chat mai-code-2")`, `.Varint(6, 3)`;
    - `.Fixed64(7, 1795199604707000000)`, `.Fixed64(8, 1795199606429000000)`;
    - seven `.Message(9, kv(...))` entries for the attributes, in order. The values are:
      - `.String(1, ...)` for strings;
      - `.Varint(3, n)` for integers;
      - `.Fixed64(4, 0x41C495B2F8000000)` for the double;
      - `.Message(5, .Message(1, .String(1, "stop")))` for the array.
    - `.Message(15, new OtlpProtoWriter())` and `.Fixed32(16, 257)`.
- **`OtlpRequests`** (tests only) builds `OtlpTraceRequest` records directly:
  - `Str(key, value)`, `Int(key, value)`, `Dbl(key)` (kind `Double`), and `Emp(key)` (kind `Empty`) make attributes.
  - `Span(traceId, spanId, parentSpanId, name, startNs, endNs, params OtlpAttribute[] attributes)` makes a span.
  - `Request(IReadOnlyList<OtlpAttribute> resourceAttributes, params OtlpSpan[] spans)` makes one `ResourceSpans`.
  - `CopilotResource` is `[Str("service.name", "github-copilot")]`.

Unless a case says otherwise, a normalization span uses the `CopilotResource`, trace `5b8efff798038103d269b633813fc60c`, span `eee19b7ec3c1b174`, no parent, start `1795199604707000000`, end `1795199606429000000`, and the attribute `Str("gen_ai.operation.name", "chat")`.

#### Protobuf decoding: `OtlpProtobufDecodingTests`

| # | Input | Expected |
|---|---|---|
| PB1 | `PGold1` | One `ResourceSpans`. `ResourceAttributes` = [`service.name`: `String` `github-copilot`]. One span: `TraceId` `5b8efff798038103d269b633813fc60c`, `SpanId` `eee19b7ec3c1b174`, `ParentSpanId` `eee19b7ec3c1b173`, `Name` `chat mai-code-2`, start 1795199604707000000, end 1795199606429000000. Attributes, in order: operation `String` `chat`; request model `String` `mai-code-2`; input `Int` 17194; output `Int` 5; cache read `Int` 3584; `nano_aiu` `Double`; finish reasons `Array`. `SpanCount` is 1. |
| PB2 | The recipe above | `ToArray()` equals `PGold1` byte for byte |
| PB3 | An empty payload | 0 `ResourceSpans` |
| PB4 | (theory) `PGold1` without its last byte; `0b`; `0affffffff0f`; `08ffffffffffffffffff7f`; `00`; `0a020a`; `0a030a0105` | `OtlpDecodeException` |
| PB5 | A span with a 15-byte `trace_id` (`0x01` repeated), an 8-byte all-zero `span_id`, an 8-byte all-zero parent, and start 1 | `TraceId`, `SpanId`, and `ParentSpanId` are all null |
| PB6 | Attributes: key bytes `67 c3 28` with string `x`; `int_value` encoded as `ff ff ff ff ff ff ff ff ff 01`; `bool_value` 1; a key with no value; an empty key | Key `g\uFFFD(` with `String` `x`; `Int` −1; `Bool` true; `Empty`; the empty-key attribute is dropped (4 attributes) |
| PB7 | Field 1 of the span sent as a varint (`08 01`) plus a valid `span_id`, and an unknown request field `12 00` | No exception: `TraceId` is null, and `SpanId` is set |

#### JSON decoding: `OtlpJsonDecodingTests`

| # | Input | Expected |
|---|---|---|
| JS1 | `PGold1Json` | The same values as PB1. Share the assertion helper. |
| JS2 | `CopilotChatTraceJson` | Two spans. The first has `ParentSpanId` `eee19b7ec3c1b173` and 10 attributes; `github.copilot.nano_aiu` is `Double`, `gen_ai.response.finish_reasons` is `Array`, and `gen_ai.usage.input_tokens` is `Int` 17194. The second has `ParentSpanId` null (from `""`), start 1795199604679000000, and end 1795199606444000000. There are 2 resource attributes. |
| JS3 | (theory) `{}`; `{"resourceSpans":[]}`; `{"resourceSpans":null}`; the bytes `EF BB BF 7B 7D` | 0 `ResourceSpans` |
| JS4 | (theory) `{`; `[]`; `{"resourceSpans":{}}`; `{"resourceSpans":[1]}`; `{"resourceSpans":[{"scopeSpans":[{"spans":{}}]}]}`; `{"resourceSpans":[{"resource":"x"}]}`; `{"resourceSpans":[],}`; `{"resourceSpans":[]} // c` | `OtlpDecodeException` |
| JS5 | `{"resourceSpans":[{"scopeSpans":[{"spans":[{"trace_id":"5b8efff798038103d269b633813fc60c","span_id":"eee19b7ec3c1b174","startTimeUnixNano":"1"}]}]}]}` | One span: `TraceId` and `SpanId` are null (snake_case is ignored), and start is 1 |
| JS6 | (theory) The value object of a single attribute (table below) | The kind and value listed |
| JS7 | `startTimeUnixNano` of `"abc"`; of the number `1795199604707000000`; `endTimeUnixNano` of `"-1"` | 0; exactly 1795199604707000000 (a `double` would round it); 0 |
| JS8 | Write this in a C# raw string literal, so each `\u` stays a JSON escape: `{"resourceSpans":[{"scopeSpans":[{"spans":[{"traceId":"5b8efff798038103d269b633813fc60c","spanId":"eee19b7ec3c1b174","parentSpanId":"\udc00","\udc00":1,"name":"a\ud800","startTimeUnixNano":"1","attributes":[{"key":"k\ud800","value":{"stringValue":"x"}},{"key":"gen_ai.agent.name","value":{"stringValue":"a\ud800b"}},{"key":"gen_ai.operation.name","value":{"stringValue":"chat"}}]}]}]}]}` | No exception. One span: `TraceId` and `SpanId` set, `ParentSpanId` null, `Name` `""`, and start 1. Two attributes, in order: `gen_ai.agent.name` `Empty`, then `gen_ai.operation.name` `String` `chat`. The `k\ud800` attribute is skipped. |

JS6 cases:

| Value | Kind and value |
|---|---|
| `{"intValue":"12x"}` | `Empty` |
| `{"intValue":1.5}` | `Empty` |
| `{"intValue":true}` | `Empty` |
| `{"intValue":"-7"}` | `Int` −7 |
| `{"intValue":9223372036854775807}` | `Int` 9,223,372,036,854,775,807 |
| `{"doubleValue":"NaN"}` | `Double` |
| `{}` | `Empty` |
| `{"stringValue":"a","intValue":"1"}` | `String` `a` (the first recognized property wins) |
| `{"foo":1,"boolValue":false}` | `Bool` false |
| `{"stringValue":5}` | `Empty` |

#### Normalization: `GenAiSpanNormalizerTests`

**NZ1.** `Normalize(DecodeJson(CopilotChatTraceJson))` gives Received 2, Rejected 0, and a null `FirstRejectionReason`.

It has one inference span:

| Field | Value |
|---|---|
| `TraceId` | `5b8efff798038103d269b633813fc60c` |
| `SpanId` | `eee19b7ec3c1b174` |
| `ParentSpanId` | `eee19b7ec3c1b173` |
| `OperationName` | `chat` |
| `StartTime` | 2026-11-20T18:33:24.707Z |
| `EndTime` | 2026-11-20T18:33:26.429Z |
| `ProviderName` | `github` |
| `RequestModel`, `ResponseModel` | `mai-code-2` |
| `AgentName` | null |
| `ConversationId` | `0f8fad5b-d9cb-469f-a165-70867728950e` |
| `ServerAddress`, `ServerPort`, `WorkingDirectory` | null |
| `InputTokens` | 17194 |
| `OutputTokens` | 5 |
| `CacheReadInputTokens` | 3584 |
| `CacheWriteInputTokens` | 0 |

It has two links, in order. Every Git field is null in both.

| | `SpanId` | `ParentSpanId` | `OperationName` | `AgentName` | `ConversationId` | `StartTime` |
|---|---|---|---|---|---|---|
| Link 1 | `eee19b7ec3c1b174` | `eee19b7ec3c1b173` | `chat` | null | `0f8fad5b-d9cb-469f-a165-70867728950e` | 2026-11-20T18:33:24.707Z |
| Link 2 | `eee19b7ec3c1b173` | null | `invoke_agent` | null | `0f8fad5b-d9cb-469f-a165-70867728950e` | 2026-11-20T18:33:24.679Z |

**NZ2–NZ11:**

| # | Case | Expected |
|---|---|---|
| NZ2 | `Normalize(DecodeProtobuf(PGold1))` | One inference span: `ProviderName`, `ConversationId`, and `ResponseModel` are null; `RequestModel` is `mai-code-2`; tokens are 17194, 5, 3584, and 0. One link, with operation `chat`. |
| NZ3 | (theory) Input 1000 plus cache write 300; cache creation 200; both 300 and 200; neither | `CacheWriteInputTokens` is 300, 200, 300, and 0. `InputTokens` stays 1000. |
| NZ4 | Input 100, cache read 80, cache write 30 | `InputTokens` 210, `CacheReadInputTokens` 80, `CacheWriteInputTokens` 30 |
| NZ5 | (theory) One-span requests: trace ID null; span ID null; start 0; `Dbl("gen_ai.usage.input_tokens")`; `Int("gen_ai.usage.output_tokens", -1)`; `Str("gen_ai.usage.cache_read.input_tokens", "5")`; input 1 with cache read `long.MaxValue` and cache write 1 | Each has Received 1, Rejected 1, and no spans or links. `FirstRejectionReason`, in order: `invalid traceId`; `invalid spanId`; `missing startTimeUnixNano`; `gen_ai.usage.input_tokens is not a non-negative integer`; `gen_ai.usage.output_tokens is not a non-negative integer`; `gen_ai.usage.cache_read.input_tokens is not a non-negative integer`; `token counts overflow` |
| NZ6 | Resource `service.name` = `my-app`, with a span that has no `gen_ai.operation.name` | Received 1, Rejected 0, no links, no inference spans |
| NZ7 | Copilot resource. Span A has no operation and the name `plan`. Span B is `execute_tool` with `Dbl("gen_ai.usage.input_tokens")`. | Two links (operation null, then `execute_tool`), Rejected 0, no inference spans |
| NZ8 | Resource `service.name` = `copilot-chat`. Span 1 is `invoke_agent` with agent `GitHub Copilot Chat`, `github.copilot.git.repository` = `https://user:s3cr3t@github.com/contoso/webapp.git`, branch `feature/login`, and commit `0123456789ABCDEF0123456789ABCDEF01234567`. Span 2 is `invoke_agent` with legacy `copilot_chat.repo.remote_url` = `git@github.com:contoso/api.git`, `head_branch_name` = `main`, and `head_commit_hash` = `not-a-sha`. | Link 1: slug `contoso/webapp`, branch `feature/login`, commit `0123456789abcdef0123456789abcdef01234567`, agent `GitHub Copilot Chat`. Link 2: slug `contoso/api`, branch `main`, commit null. |
| NZ9 | Resource with `process.working_directory` = `Path.GetTempPath()`, and a chat span with `server.address` `127.0.0.1` and `server.port` `Int` 11434. The same with `process.working_directory` = `src/app` and `server.port` 70000. | `WorkingDirectory` = `Path.GetTempPath()`, `ServerAddress` `127.0.0.1`, `ServerPort` 11434. Then `WorkingDirectory` and `ServerPort` are null. |
| NZ10 | (a) end < start; (b) operations `text_completion` and `generate_content`; (c) `gen_ai.request.model` twice, `first` then `second`; (d) `gen_ai.agent.name` = `"   "` | (a) `EndTime == StartTime`; (b) both are inference spans; (c) `RequestModel` is `first`; (d) `AgentName` is null |
| NZ11 | (theory) One-span requests: (a) `Int("gen_ai.usage.output_tokens", 1000000001)`; (b) `Int("gen_ai.usage.input_tokens", 1000000000)`; (c) input 100, cache read 600,000,000, and cache write 500,000,000, which normalizes input to 1,100,000,100 | (a) and (c): Received 1, Rejected 1, no spans or links, and `FirstRejectionReason` `token count exceeds 1000000000`. (b): Rejected 0 and one inference span with `InputTokens` 1,000,000,000; the cap is inclusive. |

### Task 2 tests

#### Test support

- **`ManualTimeProvider : TimeProvider`** is built with `(DateTimeOffset utcNow, TimeZoneInfo? localTimeZone = null)`.
  - It overrides `GetUtcNow()`, and `LocalTimeZone`, which defaults to `TimeZoneInfo.Utc`. `Advance(TimeSpan)` moves the clock.
  - `public static readonly TimeZoneInfo UtcMinus5 = TimeZoneInfo.CreateCustomTimeZone("CopilotLedger-Test-0500", TimeSpan.FromHours(-5), "UTC-05:00 (test)", "UTC-05:00 (test)");` has a fixed offset and no daylight saving time, so it behaves the same on every OS.
- **`CopilotTraces`** builds the requests below with `OtlpRequests`.
- **`LedgerSeeds.SeedChainsAsync(SqliteLedgerStore store, string repoRoot, string apiRoot)`** records K1–K5 below.
  - It builds spans with `TestSpans.Chat(...) with { TraceId = ..., SpanId = ... }`, so each span ends 2 s after it starts.
  - It prices K4 with `JsonRateCardCalculator.Parse(RateFixtures.ByokTestRatesJson)` and the rest with `LoadEmbedded()`.
  - Git contexts:
    - `swigerb/copilot-ledger` is `new GitWorkspaceContext(repoRoot, Path.Combine(repoRoot, ".git"), <branch>, SHA1, "https://github.com/swigerb/copilot-ledger.git", "swigerb/copilot-ledger")`;
    - `contoso/api` is the same shape with `apiRoot` and `https://github.com/contoso/api.git`.
  - It returns `RecordAsync`'s count, which is 5.

**Trace B** is the subagent prompt from live capture run 2, with synthetic IDs:

- resource `CopilotResource` plus `Str("service.version", "1.0.112")`;
- trace `4bf92f3577b34da6a3ce929d0e0e4736`;
- every span carries `gen_ai.conversation.id` = `7c9e6679-7425-40de-944b-e07fc1f90ae7`;
- every chat span carries `gen_ai.provider.name` `github` and both request and response model.

| Key | Span ID | Parent | Name | Start ns | End ns | Other attributes |
|---|---|---|---|---|---|---|
| root | `00f067aa0ba902b7` | none | `invoke_agent` | 1795199708999000000 | 1795199720318000000 | operation `invoke_agent`; provider `github`; `github.copilot.agent.type` `builtin` |
| main1 | `00f067aa0ba902b8` | root | `chat mai-code-2` | 1795199709019000000 | 1795199712254000000 | operation `chat`; model `mai-code-2`; input 17194; output 99; cache read 15872 |
| task | `00f067aa0ba902b9` | root | `execute_tool task` | 1795199712265000000 | 1795199716550000000 | operation `execute_tool`; `gen_ai.tool.name` `task` |
| explore | `00f067aa0ba902ba` | task | `invoke_agent explore` | 1795199712280000000 | 1795199716548000000 | operation `invoke_agent`; provider `github`; agent `explore` |
| luna1 | `00f067aa0ba902bb` | explore | `chat gpt-6-luna` | 1795199712404000000 | 1795199714391000000 | operation `chat`; model `gpt-6-luna`; input 3665; output 43; cache write 3662 |
| tool | `00f067aa0ba902bc` | explore | `execute_tool powershell` | 1795199714402000000 | 1795199715770000000 | operation `execute_tool`; `gen_ai.tool.name` `powershell`; agent `explore` |
| luna2 | `00f067aa0ba902bd` | explore | `chat gpt-6-luna` | 1795199715775000000 | 1795199716540000000 | operation `chat`; model `gpt-6-luna`; input 3764; output 5; cache read 3662; cache write 99 |
| main2 | `00f067aa0ba902be` | root | `chat mai-code-2` | 1795199716568000000 | 1795199720284000000 | operation `chat`; model `mai-code-2`; input 17333; output 5; cache read 3584 |

Batch A is tool, luna1, luna2, explore, task, and main1, in that order. Batch B is main2, then root.

**Trace D** is a VS Code Local harness prompt:

- resource `[Str("service.name", "copilot-chat")]`;
- trace `a3ce929d0e0e47364bf92f3577b34da6`;
- batch D1 is `chat` span `c0ffee0000000002`:
  - parent `c0ffee0000000001`, start 1795200001000000000, end 1795200002000000000;
  - operation `chat`, provider `github`, request model `claude-opus-5.5`;
  - input 100000, output 10000, and `gen_ai.usage.cache_creation.input_tokens` 0;
- batch D2 is `invoke_agent` span `c0ffee0000000001`:
  - no parent, start 1795200000000000000, end 1795200010000000000;
  - agent `GitHub Copilot Chat`, conversation `vscode-session-1`;
  - `github.copilot.git.repository` = `https://user:s3cr3t@github.com/contoso/webapp.git`, `github.copilot.git.branch` = `feature/login`, and `github.copilot.git.commit_sha` = `0123456789ABCDEF0123456789ABCDEF01234567`.

**Trace E** is one Copilot CLI chat:

- `CopilotResource`, trace `e4c5e6a7b8c9d0e1f2a3b4c5d6e7f809`, span `e4c5e6a7b8c9d0e1`, no parent, start 1795200100000000000, end 1795200102000000000;
- operation `chat`, provider `github`, response model `claude-opus-5.5`, conversation `d9b2d63d-a233-4123-847a-5a1f3c8e6e1f`;
- input 1200000, output 50000, cache read 800000, cache write 100000.

**Trace F** is live probe run A, a custom agent at the top level, with synthetic IDs:

- resource `CopilotResource` plus `Str("service.version", "1.0.112")`;
- trace `c0de0000000000000000000000000f01`;
- both spans carry `gen_ai.conversation.id` = `f1d5c3a2-9b8e-4d7c-a6f5-e4d3c2b1a0f1`;
- the chat span carries `gen_ai.provider.name` `github` and both request and response model.

| Key | Span ID | Parent | Name | Start ns | End ns | Other attributes |
|---|---|---|---|---|---|---|
| chat | `0f00000000000002` | root | `chat mai-code-2-flash` | 1795207524382000000 | 1795207526444000000 | operation `chat`; model `mai-code-2-flash`; input 4734; output 17 |
| root | `0f00000000000001` | none | `invoke_agent Ledger Probe Display` | 1795207524370000000 | 1795207526476000000 | operation `invoke_agent`; provider `github`; agent `Ledger Probe Display`; `github.copilot.agent.type` `custom` |

The request holds chat, then root.

**Trace G** is live probe run B, the default agent calling the custom agent with the `task` tool. It has the same resource and synthetic IDs:

- trace `c0de0000000000000000000000000f02`;
- every span carries `gen_ai.conversation.id` = `f2d5c3a2-9b8e-4d7c-a6f5-e4d3c2b1a0f2`;
- every chat span carries `gen_ai.provider.name` `github` and both request and response model.

| Key | Span ID | Parent | Name | Start ns | End ns | Other attributes |
|---|---|---|---|---|---|---|
| probechat | `0f00000000000015` | probe | `chat mai-code-2-flash` | 1795207544429000000 | 1795207546581000000 | operation `chat`; model `mai-code-2-flash`; input 954; output 5 |
| probe | `0f00000000000014` | task | `invoke_agent Ledger Probe Display` | 1795207544337000000 | 1795207546589000000 | operation `invoke_agent`; provider `github`; agent `Ledger Probe Display`; `github.copilot.agent.type` `custom` |
| task | `0f00000000000013` | root | `execute_tool task` | 1795207544328000000 | 1795207546595000000 | operation `execute_tool`; `gen_ai.tool.name` `task`; `gen_ai.transfer.target.name` `Ledger Probe Display` |
| main1 | `0f00000000000012` | root | `chat mai-code-2-flash` | 1795207542071000000 | 1795207544319000000 | operation `chat`; model `mai-code-2-flash`; input 5560; output 42 |
| main2 | `0f00000000000016` | root | `chat mai-code-2-flash` | 1795207546608000000 | 1795207549789000000 | operation `chat`; model `mai-code-2-flash`; input 5642; output 5 |
| root | `0f00000000000011` | none | `invoke_agent` | 1795207542057000000 | 1795207549819000000 | operation `invoke_agent`; provider `github`; `github.copilot.agent.type` `builtin` |

The request holds the spans in table order, the order Copilot sent them.

**Chain seed K1–K5.** Every span is a `chat` span. All except K4 use the embedded rates.

| Key | Trace ID | Span ID | Start (UTC) | Model and endpoint | Usage | Git (slug / branch) | Agent | Conversation | Expected |
|---|---|---|---|---|---|---|---|---|---|
| K1 | 32 × `1` | `1111111111111111` | 2026-11-21T14:00:00Z | `claude-opus-5.5` | input 1,200,000; cache read 800,000; cache write 100,000; output 50,000 | `swigerb/copilot-ledger` / `main` | `ledger-architect` | `conv-1` | GitHub, 2.86 USD, 286 credits |
| K2 | 32 × `2` | `2222222222222221` | 2026-11-21T13:00:00Z | `mai-code-2` | input 1,000,000; cache read 400,000; output 100,000 | `swigerb/copilot-ledger` / `feature/ledger` | `ledger-builder` | `conv-2` | GitHub, 0.56 USD, 56 credits |
| K3 | 32 × `2` | `2222222222222222` | 2026-11-21T13:05:00Z | `qwen3.8-27b-cuda-gpu:1` at `127.0.0.1:11434` | input 400,000; output 12,000 | `swigerb/copilot-ledger` / `feature/ledger` | `ledger-builder` | `conv-2` | Local, saved 0.23 |
| K4 | 32 × `3` | `3333333333333333` | 2026-11-18T12:00:00Z | `gpt-6.1-sol` at `contoso.openai.azure.com`, priced by `ByokTestRatesJson` | input 100,000; output 10,000 | `contoso/api` / `main` | null | null | Foundry BYOK, 0.315 USD |
| K5 | 32 × `4` | `4444444444444444` | 2026-10-01T12:00:00Z | `gpt-unknown` | input 1,000; output 100 | no Git context | `ledger-builder` | `conv-4` | GitHub, `UnknownModel`, 0 |

#### Pricing: `RatesRefreshTests` (embedded rates)

**RR1** (theory, 48 cases) checks every price that Task 2 adds against GitHub's table. Its expected values were computed from the pricing page with decimal math, not from `rates.json` ("GitHub price table"), so they catch a transcription error in `rates.json`.

- Each case prices `TestSpans.Chat(startTime: DateTimeOffset.Parse(start, CultureInfo.InvariantCulture), requestModel: model, inputTokens: input, cacheReadInputTokens: 10_000, cacheWriteInputTokens: 5_000, outputTokens: 2_000)` with `JsonRateCardCalculator.LoadEmbedded()`. Each of the tier's four prices produces its own `Breakdown` component, so a wrong price fails its own assertion.
- A model with one tier uses input 100,000. A tiered model uses its threshold, which selects `default`, and its threshold + 1, which selects `long_context`. That covers every tier and every tier boundary.
- Start times are 2026-11-20T12:00:00Z. A model with a promotional window adds one tick before the window ends and the instant it ends.
- When `tier` is null, the case expects no price for that date: `Status` `NoRateForDate`, `Channel` `GitHubAiCredits`, `ModelId` equal to the model, `RateCardModelId`, `TierName`, and `Breakdown` null, and `UsdCost` and `AiCredits` 0. Its four expected components are placeholders and aren't asserted.
- Otherwise the case expects `Status` `Priced`, `Channel` `GitHubAiCredits`, `ModelId` and `RateCardModelId` equal to the model, `TierName` equal to `tier`, the four `Breakdown` components, `AiCredits`, and a `UsdCost` equal to the sum of the four expected components.

The theory is `RR1_PublishedPrices_MatchGitHubTable(string model, string start, long input, string? tier, decimal uncachedInputUsd, decimal cacheReadUsd, decimal cacheWriteUsd, decimal outputUsd, decimal aiCredits)`. Copy the data exactly:

```csharp
// model, start, input tokens, tier (null: no price for that date), then the expected
// UncachedInputUsd, CacheReadUsd, CacheWriteUsd, OutputUsd, and AiCredits.
// Every case also has 10,000 cache-read, 5,000 cache-write, and 2,000 output tokens.
public static TheoryData<string, string, long, string?, decimal, decimal, decimal, decimal, decimal> PublishedPrices => new()
{
    { "gpt-5.3-codex", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.14875m, 0.00175m, 0.00875m, 0.028m, 18.725m },
    { "gpt-5.4-nano", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.017m, 0.0002m, 0.001m, 0.0025m, 2.07m },
    { "gpt-5.6-luna", "2026-11-20T12:00:00.0000000+00:00", 200000, "default", 0.037m, 0.0002m, 0.00125m, 0.0024m, 4.085m },
    { "gpt-5.6-luna", "2026-11-20T12:00:00.0000000+00:00", 200001, "long_context", 0.0740004m, 0.0004m, 0.0025m, 0.0036m, 8.05004m },
    { "gpt-5.6-sol", "2026-11-20T12:00:00.0000000+00:00", 272000, "default", 1.028m, 0.004m, 0.025m, 0.04m, 109.7m },
    { "gpt-5.6-sol", "2026-11-20T12:00:00.0000000+00:00", 272001, "long_context", 2.056008m, 0.008m, 0.05m, 0.06m, 217.4008m },
    { "gpt-6-luna", "2026-11-20T12:00:00.0000000+00:00", 272000, "default", 0.0257m, 0.0001m, 0.000625m, 0.001m, 2.7425m },
    { "gpt-6-luna", "2026-11-20T12:00:00.0000000+00:00", 272001, "long_context", 0.0514002m, 0.0002m, 0.00125m, 0.0015m, 5.43502m },
    { "gpt-6-sol", "2026-11-20T12:00:00.0000000+00:00", 272000, "default", 0.514m, 0.002m, 0.0125m, 0.02m, 54.85m },
    { "gpt-6-sol", "2026-11-20T12:00:00.0000000+00:00", 272001, "long_context", 1.028004m, 0.004m, 0.025m, 0.03m, 108.7004m },
    { "gpt-6.1-terra", "2026-11-20T12:00:00.0000000+00:00", 272000, "default", 0.257m, 0.0005m, 0.00625m, 0.01m, 27.375m },
    { "gpt-6.1-terra", "2026-11-20T12:00:00.0000000+00:00", 272001, "long_context", 0.514002m, 0.001m, 0.0125m, 0.015m, 54.2502m },
    { "gpt-6.2-astra", "2026-11-20T12:00:00.0000000+00:00", 272000, "default", 2.056m, 0.004m, 0.05m, 0.08m, 219.0m },
    { "gpt-6.2-astra", "2026-11-20T12:00:00.0000000+00:00", 272001, "long_context", 4.112016m, 0.008m, 0.10m, 0.12m, 434.0016m },
    { "gpt-6.2-sol", "2026-11-20T12:00:00.0000000+00:00", 272000, "default", 0.514m, 0.001m, 0.0125m, 0.02m, 54.75m },
    { "gpt-6.2-sol", "2026-11-20T12:00:00.0000000+00:00", 272001, "long_context", 1.028004m, 0.002m, 0.025m, 0.03m, 108.5004m },
    { "claude-haiku-4.5", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.085m, 0.001m, 0.00625m, 0.01m, 10.225m },
    { "claude-haiku-5.5", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.0085m, 0.0001m, 0.000625m, 0.001m, 1.0225m },
    { "claude-haiku-5.5", "2026-11-20T12:00:00.0000000+00:00", 100001, "long_context", 0.0425005m, 0.0005m, 0.003125m, 0.005m, 5.11255m },
    { "claude-sonnet-4", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.255m, 0.003m, 0.01875m, 0.03m, 30.675m },
    { "claude-sonnet-4.6", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.255m, 0.003m, 0.01875m, 0.03m, 30.675m },
    { "claude-opus-4.8", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.425m, 0.005m, 0.03125m, 0.05m, 51.125m },
    { "claude-sonnet-5", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.17m, 0.002m, 0.0125m, 0.02m, 20.45m },
    { "claude-fable-5", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.85m, 0.01m, 0.0625m, 0.10m, 102.25m },
    { "claude-fable-5.1", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.85m, 0.0025m, 0.0625m, 0.10m, 101.50m },
    { "claude-fable-5.2", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.68m, 0.002m, 0.05m, 0.08m, 81.2m },
    { "gemini-3.8-flash", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.06375m, 0.00075m, 0.00375m, 0.0075m, 7.575m },
    { "gemini-3.8-flash", "2026-12-31T23:59:59.9999999+00:00", 100000, "default", 0.06375m, 0.00075m, 0.00375m, 0.0075m, 7.575m },
    { "gemini-3.8-flash", "2027-01-01T00:00:00.0000000+00:00", 100000, null, 0m, 0m, 0m, 0m, 0m },
    { "gemini-4-argon", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.17m, 0.001m, 0.01m, 0.02m, 20.1m },
    { "gemini-4-argon", "2026-11-30T23:59:59.9999999+00:00", 100000, "default", 0.17m, 0.001m, 0.01m, 0.02m, 20.1m },
    { "gemini-4-argon", "2026-12-01T00:00:00.0000000+00:00", 100000, null, 0m, 0m, 0m, 0m, 0m },
    { "gemini-4-carbon", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.17m, 0.001m, 0.01m, 0.02m, 20.1m },
    { "gemini-4-carbon", "2026-11-30T23:59:59.9999999+00:00", 100000, "default", 0.17m, 0.001m, 0.01m, 0.02m, 20.1m },
    { "gemini-4-carbon", "2026-12-01T00:00:00.0000000+00:00", 100000, "default", 0.34m, 0.002m, 0.02m, 0.04m, 40.2m },
    { "gemini-4-hydrogen", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.1275m, 0.00075m, 0.0075m, 0.015m, 15.075m },
    { "gemini-4-neon", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.255m, 0.0015m, 0.015m, 0.03m, 30.15m },
    { "mai-code-1.1-flash", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.017m, 0.0002m, 0.001m, 0.0024m, 2.06m },
    { "mai-code-2-flash", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.00425m, 0.00005m, 0.00025m, 0.0005m, 0.505m },
    { "mai-code-2-flash", "2027-05-16T23:59:59.9999999+00:00", 100000, "default", 0.00425m, 0.00005m, 0.00025m, 0.0005m, 0.505m },
    { "mai-code-2-flash", "2027-05-17T00:00:00.0000000+00:00", 100000, "default", 0.0085m, 0.0001m, 0.0005m, 0.001m, 1.01m },
    { "grok-4.5", "2026-11-20T12:00:00.0000000+00:00", 200000, "default", 0.37m, 0.005m, 0.01m, 0.012m, 39.7m },
    { "grok-4.5", "2026-11-20T12:00:00.0000000+00:00", 200001, "long_context", 0.740004m, 0.01m, 0.02m, 0.024m, 79.4004m },
    { "grok-4.6", "2026-11-20T12:00:00.0000000+00:00", 200000, "default", 0.37m, 0.005m, 0.01m, 0.012m, 39.7m },
    { "grok-4.6", "2026-11-20T12:00:00.0000000+00:00", 200001, "long_context", 0.740004m, 0.01m, 0.02m, 0.024m, 79.4004m },
    { "grok-4.7", "2026-11-20T12:00:00.0000000+00:00", 200000, "default", 0.37m, 0.005m, 0.01m, 0.012m, 39.7m },
    { "grok-4.7", "2026-11-20T12:00:00.0000000+00:00", 200001, "long_context", 0.740004m, 0.01m, 0.02m, 0.024m, 79.4004m },
    { "kimi-k3", "2026-11-20T12:00:00.0000000+00:00", 100000, "default", 0.255m, 0.003m, 0.015m, 0.03m, 30.3m },
};
```

**RR2** (theory). The two live-capture `gpt-6-luna` spans. Each is GitHub, `Priced`, with the listed tier, `UsdCost`, and `AiCredits`, and its credits equal Copilot's `nano_aiu / 10^9`.

| Model | Input | Cache read | Cache write | Output | Tier | UsdCost | AiCredits |
|---|---|---|---|---|---|---|---|
| `gpt-6-luna` | 3,665 | 0 | 3,662 | 43 | default | 0.00047955 | 0.047955 |
| `gpt-6-luna` | 3,764 | 3,662 | 99 | 5 | default | 0.000051795 | 0.0051795 |

**RR3** (theory). With provider `ollama`, `lm_studio`, `foundry_local`, or `llama_cpp`, no host, model `llama3.3:70b`, input 400,000, and output 12,000: Local, `LocalOffload`, `EstimatedCloudSavedUsd` 0.23, `ModelId` `llama3.3:70b`, and `RateCardModelId` `mai-code-2`.

**RR4.** `JsonRateCardCalculator.LoadEmbedded().RatesVersion` is `2026-11-20`.

#### Git: `CachingGitContextResolverTests`

Use a fake inner `IGitContextResolver` that counts calls, and a `ManualTimeProvider`.

- **CG1.** Two `Resolve("/r")` calls 4 s apart call the inner resolver once and return the same instance.
- **CG2.** After advancing 5 s more, the inner resolver is called again.
- **CG3.** Null results are cached: an inner resolver that returns null is called once for two calls within 5 s. Different keys (`/a` and `/b`) are cached separately.

#### Attribution: `CopilotSessionWorkspaceLocatorTests`

The Copilot home is `<root>/copilot-home`. `dir = Path.Combine(<root>, "work")` doesn't need to exist. The session ID is `d9b2d63d-a233-4123-847a-5a1f3c8e6e1f`, and the file is `<home>/session-state/<id>/workspace.yaml`.

| # | Case | Expected |
|---|---|---|
| WL1 | (theory) The file holds `id: <id>`, then one `cwd` line, then `branch: x`. The `cwd` line is `cwd: <dir>`; `cwd: <dir> # note`; `cwd: "<dir with \ written as \\ and " as \">"`; or `cwd: '<dir>'`. | `GetWorkingDirectory(id)` is `dir` |
| WL2 | (theory) No file; a file without a `cwd` line; `cwd: relative/path`; `cwd:`; an indented `  cwd: <dir>` | null |
| WL3 | (theory) The IDs `""`, `../etc`, `a/b`, `a\b`, `.hidden`, and a 129-character ID of `a`s | null |
| WL4 | Look up the plain `cwd` file and get `dir`; delete the file; look up again within 60 s, then advance 61 s and look up again | `dir`, then `dir` (cached), then null |

#### Storage: `SpanLinkStoreTests`

Each test uses `<root>/ledger.db` and calls `InitializeAsync` first. For test entries built from `TestSpans.Chat(...)`, override `TraceId`, `SpanId`, and `ParentSpanId` with `with`.

| # | Case | Expected |
|---|---|---|
| SL1 | Record one entry, then through a separate `SqliteConnection` run `DROP INDEX ix_spans_trace; DROP TABLE span_links; PRAGMA user_version = 1;`. A new store calls `InitializeAsync`. | `user_version` is 2, `span_links` exists in `sqlite_master`, and the `spans` row count is still 1 |
| SL2 | `RecordSpanLinksAsync` with 3 links, then the same 3 again | 3, then 0 |
| SL3 | Trace `0af7651916cd43dd8448eb211c80319c`. Record a `claude-sonnet-5.5` chat entry (span `b7ad6b7169203332`, parent `b7ad6b7169203331`, input 10,000, output 2,000, no agent or conversation) and its link, then back-fill. Then add the `invoke_agent` link (`b7ad6b7169203331`, agent `ledger-builder`, conversation `conv-c`), back-fill, and back-fill again. | The back-fills return 0, 1, and 0. The Agent rollup then has one row: `ledger-builder`, 1 span, `AiCredits` 4, `AiCreditsUsd` 0.04. |
| SL4 | Trace `a3ce929d0e0e47364bf92f3577b34da6`. Entry chat1 is span `c0ffee0000000002` with no Git. Entry chat2 is span `c0ffee0000000003` with the `swigerb/copilot-ledger` / `main` context. Both are `claude-opus-5.5` with input 100,000 and output 10,000, and have parent `c0ffee0000000001`. Add links for both chats plus the `invoke_agent` link `c0ffee0000000001`: agent `GitHub Copilot Chat`, conversation `vscode-session-1`, slug `contoso/webapp`, branch `feature/login`, commit `0123456789abcdef0123456789abcdef01234567`. Then back-fill. | Returns 3 (the agent statement updates 2 rows, and the Git statement 1). The Branch rollup, in order: `contoso/webapp` / `feature/login` (60 credits), then `swigerb/copilot-ledger` / `main` (60 credits). The Agent rollup: `GitHub Copilot Chat`, 2 spans, 120 credits. A separate connection reads chat1's `head_commit_sha` as `0123456789abcdef0123456789abcdef01234567` and `repo_root` as NULL. |
| SL5 | Trace `ffffffffffffffffffffffffffffffff`. Links: X (`1111111111111111`, parent `2222222222222222`, `chat`) and Y (`2222222222222222`, parent `1111111111111111`, `invoke_agent`, agent `loop`). Record a `mai-code-2` entry for X with input 1,000, then back-fill. | Returns 1; the recursion stops on the cycle |
| SL6 | Links starting at 2026-11-01, 2026-11-10, and 2026-11-20 (00:00Z). Prune before 2026-11-10T00:00:00Z; again; then before 2026-12-01T00:00:00Z. | 1, then 0, then 2 |
| SL7 | `LedgerSeeds.SeedChainsAsync(store, <root>/repo, <root>/api)`. Then: `GetTopPromptChainsAsync(LedgerFilter.All, 10)`; with limit 2; with `Repository = "contoso/api"`; with `From = 2026-11-21T00:00:00Z`; and with limit 0 or 1001. | The four rows in the table below, in order. Then T1 and T2; T3 only; T1 and T2; `ArgumentOutOfRangeException`. |

SL7 rows:

| Rank | Trace | Start | End | Repository | Branch | Agents | Models | Spans | Unpriced | Input | Output | Cache read | Cache write | AiCredits | AiCreditsUsd | FoundryByokUsd | LocalSavedUsd | Conversation |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | T1 (32 × `1`) | 2026-11-21T14:00:00Z | 14:00:02Z | `swigerb/copilot-ledger` | `main` | [`ledger-architect`] | [`claude-opus-5.5`] | 1 | 0 | 1,200,000 | 50,000 | 800,000 | 100,000 | 286 | 2.86 | 0 | 0 | `conv-1` |
| 2 | T2 (32 × `2`) | 2026-11-21T13:00:00Z | 13:05:02Z | `swigerb/copilot-ledger` | `feature/ledger` | [`ledger-builder`] | [`mai-code-2`, `qwen3.8-27b`] | 2 | 0 | 1,400,000 | 112,000 | 400,000 | 0 | 56 | 0.56 | 0 | 0.23 | `conv-2` |
| 3 | T3 (32 × `3`) | 2026-11-18T12:00:00Z | 12:00:02Z | `contoso/api` | `main` | [] | [`gpt-6.1-sol`] | 1 | 0 | 100,000 | 10,000 | 0 | 0 | 0 | 0 | 0.315 | 0 | null |
| 4 | T4 (32 × `4`) | 2026-10-01T12:00:00Z | 12:00:02Z | `(none)` | `(none)` | [`ledger-builder`] | [`gpt-unknown`] | 1 | 1 | 1,000 | 100 | 0 | 0 | 0 | 0 | 0 | 0 | `conv-4` |

#### Ingestion: `LedgerIngestorTests`

Each test sets up:

- the store `<root>/data/ledger.db`, initialized, with `TimeProvider.System`;
- the embedded calculator;
- `new CachingGitContextResolver(new FileSystemGitContextResolver(ceilingDirectory: <root>))`;
- `new CopilotSessionWorkspaceLocator(<root>/copilot-home)`.

The Git fixture at `<root>/repo` has:

- `.git/HEAD` = `ref: refs/heads/feature/ledger`;
- `.git/refs/heads/feature/ledger` = SHA1;
- `.git/config` = `[remote "origin"]` followed by `\turl = https://github.com/swigerb/copilot-ledger.git`;
- an empty directory `<root>/repo/src`.

| # | Case | Expected |
|---|---|---|
| IG1 | Ingest `DecodeJson(CopilotChatTraceJson)`, then ingest it again | First: Received 2, Rejected 0, Inference 1, Recorded 1, and a null rejection reason. The Model rollup has one row: `mai-code-2`, `AiCredits` 0.69071, `AiCreditsUsd` 0.0069071, input 17,194, cache read 3,584, output 5. The Agent rollup has one row, `(none)`. Second: Recorded 0. |
| IG2 | Trace B batch A, then batch B | **Batch A:** Received 6, Rejected 0, Inference 3, Recorded 3. Its Agent rollup: `(none)` with 1 span and 0.13053 credits, then `explore` with 2 spans and 0.0531345 credits. **Batch B:** Received 2, Inference 1, Recorded 1. **Final Agent rollup, in order:** `(none)` (2 spans, input 34,527, output 104, cache read 19,456, cache write 0, `AiCredits` 0.82819, `AiCreditsUsd` 0.0082819), then `explore` (2 spans, input 7,429, output 48, cache read 3,662, cache write 3,761, `AiCredits` 0.0531345, `AiCreditsUsd` 0.000531345). **Final Model rollup:** `mai-code-2` (0.82819), then `gpt-6-luna` (0.0531345). The credits total 0.8813245, which is Copilot's 881,324,500 nano-AIU. |
| IG3 | (theory) (a) Trace E, with `<home>/session-state/d9b2d63d-a233-4123-847a-5a1f3c8e6e1f/workspace.yaml` containing `cwd: <root>/repo/src` and `branch: stale-branch`. (b) Trace E without its conversation ID, and with the resource attribute `process.working_directory` = `<root>/repo`. | Each Branch rollup has one row: `swigerb/copilot-ledger` / `feature/ledger`, 1 span, `AiCredits` 286, `AiCreditsUsd` 2.86 |
| IG4 | Trace D1, then D2 | After D1: Recorded 1, and the Branch rollup is `(none)` / `(none)`. After D2: Recorded 0, the Branch rollup is `contoso/webapp` / `feature/login` with 60 credits, and the Agent rollup is `GitHub Copilot Chat`. No TEXT value in any `spans` or `span_links` row contains `s3cr3t`; read every column through a separate connection. |
| IG5 | One request with two chat spans (distinct span IDs), the second with `Int("gen_ai.usage.output_tokens", -1)` | Received 2, Rejected 1, `FirstRejectionReason` `gen_ai.usage.output_tokens is not a non-negative integer`, Inference 1, Recorded 1 |
| IG6 | (theory) The IG6 calculator and spans below, in one request: (a) H, M; (b) H, M, N; (c) H9, M | (a) Received 2, Rejected 1, `FirstRejectionReason` `cost out of range`, Inference 1, Recorded 1. (b) Received 3, Rejected 2, `FirstRejectionReason` `gen_ai.usage.output_tokens is not a non-negative integer` (the normalizer's reason wins although N comes last), Inference 1, Recorded 1. For (a) and (b), the Model rollup has one row, `mai-code-2`: 1 span, `AiCredits` 0.075, `AiCreditsUsd` 0.00075. `SELECT COUNT(*) FROM span_links`, through a separate connection, is 2: the cost-rejected H keeps its link, and N has none. (c) Received 2, Rejected 0, Inference 2, Recorded 2. The Model rollup is `ig6-huge` (`AiCredits` 900,000,000, `AiCreditsUsd` 9,000,000), then `mai-code-2` (`AiCredits` 0.075). |

IG6 prices with `JsonRateCardCalculator.LoadEmbeddedWithOverlay(<root>/ig6-rates.json)` instead of the embedded calculator. The file holds:

```json
{
  "schemaVersion": 1,
  "ratesVersion": "ig6",
  "githubAiCredits": {
    "models": [
      { "id": "ig6-huge", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1000000, "outputPer1M": 1000000 } ] } ] }
    ]
  }
}
```

Its rates are exactly at Task 3's bound, so the file loads both before and after Task 3, and the cost rejection comes from the pico-USD limit, not from an out-of-range rate.

Build the request with `Request(CopilotResource, ...)`. Each span uses the Task 1 normalization defaults (trace `5b8efff798038103d269b633813fc60c`, no parent, the default start and end, and `Str("gen_ai.operation.name", "chat")`), with this span ID and these extra attributes:

| Span | Span ID | Attributes | Why |
|---|---|---|---|
| H | `0000000000000001` | `Str("gen_ai.request.model", "ig6-huge")`, `Int("gen_ai.usage.input_tokens", 10000000)` | Costs $10,000,000, which is 10^19 pico-USD, above `long.MaxValue`, so `PicoUsd.FromUsd` throws `ArgumentOutOfRangeException` |
| H9 | `0000000000000001` | `Str("gen_ai.request.model", "ig6-huge")`, `Int("gen_ai.usage.input_tokens", 9000000)` | Costs $9,000,000 (9 × 10^18 pico-USD), just inside the limit |
| M | `0000000000000002` | `Str("gen_ai.request.model", "mai-code-2")`, `Int("gen_ai.usage.input_tokens", 1000)`, `Int("gen_ai.usage.output_tokens", 100)` | Costs 1,000 × $0.50 + 100 × $2.50 per 1M = $0.00075 |
| N | `0000000000000004` | `Str("gen_ai.request.model", "mai-code-2")`, `Int("gen_ai.usage.output_tokens", -1)` | Rejected by the normalizer |

These results were computed with the merged Sprint 1 calculator, `PicoUsd`, and store. The overlay also loads unchanged in the Task 3 reference implementation.

**IG7.** Git context is best effort. Ingest span M from IG6 in a request whose resource is `CopilotResource` plus `Str("process.working_directory", <root>)`. Use the embedded calculator, and instead of the Git resolver above, a private fake `IGitContextResolver` in `LedgerIngestorTests` whose `Resolve` throws `IOException`. The fake is called once, with `<root>`. The result is Received 1, Rejected 0, Inference 1, Recorded 1. The Branch rollup has one row: `(none)` / `(none)`, 1 span, `AiCredits` 0.075.

**IG8.** The ingestor's `OverflowException` catch is defense in depth (decision 18), so it's tested with a fake. Use a private fake `ICreditCalculator` in `LedgerIngestorTests` whose `Calculate` throws `OverflowException` when the span's `RequestModel` is `overflow-model`, and otherwise returns the embedded calculator's result. Its `RatesVersion` is the embedded calculator's. Ingest one request built like IG6's, with span O (`0000000000000003`, `Str("gen_ai.request.model", "overflow-model")`, `Int("gen_ai.usage.input_tokens", 1)`) and then span M from IG6. The result is Received 2, Rejected 1, `FirstRejectionReason` `cost out of range`, Inference 1, Recorded 1. The Model rollup has one row, `mai-code-2`, with `AiCredits` 0.075. `SELECT COUNT(*) FROM span_links`, through a separate connection, is 2.

**IG9.** Custom agents, replaying live probe runs A and B ("Live capture: custom agents"). Ingest Trace F as one request, then Trace G as one request.

- Trace F: Received 2, Rejected 0, Inference 1, Recorded 1.
- Trace G: Received 6, Rejected 0, Inference 3, Recorded 3.
- The final Agent rollup, in order: `(none)` (2 spans, input 11,202, output 47, `AiCredits` 0.057185, `AiCreditsUsd` 0.00057185), then `Ledger Probe Display` (2 spans, input 5,688, output 22, `AiCredits` 0.02899, `AiCreditsUsd` 0.0002899). The top-level custom agent's chat and the custom subagent's chat both get the agent's `name`, and the default agent's two chats stay under `(none)`.
- The final Model rollup has one row: `mai-code-2-flash`, 4 spans, `AiCredits` 0.086175. That's 86,175,000 nano-AIU, the sum of the two runs' `--usage-output-file` totals (24,095,000 and 62,080,000), at `mai-code-2-flash`'s promotional price.

These results were computed with the merged Sprint 1 calculator and store, Task 2's `rates.json`, and this plan's Migration 2 and back-fill SQL.

### Task 3 tests

Task 3 changes Sprint 1's rates tests in place, so each case is written once. Every other Sprint 1 test stays unchanged.

#### Rates overlay: `RateOverlayTests`

1. Replace `InvalidOverlays` and `InvalidOverlay_ThrowsWithFilePath` with the data below and the theory `InvalidOverlay_ReportsExactMessage(string overlay, string expected)`. It writes `overlay` with the existing `WriteOverlay` helper, calls `LoadEmbeddedWithOverlay`, and asserts that the `InvalidDataException`'s `Message` equals `expected` after replacing `<file>` with the file's path (ordinal). Copy the data exactly; the first six rows are Sprint 1's overlays.

   ```csharp
   // Each expected message uses <file> for the overlay's path.
   public static TheoryData<string, string> InvalidOverlays => new()
   {
       // Sprint 1's invalid overlays, now with exact messages.
       { """{ "schemaVersion": 2 }""", "<file>: schemaVersion: must be 1" },
       { """{ "schemaVersion": 1, "foundryBYOK": { } }""", "<file>: foundryBYOK: is not a known property; did you mean foundryByok?" },
       { """{ "schemaVersion": 1, "localOffload": { "baselineModel": "gpt-missing" } }""", "<file>: localOffload.baselineModel: does not resolve to a githubAiCredits model in the merged rates: gpt-missing" },
       { """{ "schemaVersion": 1, "foundryByok": { "models": [ { "id": "gpt-6.1-sol", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1.75 } ] } ] } ] } }""", "<file>: foundryByok.models[0].windows[0].tiers[0].outputPer1M: is required" },
       { """{ }""", "<file>: schemaVersion: is required" },
       { """{ "schemaVersion": 1, "localOffload": { "models": [ { "id": "my-local", "aliases": ["qwen3.8:27b"] } ] } }""", "<file>: localOffload.models[0].aliases[0]: duplicate model identity in the merged rates: qwen3.8:27b (built-in model qwen3.8-27b)" },

       // Invalid JSON, and a root that isn't an object.
       { "{ \"schemaVersion\": 1,\n  \"foundryByok\": { \"models\": [ }\n}\n", "<file>: line 2, column 32: invalid JSON" },
       { "{ \"schemaVersion\": 1,\r\n  \"foundryByok\": { \"models\": [ }\r\n}\r\n", "<file>: line 2, column 32: invalid JSON" },
       { """{ "schemaVersion": 1, }""", "<file>: line 1, column 23: invalid JSON" },
       { "", "<file>: line 1, column 1: invalid JSON" },
       { """{ "schemaVersion": 1 } // note""", "<file>: line 1, column 24: invalid JSON" },
       { "[]", "<file>: must contain a JSON object" },

       // schemaVersion is checked first.
       { """{ "schemaVersion": null }""", "<file>: schemaVersion: is required" },
       { """{ "schemaVersion": "1" }""", "<file>: schemaVersion: must be an integer" },
       { """{ "foundryBYOK": { }, "schemaVersion": 2 }""", "<file>: schemaVersion: must be 1" },

       // Unknown and repeated properties.
       { """{ "schemaVersion": 1, "currency": "USD" }""", "<file>: currency: is not a known property" },
       { """{ "schemaVersion": 1, "foundryByok": { "models": [ { "id": "gpt-6.1-sol", "windows": [ { "tiers": [ { "name": "default", "inputPer1m": 1.75, "outputPer1M": 14 } ] } ] } ] } }""", "<file>: foundryByok.models[0].windows[0].tiers[0].inputPer1m: is not a known property; did you mean inputPer1M?" },
       { """{ "schemaVersion": 1, "localOffload": { }, "localOffload": { } }""", "<file>: localOffload: is defined more than once" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "tiers": [ { "name": "default", "name": "other", "inputPer1M": 1, "outputPer1M": 1 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[0].tiers[0].name: is defined more than once" },

       // A missing required property is reported at its own path.
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new" } ] } }""", "<file>: githubAiCredits.models[0].windows: is required" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "effectiveFrom": null } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[0].tiers: is required" },
       { """{ "schemaVersion": 1, "localOffload": { "models": [ { "aliases": [] } ] } }""", "<file>: localOffload.models[0].id: is required" },

       // Values of the wrong kind.
       { """{ "schemaVersion": 1, "githubAiCredits": [] }""", "<file>: githubAiCredits: must be an object" },
       { """{ "schemaVersion": 1, "foundryByok": { "hostSuffixes": "contoso.example" } }""", "<file>: foundryByok.hostSuffixes: must be an array" },
       { """{ "schemaVersion": 1, "foundryByok": { "providerNames": [1] } }""", "<file>: foundryByok.providerNames[0]: must be a string" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": "1.75", "outputPer1M": 1 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[0].tiers[0].inputPer1M: must be a number" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "tiers": [ { "name": "small", "maxInputTokens": 1.5, "inputPer1M": 1, "outputPer1M": 1 }, { "name": "large", "inputPer1M": 2, "outputPer1M": 2 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[0].tiers[0].maxInputTokens: must be an integer" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "effectiveFrom": "May 2027", "tiers": [ { "name": "default", "inputPer1M": 1, "outputPer1M": 1 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[0].effectiveFrom: must be a date and time, such as 2027-05-17T00:00:00Z" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": null, "windows": [] } ] } }""", "<file>: githubAiCredits.models[0].id: must be a string" },
       { """{ "schemaVersion": 1, "localOffload": { "models": [ null ] } }""", "<file>: localOffload.models[0]: must be an object" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1e30, "outputPer1M": 1 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[0].tiers[0].inputPer1M: is too large" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1, "outputPer1M": null } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[0].tiers[0].outputPer1M: must be a number" },
       { """{ "schemaVersion": 1, "localOffload": { "models": [ { "id": "\ud800" } ] } }""", "<file>: localOffload.models[0].id: is not valid text" },
       { """{ "schemaVersion": 1, "localOffload": { "\ud800": 1 } }""", "<file>: localOffload: contains a property name that is not valid text" },
       { """{ "schemaVersion": 1, "localOffload": { "models": [ { "id": "my-local", "aliases": null } ] } }""", "<file>: localOffload.models[0].aliases: must be an array" },
       { """{ "schemaVersion": 1, "notes": [null] }""", "<file>: notes[0]: must be a string" },
       { """{ "schemaVersion": 1, "ratesVersion": 5 }""", "<file>: ratesVersion: must be a string" },

       // Rule errors at paths into the overlay. Sprint 1 reported most of these at merged indexes.
       { """{ "schemaVersion": 1, "localOffload": { "models": [ { "id": " " } ] } }""", "<file>: localOffload.models[0].id: must not be blank" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1, "outputPer1M": 1.0000001 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[0].tiers[0].outputPer1M: must have no more than 6 decimal places" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "mai-code-2", "windows": [ { "effectiveFrom": "2027-05-17T00:00:00Z", "tiers": [ { "name": "default", "inputPer1M": 1, "outputPer1M": 5 } ] }, { "effectiveUntil": "2027-05-17T00:00:00Z", "tiers": [ { "name": "default", "inputPer1M": 0.5, "outputPer1M": 2.5 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[1].effectiveFrom: windows must be sorted and must not overlap" },
       { """{ "schemaVersion": 1, "foundryByok": { "hostSuffixes": [".contoso.example"] } }""", "<file>: foundryByok.hostSuffixes[0]: must be a host suffix without a leading '.', '/', or ':'" },
       { """{ "schemaVersion": 1, "localOffload": { "baselineModel": " " } }""", "<file>: localOffload.baselineModel: must not be blank" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "tiers": [ { "name": "default", "maxInputTokens": 1000, "inputPer1M": 1, "outputPer1M": 1 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[0].tiers[0].maxInputTokens: the last tier must be unbounded" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": -1, "outputPer1M": 1 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[0].tiers[0].inputPer1M: must be greater than or equal to 0" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1000000.000001, "outputPer1M": 1 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[0].tiers[0].inputPer1M: must be less than or equal to 1000000" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "aliases": ["Contoso New"], "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1, "outputPer1M": 1 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].aliases[0]: duplicate model identity: contoso-new" },

       // Checks against the merged rates.
       { """{ "schemaVersion": 1, "localOffload": { "models": [ { "id": "Qwen-3.8-27B" } ] } }""", "<file>: localOffload.models[0].id: duplicate model identity in the merged rates: qwen-3.8-27b (built-in model qwen3.8-27b)" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "claude-opus-5.5", "aliases": ["MAI Code 2"], "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 4, "outputPer1M": 20 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].aliases[0]: duplicate model identity in the merged rates: mai-code-2 (built-in model mai-code-2)" },
       { """{ "schemaVersion": 1, "foundryByok": { "models": [ { "id": "gpt-6.1-sol", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1.75, "outputPer1M": 14 } ] } ] } ] }, "githubAiCredits": { "models": [ { "id": "my-sol", "aliases": ["gpt-6.1-sol"], "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1, "outputPer1M": 1 } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].aliases[0]: duplicate model identity in the merged rates: gpt-6.1-sol (built-in model gpt-6.1-sol)" },

       // Reporting order: reading, then the file's own rules, then the merged rates; document order while reading.
       { """{ "schemaVersion": 1, "localOffload": { "additionalHosts": [" "], "providerNames": [1] } }""", "<file>: localOffload.providerNames[0]: must be a string" },
       { """{ "schemaVersion": 1, "localOffload": { "baselineModel": "gpt-missing", "additionalHosts": [" "] } }""", "<file>: localOffload.additionalHosts[0]: must not be blank" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "tiers": [ { "outputPer1M": "x", "inputPer1M": "y", "name": "default" } ] } ] } ] } }""", "<file>: githubAiCredits.models[0].windows[0].tiers[0].outputPer1M: must be a number" },
       { """{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1 } ] } ] } ], "hostSuffixes": [".bad"] } }""", "<file>: githubAiCredits.models[0].windows[0].tiers[0].outputPer1M: is required" },
   };
   ```

2. In `Overlay_DuplicateNormalizedIdsThrow`, replace both `Assert.Contains` calls with `Assert.Equal($"{path}: githubAiCredits.models[1].id: duplicate model identity: duplicate", exception.Message)`.
3. Add `Overlay_ReplacingBuiltInModelFreesItsAliases`. It loads `{ "schemaVersion": 1, "localOffload": { "models": [ { "id": "qwen3.8-27b", "aliases": ["my-qwen-build"] }, { "id": "my-local", "aliases": ["qwen3.8:27b"] } ] } }` without an error. `Calculate(TestSpans.Chat(requestModel: "qwen3.8:27b", inputTokens: 400_000, outputTokens: 12_000))` has `Status` `LocalOffload` and `ModelId` `my-local`.
4. Add `Overlay_BaselineCanNameAnOverlayModelByAlias`. It loads `{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "aliases": ["contoso-alias"], "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1, "outputPer1M": 1 } ] } ] } ] }, "localOffload": { "baselineModel": "Contoso Alias" } }`. `Calculate(TestSpans.Chat(serverAddress: "127.0.0.1", requestModel: "qwen3.8:27b", inputTokens: 400_000, outputTokens: 12_000))` has `RateCardModelId` `contoso-new` and `EstimatedCloudSavedUsd` 0.412 (400,000 × $1 + 12,000 × $1 per 1M).
5. Add `Overlay_RateAtMaximum_Loads`. It loads `{ "schemaVersion": 1, "githubAiCredits": { "models": [ { "id": "contoso-new", "windows": [ { "tiers": [ { "name": "default", "inputPer1M": 1000000, "outputPer1M": 1 } ] } ] } ] } }` without an error. `Calculate(TestSpans.Chat(requestModel: "contoso-new", inputTokens: 1))` has `Status` `Priced` and `UsdCost` 1.
6. Add `DocsExamples_MatchRealMessages`:
   1. Read `Path.Combine(AppContext.BaseDirectory, "docs", "configuration.md")` with `File.ReadAllLines`, so line endings don't matter.
   2. An example starts at a line equal to `<!-- rates-error-example -->` and ends at the next line equal to `<!-- /rates-error-example -->`. Its JSON is the lines after the first line equal to ```` ```json ```` up to the next line equal to ```` ``` ````. Its message is the single line after the next line equal to ```` ```text ````, which is followed by a ```` ``` ```` line.
   3. Assert there are exactly 3 examples.
   4. For each, write the JSON lines joined with `\n`, plus a final `\n`, to a new `TempDirectory`'s `rates.json`. Assert that `LoadEmbeddedWithOverlay` throws `InvalidDataException` whose `Message` equals the example's message with `/home/me/.copilot-ledger/rates.json` replaced by the file's path.

#### Rates loading: `RateCardLoadingTests`

1. Replace `InvalidDocuments` and `Parse_InvalidDocument_Throws` with this data and `Parse_InvalidDocument_ReportsExactMessage(string find, string replacement, string expected)`. It keeps the `Assert.Contains(find, json)` guard and asserts `Assert.Equal(expected, exception.Message)`. The last two rows are new.

   ```csharp
   public static TheoryData<string, string, string> InvalidDocuments => new()
   {
       { "\"schemaVersion\": 1,", "\"schemaVersion\": 2,", "schemaVersion: must be 1" },
       { "\"inputPer1M\": 2.00,", "\"inputPer1M\": 2.0000001,", "githubAiCredits.models[0].windows[0].tiers[0].inputPer1M: must have no more than 6 decimal places" },
       { "\"baselineModel\": \"claude-sonnet-5.5\"", "\"baselineModel\": \"gpt-missing\"", "localOffload.baselineModel: does not resolve to a githubAiCredits model: gpt-missing" },
       { "\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"currency\": \"USD\",", "currency: is not a known property" },
       { "\"maxInputTokens\": null, \"inputPer1M\": 2.00", "\"maxInputTokens\": 1000, \"inputPer1M\": 2.00", "githubAiCredits.models[0].windows[0].tiers[0].maxInputTokens: the last tier must be unbounded" },
       { "\"inputPer1M\": 2.00,", "\"inputPer1M\": -2.00,", "githubAiCredits.models[0].windows[0].tiers[0].inputPer1M: must be greater than or equal to 0" },
       { "\"ratesVersion\": \"test-fixture\",", string.Empty, "ratesVersion: is required" },
       { ", \"retrievedOn\": \"2026-11-20\" }", " }", "source.retrievedOn: is required" },
       { "\"inputPer1M\": 2.00,", "\"inputPer1M\": 1000000.000001,", "githubAiCredits.models[0].windows[0].tiers[0].inputPer1M: must be less than or equal to 1000000" },
   };
   ```

2. `Parse_MalformedJson_Throws` also asserts the message `line 49, column 1: invalid JSON`.
3. In `Parse_NullCollectionElement_ThrowsWithExactPath`, replace both assertions with `Assert.Equal("githubAiCredits.hostSuffixes[1]: must be a string", exception.Message)`.
4. In `Parse_InvalidBaseWithOverlay_DoesNotAddOverlayPrefix`, replace both assertions with `Assert.Equal("githubAiCredits.models[0].windows[0].tiers[0].inputPer1M: must be greater than or equal to 0", exception.Message)`.
5. Add `Parse_InvalidOverlay_NamesTheOverlay`: `JsonRateCardCalculator.Parse(RateFixtures.ByokTestRatesJson, """{ "schemaVersion": 2 }""")` throws `InvalidDataException` with the message `overlay: schemaVersion: must be 1`.
6. Add `Parse_RateAtMaximum_Loads`: replace every `"inputPer1M": 2.00,` in `RateFixtures.ByokTestRatesJson` with `"inputPer1M": 1000000,` (ordinal). `Parse` succeeds, and `Calculate(TestSpans.Chat(requestModel: "claude-sonnet-5.5", inputTokens: 1))` has `Status` `Priced` and `UsdCost` 1.

`InvalidOverlays` grows from 6 cases to 53 and `InvalidDocuments` from 7 to 9, and there are 6 new facts. Task 3 adds 55 test cases in all.

### Task 4 tests

#### Receiver: `OtlpReceiverTests`

Build each receiver with `OtlpReceiverApp.Build(options, ingestor, b => b.UseTestServer())`, then `StartAsync`, and use `app.GetTestClient()`.

`RecordingIngestor` (tests only) implements `ILedgerIngestor`. It records each request and returns a settable `IngestResult`; the default is all zeros. It can be set to throw.

To change the `Host` header, set `HttpRequestMessage.Headers.Host`. TestServer passes it through. To read a protobuf `Status`, take byte 0 as `0x12` and byte 1 as the length (under 128 here); the rest is UTF-8.

| # | Request | Expected |
|---|---|---|
| RC1 | `POST /v1/traces`, `application/x-protobuf`, `PGold1` | 200, `Content-Type` `application/x-protobuf`, an empty body. The ingestor got one request with `SpanCount` 1 and trace `5b8efff798038103d269b633813fc60c`. |
| RC2 | `application/json`, `CopilotChatTraceJson` | 200, `application/json`, body `{}`. The ingestor got `SpanCount` 2. |
| RC3 | (theory) gzip(`PGold1`) with `Content-Encoding: gzip`; the same with `GZIP`; JSON with `Content-Type: application/json; charset=utf-8` | 200 |
| RC4 | (theory) `text/plain`; no `Content-Type`; protobuf with `Content-Encoding: br` | 415. The first two respond in JSON with the messages `Unsupported Content-Type text/plain. Use application/x-protobuf or application/json.` and `Unsupported Content-Type (none). Use application/x-protobuf or application/json.`. The third responds in protobuf with `Unsupported Content-Encoding br. Use gzip or no encoding.`. The ingestor isn't called. |
| RC5 | `MaxRequestBodyBytes = 4096`: 8,192 zero bytes as protobuf, uncompressed; gzip of 1,048,576 zero bytes (1,051 bytes compressed) as protobuf | 413 for both. The body is exactly `12205265717565737420626f6479206578636565647320343039362062797465732e`, which is `Request body exceeds 4096 bytes.` |
| RC6 | Protobuf body `0b`; JSON body `{`; `Content-Encoding: gzip` with the body `not gzip` | 400 for each. The messages start with `Invalid OTLP protobuf payload: ` and `Invalid OTLP JSON payload: `; the third is `Invalid gzip body.` |
| RC7 | (theory) `Host` values `evil.example:4318`; `127.0.0.1:4318`; `[::1]:4318`; `LOCALHOST:4318` | 400 with `Host evil.example is not a loopback name.`, then 200, 200, and 200 |
| RC8 | The ingestor throws `InvalidOperationException` | 503, `Retry-After: 5`, and the JSON message `The ledger is unavailable; retry later.` |
| RC9 | The ingestor returns `RejectedSpans` 2 and `FirstRejectionReason` `invalid traceId`; send once as protobuf and once as JSON | Protobuf body exactly `0a270802122332207370616e2873292072656a65637465643a20696e76616c69642074726163654964`. JSON: `partialSuccess.rejectedSpans` is the string `"2"`, and `partialSuccess.errorMessage` is `2 span(s) rejected: invalid traceId`. |
| RC10 | `GET /v1/traces`; `POST /v1/unknown` | 405; 404 |
| RC11 | `POST /v1/metrics` as protobuf (10 bytes); `POST /v1/logs` as JSON `{"resourceLogs":[]}` | 200 with an empty protobuf body; 200 with `{}`. The ingestor isn't called. |
| RC12 | The real `LedgerIngestor` set up as in IG1: post `CopilotChatTraceJson`, then `PGold1`, which repeats the same trace and span IDs | 200 and 200. The Model rollup has one row: `mai-code-2`, `AiCredits` 0.69071, 1 span. The duplicate isn't stored twice. |

#### Daemon: `DaemonCommandTests` (with `CliHarness`)

| # | Case | Expected |
|---|---|---|
| DM1 | Start an in-process daemon, then run `daemon start`, a second `daemon run`, `daemon stop`, and `daemon stop` again | `daemon.json` has `processId` = `Environment.ProcessId`, `endpoint` `http://localhost:4318`, `version` `0.2.0`, and `databasePath` `<root>/data/ledger.db`. `daemon start` exits 0 with `gh-ledger daemon is already running (pid <pid>).`. The second `daemon run` exits 1, and its output contains `gh-ledger daemon is already running.`. `daemon stop` exits 0 with `gh-ledger daemon stopped.`; the background run returns 0, and neither `daemon.json` nor `daemon.stop` exists. The last `daemon stop` exits 0 with `gh-ledger daemon is not running.`. |
| DM2 | `daemon start`, with the harness's in-process `StartDetachedHandler` | Exit 0: `gh-ledger daemon started (pid <Environment.ProcessId>) on http://localhost:4318.`. `DetachedStarts` has one call: `/opt/gh-ledger/gh-ledger`, with the arguments `daemon run --port 4318 --log-file <root>/data/daemon.log` and working directory `<root>/data`. |
| DM3 | `daemon start` where `StartDetachedHandler` returns 999999, which isn't running | Exit 1; `Error` is `gh-ledger daemon did not start; see <root>/data/daemon.log.` |
| DM4 | (theory) `ProcessPath` of `/usr/bin/dotnet`; `C:\tools\GH-LEDGER.EXE`; `/opt/bin/gh-ledger` | Exit 1 with `Run this command from the gh-ledger executable (current process: /usr/bin/dotnet).`, then exit 0, then exit 0 |
| DM5 | Create `<root>/data` and write the malformed overlay from RT3 (b) to `<root>/data/rates.json`. Run `daemon run`, then `daemon stop`. | `daemon run` exits 1. Its `Out` contains `Invalid rates overlay: <root>/data/rates.json: line 2, column 32: invalid JSON` and doesn't contain `gh-ledger daemon stopped`. `daemon.json` doesn't exist. `daemon stop` exits 0 with `gh-ledger daemon is not running.`, which shows the lock was released. |

#### Rates: `RatesCommandTests` (with `CliHarness`)

| # | Case | Expected |
|---|---|---|
| RT1 | `rates init`, then `rates init` again, then `rates init --force` | Exit 0. The file equals the scaffold byte for byte, and the output is `Created <path>. Add your prices, then run: gh-ledger rates validate`. The second run exits 1 with `<path> already exists. Use --force to replace it.`, and the file is unchanged. `--force` exits 0. |
| RT2 | `rates validate` with no file, then after `rates init` | `No rates overlay at <path>; using the built-in rates (2026-11-20).`; then `<path> is valid. Rates version: 2026-11-20+user.` Both exit 0. |
| RT3 | (theory) `rates validate --file <root>/bad.json`, where `bad.json` holds (C# string escapes, so `\n` is a newline): (a) `{ "schemaVersion": 2 }`; (b) `{ "schemaVersion": 1,\n  "foundryByok": { "models": [ }\n}\n`; (c) `{ "schemaVersion": 1, "foundryBYOK": { } }`. Then (d) case (a)'s file with the relative `--file bad.json`, run with `RunInAsync(<root>, ...)`; and (e) `rates validate --file <root>/missing.json`. | Each exits 1 with an empty `Out`. `Error` is: (a) and (d) `Invalid rates overlay: <root>/bad.json: schemaVersion: must be 1`; (b) `Invalid rates overlay: <root>/bad.json: line 2, column 32: invalid JSON`; (c) `Invalid rates overlay: <root>/bad.json: foundryBYOK: is not a known property; did you mean foundryByok?`; (e) `<root>/missing.json does not exist.` |

### Task 5 tests

#### Reports: `ReportCommandTests`

These tests use a `CliHarness`:

- `ManualTimeProvider(2026-11-21T15:00:00Z, ManualTimeProvider.UtcMinus5)`, so local now is 2026-11-21 10:00 −05:00;
- the Git fixture at `<root>/repo`, with `.git/HEAD` = `ref: refs/heads/main` and the origin config for `swigerb/copilot-ledger`;
- `LedgerSeeds.SeedChainsAsync` on a store at `<root>/data/ledger.db`, with `repoRoot = <root>/repo` and `apiRoot = <root>/api`.

Unless a case says otherwise, the daemon isn't running and no environment variables are set. In the expected text, `<DatabasePath>` is `<root>/data/ledger.db`.

**RP1.** `status` exits 0 and prints exactly:

```text
gh-ledger 0.2.0
Daemon:  not running; start it with: gh-ledger daemon start
Ledger:  <DatabasePath>
Rates:   2026-11-20
Copilot: OTel export not enabled; run: gh-ledger install

Period        Spans  Unpriced  AI credits  GitHub USD  BYOK USD  Local saved
Today             3         0      342.00       $3.42     $0.00        $0.23
Last 7 days       4         0      342.00       $3.42     $0.32        $0.23
Last 30 days      4         0      342.00       $3.42     $0.32        $0.23
```

**RP2–RP10:**

| # | Case | Expected |
|---|---|---|
| RP2 | `status --json`, with an in-process daemon running and `UserVariables["COPILOT_OTEL_ENABLED"] = "true"` (`IsWindows = true`) | `daemon.running` true, `daemon.processId` = `Environment.ProcessId`, and `daemon.endpoint` `http://localhost:4318`. `copilotOtelEnabled` true, `warnings` empty, `ratesVersion` `2026-11-20`. **today:** from 2026-11-21T05:00:00Z, spans 3, unpriced 0, `aiCredits` 342, `aiCreditsUsd` 3.42, `foundryByokUsd` 0, `localSavedUsd` 0.23. **last 7 days:** from 2026-11-15T05:00:00Z, spans 4, `foundryByokUsd` 0.315. **last 30 days:** from 2026-10-23T05:00:00Z, spans 4. |
| RP3 | (theory) `status --json` with one process variable set, per the table below | `warnings` is exactly the listed list. `copilotOtelEnabled` is false, except true for the first row. |
| RP4 | `branch`, run in `<root>/repo` | Exit 0 and exactly the text below |
| RP5 | `branch --all-repos --json` | `period` `last 30 days`, `from` 2026-10-23T05:00:00Z, `repository` null. Rows, in order: `swigerb/copilot-ledger` `main` (spans 1, `aiCredits` 286, `aiCreditsUsd` 2.86); `swigerb/copilot-ledger` `feature/ledger` (spans 2, `aiCredits` 56, `aiCreditsUsd` 0.56, `localSavedUsd` 0.23); `contoso/api` `main` (spans 1, `foundryByokUsd` 0.315). |
| RP6 | `agents --since all` | Exit 0 and exactly the text below |
| RP7 | `top-prompts --since all --limit 2` | Exit 0 and exactly the text below |
| RP8 | `top-prompts --since all --json` | 4 rows with `rank` 1–4 and the T1–T4 values from SL7. Row 2: `agents` `["ledger-builder"]`, `models` `["mai-code-2","qwen3.8-27b"]`, `startTime` 2026-11-21T13:00:00Z, `endTime` 2026-11-21T13:05:02Z, `localSavedUsd` 0.23. Row 4: `repository` `(none)`, `unpricedSpans` 1. |
| RP9 | (theory) `agents --since <v> --json` | See the list below |
| RP10 | Write the malformed overlay from RT3 (b) to `<root>/data/rates.json`, then run `status` and `status --json` | Both exit 0. The text is RP1's, except the Rates line is `Rates:   invalid overlay: <root>/data/rates.json: line 2, column 32: invalid JSON`. In the JSON, `ratesVersion` is null, `ratesError` is `<root>/data/rates.json: line 2, column 32: invalid JSON`, and `periods` matches RP2's values. |

RP3 cases:

| Process variable | Warnings |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4318/` | none |
| `OTEL_EXPORTER_OTLP_ENDPOINT=https://collector.contoso.com:4318` | `OTEL_EXPORTER_OTLP_ENDPOINT is https://collector.contoso.com:4318; Copilot exports there instead of to CopilotLedger at http://localhost:4318.` |
| `COPILOT_OTEL_FILE_EXPORTER_PATH=/tmp/x.jsonl` | `COPILOT_OTEL_FILE_EXPORTER_PATH is set; Copilot writes telemetry to that file instead of exporting it to CopilotLedger.` |
| `COPILOT_OTEL_EXPORTER_TYPE=file` | `COPILOT_OTEL_EXPORTER_TYPE is file; remove it or set it to otlp-http.` |
| `COPILOT_OTEL_EXPORTER_TYPE=OTLP-HTTP` | none |
| `OTEL_EXPORTER_OTLP_TRACES_CLIENT_KEY=/k.pem` | `OTEL_EXPORTER_OTLP_TRACES_CLIENT_KEY is set; Copilot turns off export to http:// endpoints while TLS settings are set.` |
| `OTEL_EXPORTER_OTLP_PROTOCOL=grpc` | `OTEL_EXPORTER_OTLP_PROTOCOL is grpc; CopilotLedger accepts only http/protobuf and http/json.` |
| `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` | none |

RP9 cases, for `agents --since <v> --json`:

- **Valid values**, with `period` and `from`:
  - `today` and `TODAY`: `today`, 2026-11-21T05:00:00Z;
  - `1d`: `today`, 2026-11-21T05:00:00Z;
  - `7d`: `last 7 days`, 2026-11-15T05:00:00Z;
  - `30d`: `last 30 days`, 2026-10-23T05:00:00Z;
  - `all`: `all time`, null;
  - `2026-11-01`: `since 2026-11-01`, 2026-11-01T05:00:00Z.
- **Rows for `today`:** `ledger-architect` (spans 1, `aiCredits` 286), then `ledger-builder` (spans 2, `aiCredits` 56, `localSavedUsd` 0.23).
- **Invalid values** `0d`, `3651d`, `7x`, `2026-13-01`, and `yesterday` exit 1, and `Error` contains `Invalid --since value <v>. Use today, all, <N>d (1-3650), or yyyy-MM-dd.`.
- `branch --repo a/b --all-repos` exits 1 with `Use either --repo or --all-repos, not both.`.
- `top-prompts --limit 0` exits 1 with `--limit must be from 1 to 100.`.

RP4 expected text:

```text
Branches in swigerb/copilot-ledger (last 30 days)

Branch          Spans  Unpriced  AI credits  GitHub USD  BYOK USD  Local saved
main                1         0      286.00       $2.86     $0.00        $0.00
feature/ledger      2         0       56.00       $0.56     $0.00        $0.23
```

RP6 expected text:

```text
Agents in all repositories (all time)

Agent             Spans  Unpriced  AI credits  GitHub USD  BYOK USD  Local saved
ledger-architect      1         0      286.00       $2.86     $0.00        $0.00
ledger-builder        3         1       56.00       $0.56     $0.00        $0.23
(none)                1         0        0.00       $0.00     $0.32        $0.00
```

RP7 expected text:

```text
Top 2 prompts by cost in all repositories (all time)

#  Started           Repository              Agents            Models                   Spans  AI credits  GitHub USD  BYOK USD  Local saved  Trace
1  2026-11-21 09:00  swigerb/copilot-ledger  ledger-architect  claude-opus-5.5              1      286.00       $2.86     $0.00        $0.00  11111111111111111111111111111111
2  2026-11-21 08:00  swigerb/copilot-ledger  ledger-builder    mai-code-2, qwen3.8-27b      2       56.00       $0.56     $0.00        $0.23  22222222222222222222222222222222
```

#### Install: `InstallCommandTests` (with `CliHarness` and `FakeCliPlatform`)

**Windows fixture:**

- `IsWindows = true`, `ProcessPath = C:\tools\gh-ledger.exe`;
- `PathExecutables`: `gh` → `C:\gh\gh.exe`, and `git` → `C:\git\cmd\git.exe`;
- `ExistingFiles` empty.

**Unix fixture:**

- `IsWindows = false`, `HomeDirectory = <root>/home`, `ShellPath = /bin/zsh`, `ProcessPath = /opt/bin/gh-ledger`;
- `PathExecutables`: `gh` → `/usr/bin/gh`;
- `<root>/home/.zshrc` contains `export FOO=1` with no final newline.

**IN1** (Windows fixture, fresh). `install` exits 0:

- `UserVariableWrites` is `[("COPILOT_OTEL_ENABLED", "true")]`.
- `ProcessRuns` is `[("C:\gh\gh.exe", ["alias", "set", "ledger", "'C:/tools/gh-ledger.exe' \"$@\"", "--shell", "--clobber"])]`.
- `Out` is exactly these lines, where `<pid>` is `Environment.ProcessId`:

  ```text
  Set COPILOT_OTEL_ENABLED=true for your user account.
  Set gh alias: gh ledger runs C:\tools\gh-ledger.exe.
  Warning: GitHub CLI needs sh.exe from Git for Windows to run shell aliases, so gh ledger won't work until Git for Windows is installed. Run gh-ledger directly.
  gh-ledger daemon started (pid <pid>) on http://localhost:4318.
  CopilotLedger is installed. Restart the GitHub Copilot app, VS Code, and open terminals so they pick up COPILOT_OTEL_ENABLED.
  ```

- `install.json` has `executablePath` `C:\tools\gh-ledger.exe`, `windowsUserVariableSet` true, `windowsUserVariablePreviousValue` null, `profileFiles` empty, and `ghAliasSet` true.

**IN2–IN10:**

| # | Case | Expected |
|---|---|---|
| IN2 | IN1 with `ExistingFiles` = `C:\git\cmd\..\bin\sh.exe` | The same, without the `sh.exe` warning line |
| IN3 | IN1, then `install` again | `UserVariableWrites` still has 1 entry. Line 1 is `COPILOT_OTEL_ENABLED=true is already set for your user account.`, and the daemon line is `gh-ledger daemon is already running (pid <pid>).`. The manifest still has `windowsUserVariableSet` true and previous value null. |
| IN4 | Windows fixture with `UserVariables["COPILOT_OTEL_ENABLED"] = "false"`: `install`, then `uninstall` | `install` writes `true`, and the manifest's previous value is `false`. `uninstall`'s last variable write is `("COPILOT_OTEL_ENABLED", "false")`, with the line `Restored COPILOT_OTEL_ENABLED=false for your user account.` |
| IN5 | Unix fixture: `install` twice | `.zshrc` is exactly `export FOO=1\n\n# >>> copilot-ledger >>>\nexport COPILOT_OTEL_ENABLED=true\n# <<< copilot-ledger <<<\n`. The alias expansion argument is `'/opt/bin/gh-ledger' "$@"`. There are no user-variable writes, and no `sh` warning. The output includes `Added COPILOT_OTEL_ENABLED=true to <root>/home/.zshrc.`, and `profileFiles` is that path. The second run leaves the file unchanged and prints `<root>/home/.zshrc already sets COPILOT_OTEL_ENABLED.` |
| IN6 | (theory) Unix fixture with (`ShellPath`, `IsMacOS`) = (`/bin/bash`, true); (`/usr/bin/bash`, false); (`/usr/bin/fish`, false); (null, false), and the profile missing | The profile is `.bash_profile`, `.bashrc`, `.profile`, and `.profile`. Each file is created holding exactly the 3-line block and a final `\n`. |
| IN7 | (theory) Windows fixture without `gh`; Windows fixture whose `RunHandler` returns `(1, "", "boom")` | `GitHub CLI (gh) was not found, so the gh ledger alias was not set. Run gh-ledger directly.`; `Warning: could not set the gh alias: boom`. Both exit 0 with `ghAliasSet` false. |
| IN8 | `ProcessPath = /usr/share/dotnet/dotnet` | Exit 1 with `Error` `Run install from the gh-ledger executable (current process: /usr/share/dotnet/dotnet).`. No writes, runs, detached starts, or `install.json`. |
| IN9 | Windows fixture: `install --dry-run` | Exit 0. No writes, runs, or detached starts, and no `install.json`. `Out` is exactly `Would set COPILOT_OTEL_ENABLED=true for your user account.`, `Would set gh alias: gh ledger runs C:\tools\gh-ledger.exe.`, the `sh.exe` warning line, `Would start the gh-ledger daemon.`, and `Dry run: no changes made.` |
| IN10 | (theory) `uninstall` after IN1; after IN5; with no manifest; after IN1 with `RunHandler` returning `(1, "", "no such alias ledger")` for `alias delete`; after IN1 with the user variable changed to `1` | See the cases below |

IN10 cases:

- **After IN1:**
  - The daemon line is `gh-ledger daemon stopped.`.
  - The last variable write is `("COPILOT_OTEL_ENABLED", null)`, with `Removed COPILOT_OTEL_ENABLED from your user account.`.
  - The last run is `("C:\gh\gh.exe", ["alias", "delete", "ledger"])`, with `Removed gh alias: ledger.`.
  - `install.json` is deleted, and the last line is `CopilotLedger is uninstalled. Your ledger data is still in <root>/data.`.
- **After IN5:** `.zshrc` is exactly `export FOO=1\n`.
- **No manifest:** `CopilotLedger is not installed; nothing to undo.` and exit 0.
- **`no such alias`:** prints `Removed gh alias: ledger.`.
- **Variable changed to `1`:** prints `Left COPILOT_OTEL_ENABLED unchanged because it is no longer true.`, with no variable write.

## Deferred to later sprints

- **Start at login.** Candidates are a Windows Task Scheduler entry, a macOS `launchd` agent, a Linux `systemd --user` unit, or a user-level Copilot CLI `sessionStart` hook that runs `gh-ledger daemon start`. Until then, users run `gh-ledger daemon start` after signing in, and Copilot drops telemetry while the daemon is down.
- **`gh ledger` on Windows without Git for Windows.** A precompiled gh extension runs without `sh`, but it needs a repository named `gh-*` (for example `swigerb/gh-ledger`) with per-platform release assets. Creating that repository is a user decision; it pairs with Sprint 4's release work.
- **Binary location.** `install` doesn't copy the binary or edit `PATH`.
- **A custom port in `install`.** It would need `OTEL_EXPORTER_OTLP_ENDPOINT`, which affects every OpenTelemetry SDK.
- **Reconciliation.** Verified on 10 spans: `github.copilot.nano_aiu / 10^9` equals the computed AI credits. Next: store each `chat` span's `nano_aiu` and report drift per model. Don't use the root `invoke_agent` span's total: in the custom-agent capture, it left out the subagent's spend. JSON carries the number's text. Protobuf carries an IEEE `double`, so this needs a design that keeps money decimal-only.
- **Rates follow-ups.** Confirm the nine derived model IDs (A5) and Claude Opus 4.8 (fast mode)'s ID from live telemetry, and add aliases or the model if they differ. Add Gemini 4 Argon's and Gemini 3.8 Flash's post-promotion prices when GitHub publishes them. Both ship with the rates update command.
- **More live captures:** a BYOK provider and a local provider (assumption A1: provider labels and `server.address`), and VS Code Local harness payloads (A3). Adjust the `rates.json` host and provider lists to match.
- **File-exporter ingestion** (`COPILOT_OTEL_FILE_EXPORTER_PATH`, JSON lines) as an offline alternative to the daemon. Its line format is undocumented, so capture it first.
- **Native AOT.** After Task 3 the rates loader has no reflection, but `daemon.json` and `install.json` still need source-generated System.Text.Json, and Microsoft.Data.Sqlite's native library still ships as a separate file.
- **Overflow-safe rollups.** SQLite's `SUM` over `INTEGER` raises an error on 64-bit overflow, and then every report fails. With the token cap, a span at built-in rates costs at most $100,000 (10^17 pico-USD), so a rollup needs 93 such spans to overflow; an overlay with extreme rates needs fewer. If that ever matters, cap per-span cost at ingestion or aggregate in `decimal`, which changes Sprint 1's Storage.
- **Sprints 3 and 4,** per the roadmap: the runaway-loop circuit breaker, the MCP server, PR receipts with the GitHub Action, and the rates update command.
