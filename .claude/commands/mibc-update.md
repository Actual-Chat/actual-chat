---
allowed-tools: Bash, Read, Edit
description: Re-record the startup profiles that drive partial ReadyToRun and regenerate android.mibc, android-notif.mibc, windows.mibc and merged.mibc. Takes [android|android-notif|windows|all], default all.
---

# /mibc-update — refresh the startup profiles

Maintains four files in `src/dotnet/App.Maui/_Profiling/`: `android.mibc`,
`android-notif.mibc`, `windows.mibc`, and `merged.mibc` — the union, and the only one the
builds actually compile against.

`$ARGUMENTS` selects what to re-record:

| Argument | Re-records |
|---|---|
| `android` | `android.mibc` |
| `android-notif` | `android-notif.mibc` — needs a push sent by hand, see below |
| `windows` | `windows.mibc` |
| `all` *(default, and what empty means)* | `android` + `windows` |

Anything else — stop and say what the valid values are; don't guess.

**`all` deliberately excludes `android-notif`.** That capture can't be automated end to end:
it needs someone to send the device a push at the right moment. The file is a kept artifact
that gets merged in from disk on every run, so it stays useful without being re-recorded.

`merged.mibc` is rebuilt from whichever of the three exist, whatever was selected — so a
single-platform run still produces a complete union.

Background, measured numbers and the reasoning behind every step:
[`docs/startup-profiling.md`](../../docs/startup-profiling.md).

## Before starting

Check and report these; don't assume any of them.

For `android` or `all`:
- `adb devices` shows one device in state `device` — not `offline`, not `unauthorized`.
- **The app on the device is signed in.** A signed-out recording profiles the sign-in screen
  instead of real startup and is worthless. If it isn't signed in, stop and tell the user —
  signing in needs them, and a tracing build can't even be opened by hand without a tracer
  attached (use `-Mode Attach` to give them a window if they ask).

For any platform:
- `src/dotnet/App.Maui/wwwroot/dist` exists. If not, run `npm run build:Release` first —
  without it the app starts and then dies, and the profile is garbage.

Tell the user up front what this costs: Android is **reinstalled** with a tracing build
(which suspends at launch until a tracer attaches), and the whole thing takes roughly 30-40
minutes for `all`.

## Steps

Run in order. Each `dotnet publish` takes several minutes — run it in the background and wait
for completion rather than polling.

### Android — skip unless `$ARGUMENTS` is `android` or `all`

```bash
pwsh -NoProfile -File scripts/Record-AndroidStartupProfiles.ps1 -Runs 2 -Mode Methods -Build
pwsh -NoProfile -File scripts/New-StartupMibc.ps1 -Platform Android
```

`-Build` publishes with `IsTracingEnabled=true` and installs it. Two runs is the right
number: cold starts barely vary, and each 10 s capture is ~420 MB.

### Android notification path — only when `$ARGUMENTS` is `android-notif`

A push wakes the app into a different startup than a launcher tap:
`FirebaseMessagingService.OnMessageReceived` runs in a process Android started for the
*service*, with no Activity and none of the `MauiProgram` UI path.

```bash
pwsh -NoProfile -File scripts/Record-AndroidNotificationProfile.ps1 -Warm -Duration 00:01:00
# it prints "SEND THE PUSH NOW" and waits - post from a DEV bot to the account signed in
# on the device, then tap the notification if you also want the MainActivity path
pwsh -NoProfile -File scripts/New-StartupMibc.ps1 -Platform AndroidNotification
```

The push must come from the **same environment as the build** — a prod bot cannot notify a
`chat.actual.dev.app` install. Use the `voxt-robokitty-dev` MCP for dev.

**Verify you captured the right thing.** The trace is worthless if the handler never ran:

```bash
grep -c "FirebaseMessagingService" tmp/mibc/notif-profiles/../notif-profiles-dump.txt
```

Zero means the push produced no notification, or Firebase displayed it from its own base
service without calling our override — in which case the trace is just an ordinary
background start. A capture that adds only a few dozen thread-pool and JNI methods over
`merged.mibc` is the same symptom. Say so rather than reporting success.

### Windows — skip unless `$ARGUMENTS` is `windows` or `all`

```bash
dotnet publish src/dotnet/App.Maui/App.Maui.csproj -f:net11.0-windows10.0.22621.0 -c:Release -p:WindowsPackageType=None
pwsh -NoProfile -File scripts/Record-WindowsStartupProfiles.ps1 -Runs 2 -Mode Methods
pwsh -NoProfile -File scripts/New-StartupMibc.ps1 -Platform Windows
```

No tracing build is needed here — `dotnet-trace collect -- <exe>` launches and suspends the
app itself.

`New-StartupMibc.ps1` rebuilds `merged.mibc` at the end of **every** run, so there is no
separate merge step.

### Verify before reporting

```bash
./dotnet-pgo.cmd dump -i src/dotnet/App.Maui/_Profiling/merged.mibc -o tmp/merged-dump.txt | grep "# Methods:"
sed -n 's/.*"Method": "\[\([^]]*\)\].*/\1/p' tmp/merged-dump.txt | sort | uniq -c | sort -rn | head
git diff --stat src/dotnet/App.Maui/_Profiling/
```

Sanity-check the output rather than trusting exit codes:

- Method counts are in range — android ~27k, windows ~32k, merged ~37k. Far below means
  traces were dropped.
- The per-assembly listing shows `Mono.Android` **and** `Microsoft.WinUI` / `WinRT.Runtime`.
  A missing half is otherwise silent: the merge succeeds with whatever parts survived. (When
  only one platform was re-recorded, the other half comes from the existing `.mibc` — so both
  should still appear.)
- The expected files changed.

### Clear stale R2R output

```bash
rm -rf artifacts/obj/App.Maui/release_net11.0-android_android-arm64/R2R
rm -rf artifacts/obj/App.Maui/release_net11.0-windows10.0.22621.0_win-x64/R2R
```

The pgo files *are* an input to `_CreateR2RImages`, so a profile change normally retriggers
it — but delete the output anyway if anything about the crossgen2 arguments changed in the
same session, because those are **not** inputs and the build silently reuses the old
composite.

## Failure modes

**"Read past end of stream"** during conversion — the `.nettrace` is truncated. On Android
this is almost always the device dropping off ADB mid-capture; look for a capture that is
~13 MB where the others are ~420 MB. Recover with `adb kill-server && adb start-server`, then
re-record that platform. `New-StartupMibc.ps1` skips bad traces and merges the rest, so check
that its "merging N part(s)" line matches the number of runs.

**`adb install` prints "check for a confirmation dialog"** — the install did not happen and
you are about to profile the *old* build. Verify with
`adb shell dumpsys package chat.actual.dev.app | grep lastUpdateTime` and retry.

**Device goes `offline` repeatedly** — recurring on this hardware. `adb kill-server && adb
start-server` recovers it; if it keeps happening, tell the user to reseat the cable rather
than burning runs.

**"Unable to validate match between assembly ..."** means the references don't match what
ran. `New-StartupMibc.ps1` points at the `R2R/` output, so this shouldn't appear; if it does,
the app was rebuilt after recording — re-record.

## Reporting

Give the user method counts for all three files with before/after deltas, which platform
halves were re-recorded, and any traces that were dropped. If runs were lost, say how many
rather than quietly merging fewer.
