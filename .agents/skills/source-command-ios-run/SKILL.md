---
name: "source-command-ios-run"
description: "Build, deploy and start iOS app on device. Use proactively when user asks to test, run, or deploy iOS changes."
---

# source-command-ios-run

Use this skill when the user asks to run the migrated source command `ios-run`.

## Command Template

# iOS Run

Build, deploy, and start the ActualChat iOS app on a connected device.

## Prerequisites

- Physical iOS device connected via USB
- Device must be trusted and paired with this Mac
- Valid provisioning profile for the app

## Usage

Run the `./scripts/run-ios.sh` script which:
1. Detects the connected iOS device
2. Builds the iOS app (`net11.0-ios` target)
3. Deploys to the device
4. Launches the app with console output

## Command

```bash
./scripts/run-ios.sh
```

**Note:** This script runs until the app is stopped on the device. The console output shows app logs in real-time.

## Output

The script outputs:
- Build progress and warnings
- Device detection info
- Signing identity details
- App launch confirmation
- Live console logs from the app (prefixed with timestamps)

## Troubleshooting

If the script fails:
- Ensure device is connected and trusted
- Check that provisioning profile is valid in Apple Developer portal
- Verify Xcode command line tools are installed (`xcode-select --install`)
