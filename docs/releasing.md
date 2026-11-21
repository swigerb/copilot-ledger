# Releasing gh-ledger

Releases are GitHub Releases of this repository. They make `gh extension install swigerb/gh-ledger` work, and `gh ledger rates update` downloads their `rates.json`.

## Cut a release

1. Change the version everywhere it's written, and merge that change:
   - `<Version>` in `src/CopilotLedger.Cli/CopilotLedger.Cli.csproj`;
   - in `src/CopilotLedger.Cli/GhLedgerCli.cs`, the `AssemblyInformationalVersionAttribute` comment and the fallback after `??`;
   - the version that the smoke test in `.github/workflows/ci.yml` compares with;
   - the tests that expect it: `DaemonCommandTests.DM1` and `ReportCommandTests.StatusText`.
2. Optional: run the Release workflow on `main` from the Actions tab. A manual run builds all six binaries, smoke-tests the three that GitHub's runners can run (`win-x64`, `linux-x64`, and `osx-arm64`), and uploads the six as workflow artifacts. It never creates a release, even when it's started from a tag.
3. Tag the merged commit and push the tag:

   ```shell
   git tag v0.4.0
   git push origin v0.4.0
   ```

When a `v*` tag is pushed, the Release workflow checks that the tag equals `v` plus the project version, builds a single-file binary for each platform, smoke-tests the same three, and creates the release with these assets:

| Asset | Platform |
|---|---|
| `gh-ledger-windows-amd64.exe` | Windows x64 |
| `gh-ledger-windows-arm64.exe` | Windows Arm64 |
| `gh-ledger-linux-amd64` | Linux x64 |
| `gh-ledger-linux-arm64` | Linux Arm64 |
| `gh-ledger-darwin-amd64` | macOS x64 |
| `gh-ledger-darwin-arm64` | macOS Apple silicon |
| `rates.json` | The built-in rates, for `gh ledger rates update` |
| `gh-ledger-checksums.txt` | SHA-256 checksums of the files above |

GitHub CLI picks a release asset by the `<os>-<arch>` suffix of its name, so keep these names. A tag with a hyphen, such as `v0.5.0-rc.1`, makes a prerelease, which `gh extension install` and `rates update` ignore because they use the latest release.
