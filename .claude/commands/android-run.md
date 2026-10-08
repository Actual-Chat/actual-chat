---
allowed-tools: Bash
description: Build, deploy and start the Android app on a connected device or emulator. Use proactively when user asks to test, run, or deploy Android changes.
---

# Android Run

Build, deploy, and start the ActualChat (Voxt) Android app on a connected device or emulator.

## Prerequisites

- Android device connected via USB with USB debugging enabled and this Mac authorized, or a running emulator
- `adb` on `PATH`; `adb devices` must list exactly one target in the `device` state
- The `android` workload (`sudo dotnet workload install android`)

## Usage

Run `./b.cmd app run android`, which:
1. Builds the JS bundle (`npm run build:Debug`; `npm ci` first only when the packages aren't installed)
2. Publishes the Android app (`net11.0-android` target) into `artifacts/publish/App.Maui/debug_net11.0-android/`
3. Installs the signed APK with `adb install -r` (`chat.actual.dev.app` for dev, `chat.actual.app` for prod)
4. Launches the app with `adb shell monkey`

## Command

```bash
./b.cmd app run android
```

The two variants worth knowing; every other option is in `/b`:

```bash
./b.cmd app run android --no-web      # skip the npm build - much faster when only C# changed
./b.cmd app run android --ci          # install the newest CI-built dev APK instead of building
```

`./b.cmd app build android` stops after the build and `./b.cmd app install android` after the install.

**Note:** Unlike the iOS and Mac runs, the command returns as soon as the app is launched - it doesn't stream logs. Read them with `adb logcat`:

```bash
adb logcat --pid=$(adb shell pidof chat.actual.dev.app)
```

## Output

The command outputs:
- Each step as `$ <command>` before running it
- Build progress and warnings
- The APK path
- `adb install` and launch results

## Troubleshooting

If the command fails:
- `adb: no devices/emulators found` or `unauthorized`: reconnect the device and accept the USB debugging prompt on it
- `more than one device/emulator`: pick one with `ANDROID_SERIAL=<serial> ./b.cmd app run android` (serials: `adb devices`)
- `INSTALL_FAILED_UPDATE_INCOMPATIBLE`: the installed build is signed with another key (a CI or store build vs. a local one) - `adb uninstall <appId>` and rerun; this wipes the app's data on the device
- Missing workload: run `sudo dotnet workload install android`
- `--ci` fails to find or download the APK: `gh` must be logged in (or `GH_TOKEN` set), and CI keeps APKs for 10 days only
- JSException loops like "x is not a function" after a rebase: the shipped `wwwroot/dist` is stale - rerun without `--no-web`, and run `npm ci` first if `package-lock.json` changed
- App talks to the wrong backend: the dev build (`IsDevMaui=true`, the default) targets the dev instance; use `--prod` for prod
