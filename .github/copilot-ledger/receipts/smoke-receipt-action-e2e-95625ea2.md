<!-- copilot-ledger-receipt -->
### CopilotLedger receipt for `smoke/receipt-action-e2e`

- **GitHub AI Credits:** 26.50 credits ($0.27) across 2 turns: `claude-sonnet-5.5` 25.00, `mai-code-2` 1.50
- **Foundry BYOK:** none
- **Local offload:** none
- **Circuit breaker:** 0 trips • max single-prompt burn: 25.00 credits

<details>
<summary>Details and data</summary>

| Billing | Model | Turns | Unpriced | Input tokens | Output tokens | Cost | Cloud spend avoided |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| GitHub AI Credits | `claude-sonnet-5.5` | 1 | 0 | 100,000 | 5,000 | 25.00 credits ($0.25) | $0.00 |
| GitHub AI Credits | `mai-code-2` | 1 | 0 | 20,000 | 2,000 | 1.50 credits ($0.02) | $0.00 |

Repository `swigerb/gh-ledger` • 2 prompts • commit `2d7834983373` • generated 2026-11-21 15:37 UTC by gh-ledger 0.3.0

To check these totals, run `gh ledger branch --since all --json` in the repository and compare the `smoke/receipt-action-e2e` row.

```json
{
  "schemaVersion": 1,
  "repository": "swigerb/gh-ledger",
  "branch": "smoke/receipt-action-e2e",
  "commit": "2d7834983373122df5bd36e267a2c77e8916f36d",
  "generatedAt": "2026-11-21T15:37:44.2927714+00:00",
  "generator": "gh-ledger 0.3.0",
  "totals": {
    "spans": 2,
    "unpricedSpans": 0,
    "inputTokens": 120000,
    "outputTokens": 7000,
    "cacheReadInputTokens": 0,
    "cacheWriteInputTokens": 0,
    "aiCredits": 26.5,
    "aiCreditsUsd": 0.265,
    "foundryByokUsd": 0,
    "localSavedUsd": 0
  },
  "prompts": 2,
  "maxPromptCredits": 25,
  "breakerTrips": 0,
  "models": [
    {
      "channel": "github_ai_credits",
      "model": "claude-sonnet-5.5",
      "spans": 1,
      "unpricedSpans": 0,
      "inputTokens": 100000,
      "outputTokens": 5000,
      "cacheReadInputTokens": 0,
      "cacheWriteInputTokens": 0,
      "aiCredits": 25,
      "aiCreditsUsd": 0.25,
      "foundryByokUsd": 0,
      "localSavedUsd": 0
    },
    {
      "channel": "github_ai_credits",
      "model": "mai-code-2",
      "spans": 1,
      "unpricedSpans": 0,
      "inputTokens": 20000,
      "outputTokens": 2000,
      "cacheReadInputTokens": 0,
      "cacheWriteInputTokens": 0,
      "aiCredits": 1.5,
      "aiCreditsUsd": 0.015,
      "foundryByokUsd": 0,
      "localSavedUsd": 0
    }
  ]
}
```

</details>
