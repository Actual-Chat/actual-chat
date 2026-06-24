---
allowed-tools: Bash
description: Build, deploy and start the Mac Catalyst app locally. Use proactively when user asks to test, run, or deploy macOS changes.
---

# macOS Run

Build and run the ActualChat Mac Catalyst app locally (Debug). Mirrors `ios-run`,
but for the `net10.0-maccatalyst` target — no device needed, it runs on this Mac.

## Prerequisites

- macOS with the MAUI workload installed (`dotnet workload install maui`)
- Apple Development cert + the matching `mac.chat.actual.dev.app` provisioning
  profile installed, and this Mac registered as a device on it
  (see `docs/maccatalyst-distribution.md`)

## Usage

Run the `./run-macos.sh` script which:
1. Builds the TypeScript / wwwroot bundle (`npm run build:Debug`)
2. Builds the Mac Catalyst app (`net10.0-maccatalyst`, Debug, dev backend)
3. Quits any running instance
4. Launches the freshly built `.app`

## Command

```bash
./run-macos.sh
```

Options:
- `--prod` — build against the prod backend (`voxt.ai`, `Voxt.app`) instead of dev
- `--no-js` — skip the TypeScript/wwwroot build (faster when only C# changed)
- `--console` — run in the foreground and stream the app's stdout logs

Examples:

```bash
./run-macos.sh --no-js            # C#-only change, fast rebuild + relaunch
./run-macos.sh --console          # foreground, watch live logs
./run-macos.sh --prod             # run against the prod backend
```

## Output

- TypeScript + .NET build progress and warnings
- App quit / launch confirmation
- With `--console`: live stdout logs from the app

## Troubleshooting

If the script fails:
- Build errors land in the console; for a C#-only change re-run with `--no-js`
- Signing failures — verify the cert/profile per `docs/maccatalyst-distribution.md`
  (`security find-identity -v -p basic`)
- App launches but doesn't appear — check `~/Library/Logs/DiagnosticReports/` for a
  crash `.ips`, and view runtime logs with `log stream --predicate 'process == "ActualChat"'`
