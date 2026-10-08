---
allowed-tools: Bash
description: Build, deploy and start the iOS app on a connected iPhone or in the simulator. Use proactively when user asks to test, run, or deploy iOS changes.
---

# iOS Run

Build, deploy, and start the ActualChat (Voxt) iOS app on a connected device, or in the
iOS simulator with `--simulator`.

## Prerequisites

- Device: a physical iOS device, trusted and paired with this Mac, and a valid provisioning profile for the app
- Simulator: an iOS simulator runtime installed in Xcode - no pairing or profile needed
- Xcode command line tools installed (`xcode-select --install`)

## Usage

Run `./b.cmd app run ios`, which executes `scripts/run-ios.sh`:
1. Resolves the target: detects the connected device, or with `--simulator` uses the booted simulator (boots the newest available iPhone one if none is) and installs the local root CA certificate into it
2. Builds the JS bundle (`npm run build:Debug`; `npm ci` first only when the packages aren't installed)
3. Clears the shared intermediates folder `artifacts/out` if the previous build was for the other target
4. Builds the iOS app (`net11.0-ios` target, `ios-arm64` or `iossimulator-<arch>`)
5. Installs it (`devicectl` on a device, `simctl` in a simulator)
6. Launches it with the console attached

## Command

```bash
./b.cmd app run ios                # connected device
./b.cmd app run ios --simulator    # iOS simulator
```

`./b.cmd app build ios` stops after the build and `./b.cmd app install ios` after the install; neither
needs the app to start. Every other option is in `/b`.

**Note:** The command runs until the app exits, so start it in the background. The console output shows
the app's logs in real time. Stopping the command terminates the app - on a device too.

Only the Debug dev app (`chat.actual.dev.app`) can be run this way: `--release`, `--aot` and `--prod`
are rejected. A Release iOS build is the store package - `./b.cmd app pack ios`.

## Output

The command outputs:
- Device detection info, or which simulator is used
- Build progress and warnings
- Signing identity details
- Install and launch confirmation
- Live console logs from the app (prefixed with timestamps)

## Troubleshooting

If the command fails:
- Ensure the device is connected and trusted
- Check that the provisioning profile is valid in the Apple Developer portal
- Verify Xcode command line tools are installed (`xcode-select --install`)
- A full rebuild after switching between device and simulator is expected: their objects don't link into each other, so the switch clears `artifacts/out`. If a build still fails with `building for 'iOS-simulator', but linking in object file`, delete `artifacts/out` and rerun
- `Fixing corrupted library: ...` lines in a simulator build are a workaround doing its job, not an error
- The app dies about a second after launch in a fresh worktree (`invalid GOOGLE_APP_ID`): the committed `GoogleService-Info.plist.*` are placeholders - copy the real ones from another worktree and run `sh ./git-prevent-tracking.cmd`
- JSException loops like "x is not a function" after a rebase: the shipped `wwwroot/dist` is stale - rerun without `--no-web`, and run `npm ci` first if `package-lock.json` changed
