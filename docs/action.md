# Posting PR receipts with the CopilotLedger action

The CopilotLedger action posts a branch's AI spend receipt on its pull request. The receipt comes from your machine: CopilotLedger's ledger is local, and the action only reads the receipt file the pull request commits. It needs no cloud service and no secrets.

## How it works

1. On your machine, check out the pull request's branch and run `gh ledger export-pr`. It writes the branch's receipt to `.github/copilot-ledger/receipts/<branch>-<hash>.md`.
2. Commit that file on the branch and push.
3. On each push, the action finds the receipt among the pull request's changed files, checks it, and posts it as one comment. Later pushes update that comment instead of adding new ones.

The receipt is Markdown with its data in a JSON block. It shows GitHub AI Credits, Foundry BYOK spend, local-model tokens with the cloud spend they avoided, circuit-breaker trips, and the most expensive prompt. Its totals equal the branch's row in `gh ledger branch --since all --json`.

Receipts stay in the repository after the pull request merges, as a record of what each branch spent.

## Add the workflow

Create `.github/workflows/copilot-ledger-receipt.yml`:

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

The action reads the receipt with `contents: read` and comments with `pull-requests: write`, using the workflow's `GITHUB_TOKEN`.

## Inputs and outputs

| Input | Default | Meaning |
|---|---|---|
| `github-token` | `${{ github.token }}` | Token that reads the pull request and writes the comment |
| `receipts-directory` | `.github/copilot-ledger/receipts` | Where receipts are committed; change it only if you move them |
| `comment-author` | `github-actions[bot]` | The login that owns the receipt comment. Change it only when `github-token` belongs to another account. |

| Output | Meaning |
|---|---|
| `result` | `created`, `updated`, `unchanged`, `no-receipt`, or `skipped` |
| `comment-url` | The receipt comment's URL; empty for `no-receipt` and `skipped` |

## What the action checks

- It runs only for `pull_request` events. Pull requests from forks get a read-only token, so the action skips them with a notice.
- A receipt must be among the pull request's changed files, under the receipts directory, and end in `.md`. With none, the action leaves a notice and succeeds.
- A receipt must start with `<!-- copilot-ledger-receipt -->` (with no byte order mark before it), be at most 65,536 bytes of UTF-8, and hold a JSON block with `"schemaVersion": 1` and a `branch` exactly equal to the pull request's branch. Otherwise the action fails with an error that names the file or, when no receipt is for the pull request's branch, lists the branches of the receipts it found.
- It updates the oldest comment by `comment-author` (compared case-insensitively, as GitHub logins are) that starts with `<!-- copilot-ledger-receipt -->`. It never edits anyone else's comment.

The action is a composite action that runs PowerShell 7, which GitHub-hosted runners include.
