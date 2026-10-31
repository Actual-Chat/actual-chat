---
allowed-tools: Bash
description: Build and start the macOS (AppKit) app on this Mac. Use proactively when user asks to test, run, or deploy macOS changes.
---

# Mac Run

Build and launch the ActualChat (Voxt) macOS app on the local Mac: the AppKit app (maui-labs,
`net11.0-macos`).

## Prerequisites

- The `macos` workload (`sudo dotnet workload install macos`)
- Xcode command line tools installed (`xcode-select --install`)

## Usage

Run `./b.cmd app run mac`, which executes `scripts/run-mac.sh`:
1. Picks your Apple Development certificate of the Actual Chat team by hash, for signing
2. Enables the opt-in `net11.0-macos` TFM via a `TargetFrameworks` override
3. Builds the JS bundle (`npm run build:Debug`; `npm ci` first only when the packages aren't installed)
4. Builds the app
5. Locates the produced `.app` bundle by its exact name (`Voxt (Dev).app` for dev, `Voxt.app` with `--prod`)
6. Terminates any previous instance and launches the app via `open -W` (LaunchServices, so TCC prompts belong to the app), with stdout/stderr forwarded to the terminal

## Command

```bash
./b.cmd app run mac
```

`./b.cmd app build mac` stops after the build. There is no `app install mac`: the app runs from
`artifacts/` as-is. Every other option is in `/b`.

**Note:** The app is launched with `open -W`, so this command runs until the app is quit - start it in the background. The console output shows app logs in real-time; they also land in `~/Library/Logs/ActualChat.log`.

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
- `error: not found: .../Voxt (Dev).app`: confirm the build succeeded and check `artifacts/bin/App.Maui/debug_net11.0-macos/`
- Build errors: build the target on its own with `./b.cmd app build mac`
- Missing workload: run `sudo dotnet workload install macos`
- Codesign errors (no identity / ambiguous): Debug builds are signed with your own Apple Development cert of the Actual Chat team (`M287G8G83F`); the script picks it by hash, so make sure Xcode has issued one for you (Xcode → Settings → Accounts → Manage Certificates)
- JSException loops like "x is not a function" after a rebase: the shipped `wwwroot/dist` is stale - rerun without `--no-web`, and run `npm ci` first if `package-lock.json` changed
- App talks to the wrong backend: the dev build (`IsDevMaui=true`, the default) targets the dev instance; use `--prod` for prod
