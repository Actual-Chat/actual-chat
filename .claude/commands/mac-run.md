---
allowed-tools: Bash
description: Build and start the native AppKit macOS app on this Mac. Use proactively when user asks to test, run, or deploy macOS AppKit changes.
---

# Mac Run (AppKit)

Build and launch the ActualChat (Voxt) app with the experimental AppKit backend
(maui-labs, `net11.0-macos`) on the local Mac - the default macOS backend of `b app`.
For the Mac Catalyst app use `/maccatalyst-run` (or `./b.cmd app run macos --catalyst`) instead.

## Prerequisites

- macOS host with the `macos` workload (`sudo dotnet workload install macos`)
- Xcode command line tools installed (`xcode-select --install`)

## Usage

Run `./b.cmd app run macos`, which executes `scripts/run-macos.sh`:
1. Builds the JS bundle (`npm run build:Debug`)
2. Builds the AppKit app (`net11.0-macos` target, enabled via a `TargetFrameworks` override)
3. Locates the produced `.app` bundle (`Voxt (Dev).app` for dev, `Voxt.app` for prod)
4. Terminates any previous instance and launches the app via `open -W` (LaunchServices, so TCC prompts belong to the app), with stdout/stderr forwarded to the terminal

## Command

```bash
./b.cmd app run macos
```

**Note:** The app is launched with `open -W`, so this command runs until the app is quit. The console output shows app logs in real-time; they also land in `~/Library/Logs/ActualChat.log`.

## Output

The command outputs:
- Build progress and warnings
- The `.app` path
- App launch confirmation
- Live console logs from the app

## Troubleshooting

If the command fails:
- No `.app` found: confirm the build succeeded and check `artifacts/bin/App.Maui/debug_net11.0-macos/`
- Build errors: build the target on its own with `dotnet build src/dotnet/App.Maui/ -f net11.0-macos '-p:TargetFrameworks="net11.0-macos;net11.0"'`
- Missing workload: run `sudo dotnet workload install macos`
- Codesign errors (no identity / ambiguous): Debug builds are signed with your own Apple Development cert of the Actual Chat team (`M287G8G83F`); the script picks it by hash, so make sure Xcode has issued one for you (Xcode → Settings → Accounts → Manage Certificates)
- JSException loops like "x is not a function" after a rebase: the shipped `wwwroot/dist` is stale — rerun the command so `npm run build:Debug` refreshes it
- App talks to the wrong backend: the dev build (`IsDevMaui=true`, the default) targets the dev instance; use `--prod` for prod
