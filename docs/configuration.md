# Configuring CopilotLedger

> Status: specified in Sprint 1. The rate engine that reads this configuration is built in Sprint 1, Task 2.

CopilotLedger works with no configuration. This page explains how it prices usage and how to change that with one optional file.

## Where CopilotLedger keeps its files

| Path | Purpose |
|---|---|
| `~/.copilot-ledger/` | Data directory (`%USERPROFILE%\.copilot-ledger\` on Windows) |
| `~/.copilot-ledger/ledger.db` | The local SQLite ledger (WAL mode) |
| `~/.copilot-ledger/rates.json` | Optional rates overlay, which you create |

CopilotLedger never stores prompt or response content, and it strips credentials from Git remote URLs before saving them.

## How usage is priced

Each LLM request (an OpenTelemetry `chat` span) is attributed to one billing channel:

| Channel | How it's detected | What's recorded |
|---|---|---|
| GitHub AI Credits | No endpoint on the span, or an endpoint under `githubcopilot.com` | AI credits and USD from GitHub's published prices (1 AI credit = $0.01) |
| Microsoft Foundry BYOK | An endpoint under `services.ai.azure.com`, `openai.azure.com`, or `cognitiveservices.azure.com`, or provider `azure.ai.inference` | USD from **your** prices in `rates.json`, and 0 AI credits. Unpriced until you add prices. |
| Local | An endpoint of `localhost`, `127.x.x.x`, `::1`, or a host you list; or a known local model, such as Qwen3.8-27B, with no endpoint | $0.00 billed, plus estimated savings: the same tokens priced at MAI Code 2's GitHub rates |
| Unpriced | Anything else | Tokens only, with a status that explains why |

The built-in prices are GitHub's November 2026 rates for `claude-opus-5.5`, `claude-sonnet-5.5`, `gpt-6.1-sol`, `gpt-6-astra`, and `mai-code-2`.

### Pricing statuses

| Status | Meaning | Fix |
|---|---|---|
| `priced` | Priced from a rate card | — |
| `local_offload` | Local model: $0.00 billed, savings estimated | — |
| `byok_unpriced` | Foundry BYOK endpoint, but no price for the model | Add the model under `foundryByok.models` |
| `unknown_model` | No model on the request, or the model isn't in the price list | Add the model, or an alias for it, under `githubAiCredits.models` or `foundryByok.models` |
| `no_rate_for_date` | The model has no price for the request's date | Add a price window that covers the date |
| `unrecognized_endpoint` | The endpoint isn't GitHub, Foundry, or local | Add its host to the matching `hostSuffixes` or `additionalHosts` list |
| `invalid_usage` | Token counts are inconsistent, for example more cached tokens than input tokens | Report it; it usually means a telemetry exporter bug |

## Why Foundry BYOK is unpriced by default

With bring-your-own-key (BYOK), Microsoft Foundry bills you directly, not GitHub. Azure prices depend on region, deployment type, and your agreement, so CopilotLedger can't know yours.

Until you add prices, BYOK requests are still counted and attributed to repository, branch, model, and agent, with status `byok_unpriced` and $0.00 cost.

## The rates overlay: `~/.copilot-ledger/rates.json`

To change pricing, create `~/.copilot-ledger/rates.json`. CopilotLedger merges it over the built-in rates when it loads them, so you only write what you want to change.

- Every section is optional except `"schemaVersion": 1`.
- A model with a new `id` is added. A model with the same `id` as a built-in one replaces it.
- Host and provider lists are added to the built-in lists.
- `localOffload.baselineModel` replaces the built-in value.
- `ratesVersion` is an optional label. Each request in the ledger records which rates priced it, for example `2026-11-20+contoso-2026-11`, or `2026-11-20+user` if you leave the label out.
- JSON doesn't allow comments, so put your own notes in the optional `notes` array.

If the file is invalid, CopilotLedger refuses to load it and reports an error that names the file and the exact JSON path, such as `foundryByok.models[0].windows[0].tiers[0].outputPer1M`. It never silently ignores a broken file.

### Price your Foundry BYOK models

Prices are USD per 1,000,000 tokens, with at most 6 decimal places. Take them from your Azure pricing or invoice. The rates below are an example, not real Azure prices.

```json
{
  "schemaVersion": 1,
  "ratesVersion": "contoso-2026-11",
  "notes": ["Azure prices from our November 2026 invoice."],
  "foundryByok": {
    "models": [
      {
        "id": "gpt-6.1-sol",
        "aliases": ["contoso-gpt61-prod"],
        "windows": [
          { "tiers": [ { "name": "default", "inputPer1M": 1.75, "cachedInputPer1M": 0.175, "outputPer1M": 14.00 } ] }
        ]
      }
    ]
  }
}
```

- `id` is the model name, and `aliases` lists other names that mean the same model. Foundry requests often report your **deployment name** (`contoso-gpt61-prod` here), so list your deployment names as aliases.
- `cachedInputPer1M` and `cacheWritePer1M` are optional. If you leave one out, those tokens are billed at `inputPer1M`.
- Model names match case-insensitively, with spaces treated as `-`, so `Claude Opus 5.5` matches `claude-opus-5.5`.

### Use a custom Foundry endpoint host

Requests to `*.services.ai.azure.com`, `*.openai.azure.com`, and `*.cognitiveservices.azure.com` are recognized automatically. To add another host, such as a gateway in front of Foundry:

```json
{
  "schemaVersion": 1,
  "foundryByok": { "hostSuffixes": ["ai-gateway.contoso.com"] }
}
```

A suffix matches that host and any subdomain of it.

### Local models and savings

Local runtimes such as Foundry Local, Ollama, and LM Studio run models on your own GPU, so local requests cost $0.00. CopilotLedger recognizes them by their `localhost` endpoint.

**Qwen3.8-27B** is the reference local model. Its runtime names all roll up as `qwen3.8-27b`: `qwen3.8:27b`, `qwen3.8:27b-q4_K_M`, `qwen3.8-27b-cuda-gpu:1`, and `Qwen 3.8 27B`.

To show what running locally is worth, CopilotLedger prices the same tokens at **MAI Code 2**'s GitHub rates, a deliberately conservative comparison. That amount is reported as local savings and is never billed.

To add names for a local model, use an entry like this. An entry with the same `id` replaces the built-in one, so include the built-in aliases you still want.

```json
{
  "schemaVersion": 1,
  "localOffload": {
    "models": [
      { "id": "qwen3.8-27b", "aliases": ["qwen-3.8-27b", "qwen3.8:27b", "qwen3.8:27b-q4_k_m", "qwen3.8-27b-cuda-gpu:1", "my-qwen-build"] }
    ]
  }
}
```

To measure savings against a different GitHub model:

```json
{
  "schemaVersion": 1,
  "localOffload": { "baselineModel": "claude-sonnet-5.5" }
}
```

To treat a model server on another machine on your network as local:

```json
{
  "schemaVersion": 1,
  "localOffload": { "additionalHosts": ["gpu-box.lan"] }
}
```

### Add or correct a GitHub price

If GitHub adds a model or changes a price before CopilotLedger ships updated rates, add or replace the model under `githubAiCredits.models`, using the same shape as the Foundry example.

For tiered or time-limited prices:

- `maxInputTokens` is an inclusive input-token limit. Set it on every tier except the last.
- `effectiveFrom` (inclusive) and `effectiveUntil` (exclusive) bound a price window. Use UTC timestamps such as `2027-05-17T00:00:00Z`.

## Field reference

Overlay sections:

| Field | Required | Notes |
|---|---|---|
| `schemaVersion` | Yes | Must be `1` |
| `ratesVersion` | No | Label recorded with each priced request |
| `notes` | No | Strings for your own notes |
| `githubAiCredits.hostSuffixes`, `foundryByok.hostSuffixes` | No | Added to the built-in lists |
| `foundryByok.providerNames`, `localOffload.providerNames` | No | Matched against `gen_ai.provider.name` |
| `githubAiCredits.models`, `foundryByok.models` | No | Priced models (see the next table) |
| `localOffload.models` | No | Local model entries: `{ "id": "...", "aliases": ["..."] }` |
| `localOffload.baselineModel` | No | A GitHub model id or alias used to estimate savings |
| `localOffload.additionalHosts` | No | Hosts treated as local |

Priced model entries:

| Field | Required | Default |
|---|---|---|
| `id` | Yes | — |
| `aliases` | No | `[]` |
| `windows` | Yes, at least one | — |
| `windows[].effectiveFrom`, `windows[].effectiveUntil` | No | Unbounded |
| `windows[].tiers` | Yes, at least one | — |
| `tiers[].name` | Yes | — |
| `tiers[].maxInputTokens` | Only for tiered prices: on every tier except the last | Unbounded |
| `tiers[].inputPer1M`, `tiers[].outputPer1M` | Yes | — |
| `tiers[].cachedInputPer1M`, `tiers[].cacheWritePer1M` | No | Billed at `inputPer1M` |
