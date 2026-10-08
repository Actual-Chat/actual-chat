---
allowed-tools: Bash
description: Build and start the macOS app on this Mac - the native AppKit app by default, the Mac Catalyst app with --catalyst. Use proactively when user asks to test, run, or deploy macOS (AppKit or Mac Catalyst) changes.
---

# Mac Run

Build and launch the ActualChat (Voxt) macOS app on the local Mac. There are two of them:

- **AppKit** (maui-labs, `net11.0-macos`) - the default macOS backend of `b app`, and what
  "mac"/"macos" without qualification means.
- **Mac Catalyst** (`net11.0-maccatalyst`) - with `--catalyst`.

## Prerequisites

- AppKit: the `macos` workload (`sudo dotnet workload install macos`)
- Mac Catalyst: the .NET MAUI workload with the `maccatalyst` target (`dotnet workload list` shows `maui`)
- Xcode command line tools installed (`xcode-select --install`)

## Usage

Run `./b.cmd app run mac`, which executes `scripts/run-mac.sh`:
1. Picks your Apple Development certificate of the Actual Chat team by hash, for signing
2. Resolves the target: AppKit (enabled via a `TargetFrameworks` override), or with `--catalyst` Mac Catalyst and its RID for the host CPU (`maccatalyst-arm64` on Apple Silicon, `maccatalyst-x64` on Intel)
3. Builds the JS bundle (`npm run build:Debug`; `npm ci` first only when the packages aren't installed)
4. Builds the app
5. Locates the produced `.app` bundle by its exact name (`Voxt (Dev).app` for dev, `Voxt.app` with `--prod`)
6. Terminates any previous instance and launches the app via `open -W` (LaunchServices, so TCC prompts belong to the app), with stdout/stderr forwarded to the terminal

## Command

```bash
./b.cmd app run mac                # AppKit
./b.cmd app run mac --catalyst     # Mac Catalyst
```

`./b.cmd app build mac` stops after the build. There is no `app install mac`: the app runs from
`artifacts/` as-is. Every other option is in `/b`.

**Note:** The app is launched with `open -W`, so this command runs until the app is quit - start it in the background. The console output shows app logs in real-time; the AppKit app's also land in `~/Library/Logs/ActualChat.log`.

Only Debug builds can be run this way: `--release` and `--aot` are rejected. A Release Mac build is
the store package - `./b.cmd app pack mac`.

## Output

The command outputs:
- The signing certificate picked (`Codesign: ...`)
- Build progress and warnings
- The `.app` path
- App launch confirmation
- Live console logs from the app

## Troubleshooting

If the command fails:
- `error: not found: .../Voxt (Dev).app`: confirm the build succeeded and check `artifacts/bin/App.Maui/debug_net11.0-macos/` (AppKit) or `debug_net11.0-maccatalyst_<RID>/` (Mac Catalyst)
- Build errors: build the target on its own with `./b.cmd app build mac`
- Missing workload: run `sudo dotnet workload install macos` (AppKit) or `dotnet workload install maui` (Mac Catalyst)
- Codesign errors (no identity / ambiguous): Debug builds are signed with your own Apple Development cert of the Actual Chat team (`M287G8G83F`); the script picks it by hash, so make sure Xcode has issued one for you (Xcode → Settings → Accounts → Manage Certificates)
- Mac Catalyst only - repeated `CoreData: XPC: Unable to connect to server` lines in the log: the Contacts store refusing the sandboxed app, whose entitlements have no contacts entry. Not a launch failure
- JSException loops like "x is not a function" after a rebase: the shipped `wwwroot/dist` is stale - rerun without `--no-web`, and run `npm ci` first if `package-lock.json` changed
- App talks to the wrong backend: the dev build (`IsDevMaui=true`, the default) targets the dev instance; use `--prod` for prod
