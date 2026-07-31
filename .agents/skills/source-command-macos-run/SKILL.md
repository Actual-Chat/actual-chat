---
name: "source-command-macos-run"
description: "Build and start the Mac Catalyst app on this Mac. Use proactively when user asks to test, run, or deploy macOS (Mac Catalyst) changes."
---

# source-command-macos-run

Use this skill when the user asks to run the migrated source command `macos-run`.

## Command Template

# macOS Run

Build and launch the ActualChat (Voxt) Mac Catalyst app on the local Mac.

## Prerequisites

- macOS host with the .NET MAUI workload and the `maccatalyst` target (`dotnet workload list` shows `maui`)
- Xcode command line tools installed (`xcode-select --install`)

## Usage

Run the `./scripts/run-macos.sh` script which:
1. Picks the Mac Catalyst RID for the host CPU (`maccatalyst-arm64` on Apple Silicon, `maccatalyst-x64` on Intel)
2. Builds the JS bundle (`npm run build:Debug`)
3. Builds the Mac Catalyst app (`net11.0-maccatalyst` target)
4. Locates the produced `.app` bundle (`Voxt (Dev).app` for dev, `Voxt.app` for prod)
5. Terminates any previous instance and launches the app binary directly so logs stream to the terminal

## Command

```bash
./scripts/run-macos.sh
```

**Note:** The app binary is run directly (not via `open`), so this script runs until the app is quit. The console output shows app logs in real-time.

## Output

The script outputs:
- Build progress and warnings
- The resolved RID and `.app` path
- App launch confirmation
- Live console logs from the app

## Troubleshooting

If the script fails:
- No `.app` found: confirm the build succeeded and check `artifacts/bin/App.Maui/debug_net11.0-maccatalyst_<RID>/`
- Build errors: build the target on its own with `dotnet build src/dotnet/App.Maui/ -f net11.0-maccatalyst`
- Missing workload: run `dotnet workload install maui` (or `maccatalyst`)
- App talks to the wrong backend: the dev build (`IsDevMaui=true`, the default) targets the dev instance; pass `-p:IsDevMaui=false` to the build for prod
