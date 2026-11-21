<!-- copilot-ledger-receipt -->
### CopilotLedger receipt for `smoke/receipt-action-e2e`

- **GitHub AI Credits:** 29.50 credits ($0.30) across 3 turns: `claude-sonnet-5.5` 25.00, `mai-code-2` 4.50
- **Foundry BYOK:** none
- **Local offload:** none
- **Circuit breaker:** 0 trips • max single-prompt burn: 25.00 credits

<details>
<summary>Details and data</summary>

| Billing | Model | Turns | Unpriced | Input tokens | Output tokens | Cost | Cloud spend avoided |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| GitHub AI Credits | `claude-sonnet-5.5` | 1 | 0 | 100,000 | 5,000 | 25.00 credits ($0.25) | $0.00 |
| GitHub AI Credits | `mai-code-2` | 2 | 0 | 60,000 | 6,000 | 4.50 credits ($0.05) | $0.00 |

Repository `swigerb/gh-ledger` • 3 prompts • commit `28a360b1b503` • generated 2026-11-21 15:38 UTC by gh-ledger 0.3.0

To check these totals, run `gh ledger branch --since all --json` in the repository and compare the `smoke/receipt-action-e2e` row.

```json
{
  "schemaVersion": 1,
  "repository": "swigerb/gh-ledger",
  "branch": "smoke/receipt-action-e2e",
  "commit": "28a360b1b503807220f02e80c6d3af73dfec6fe5",
  "generatedAt": "2026-11-21T15:38:58.9398629+00:00",
  "generator": "gh-ledger 0.3.0",
  "totals": {
    "spans": 3,
    "unpricedSpans": 0,
    "inputTokens": 160000,
    "outputTokens": 11000,
    "cacheReadInputTokens": 0,
    "cacheWriteInputTokens": 0,
    "aiCredits": 29.5,
    "aiCreditsUsd": 0.295,
    "foundryByokUsd": 0,
    "localSavedUsd": 0
  },
  "prompts": 3,
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
      "spans": 2,
      "unpricedSpans": 0,
      "inputTokens": 60000,
      "outputTokens": 6000,
      "cacheReadInputTokens": 0,
      "cacheWriteInputTokens": 0,
      "aiCredits": 4.5,
      "aiCreditsUsd": 0.045,
      "foundryByokUsd": 0,
      "localSavedUsd": 0
    }
  ]
}
```

</details>
