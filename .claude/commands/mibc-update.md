---
allowed-tools: Bash, Read, Edit
description: Refresh the .mibc profiles in App.Maui/_Profiling - aothelper.mibc (with the AOT helper .g.cs files), android.mibc, android-notif.mibc, windows.mibc, ios.mibc and merged.mibc. Takes [aothelper|android|android-notif|windows|ios|all] and an optional replace flag, default all.
---

# /mibc-update — refresh the profiles

Maintains six files in `src/dotnet/App.Maui/_Profiling/`: `aothelper.mibc` (static, from
`App.AotHelper`; used by iOS / Mac Catalyst Release), the recorded `android.mibc`,
`android-notif.mibc`, `windows.mibc` and `ios.mibc`, and `merged.mibc` — their union, and the
only one Android and Windows builds compile against (iOS Release takes `ios.mibc`,
`merged.mibc` and `aothelper.mibc`).

`$ARGUMENTS` selects what to refresh:

| Argument | Refreshes |
|---|---|
| `aothelper` | the `*AotSource.g.cs` files and `aothelper.mibc`, via `/aot-helpers-update mibc` |
| `android` | `android.mibc` |
| `android-notif` | `android-notif.mibc` — needs a push sent by hand, see below |
| `windows` | `windows.mibc` |
| `ios` | `ios.mibc` — recorded on the Mac mini and the iPhone, see below |
| `all` *(default, and what empty means)* | `aothelper`, then `android` + `windows`, then `ios` |

Anything else — stop and say what the valid values are; don't guess.

**`replace`** (also written `-replace`), alongside any of those: don't merge the old
platform-dependent profiles into the new recordings — ditch them. Without it, every
re-recorded profile is the union of the new capture and the old file. It applies to the
recorded files only (`android`, `windows`, `ios`); `aothelper.mibc` is regenerated from the
tree either way, and `android-notif.mibc` is never touched. `merged.mibc` is the same in both
modes: the union of whatever per-platform files are on disk at the end. Replacing discards
coverage, so the numbers come first — see *Merge, or replace*.

**Order in `all`:** `aothelper` runs first. It rewrites the `.g.cs` files that the Android and
Windows publishes compile, so recording against the old ones would profile a stale build.
`ios` runs last: its build compiles against `merged.mibc` and `aothelper.mibc`, and happens on
another machine from pushed commits, so it must see everything above.

**`all` deliberately excludes `android-notif`.** That capture can't be automated end to end:
it needs someone to send the device a push at the right moment. The file is a kept artifact
that gets merged in from disk on every run, so it stays useful without being re-recorded.

`merged.mibc` is rebuilt from whichever of the three exist, whatever was selected — so a
single-platform run still produces a complete union.

Background, measured numbers and the reasoning behind every step:
[`docs/startup-profiling.md`](../../docs/startup-profiling.md).

## Before starting

Check and report these; don't assume any of them.

For `ios` or `all`:
- `ssh macmini true` answers, and the iPhone is tethered and **unlocked** (`/macmini`).
- The dev app on the phone is signed in; a reinstall keeps the sign-in.

For `android` or `all`:
- `adb devices` shows one device in state `device` — not `offline`, not `unauthorized`.
- **The app on the device is signed in.** A signed-out recording profiles the sign-in screen
  instead of real startup and is worthless. If it isn't signed in, stop and tell the user —
  signing in needs them, and a tracing build can't even be opened by hand without a tracer
  attached (use `-Mode Attach` to give them a window if they ask).

For any platform:
- `src/dotnet/App.Maui/wwwroot/dist` exists. If not, run `npm run build:Release` first —
  without it the app starts and then dies, and the profile is garbage.
- **Nothing else is building.** A concurrent publish shares `artifacts/obj/App.Maui/` and
  will overwrite assemblies mid-capture; check that no `dotnet`/`MSBuild` process is
  consuming CPU before you start.
- **Snapshot the committed profiles** — the recordings below overwrite them, and the decision
  in *Merge, or replace* needs the old ones. (They are in Git LFS, so `git show HEAD:…`
  would only give a pointer.)

  ```bash
  mkdir -p tmp/mibc-old && cp src/dotnet/App.Maui/_Profiling/*.mibc tmp/mibc-old/
  ```

Tell the user up front what this costs: Android is **reinstalled** with a tracing build
(which suspends at launch until a tracer attaches), and the whole thing takes roughly 30-40
minutes for `all`.

## Steps

Run in order. Each `dotnet publish` takes several minutes — run it in the background and wait
for completion rather than polling.

### AOT helper — skip unless `$ARGUMENTS` is `aothelper` or `all`

Follow [`/aot-helpers-update`](./aot-helpers-update.md) with `mibc` (don't ask which mode):

```bash
./update-aot-helpers.cmd
```

This takes a minute or two, not the tens of minutes of the recordings. Both the `.g.cs` files
and `aothelper.mibc` are rewritten.

### Android — skip unless `$ARGUMENTS` is `android` or `all`

```bash
pwsh -NoProfile -File scripts/Record-AndroidStartupProfiles.ps1 -Runs 1 -Duration 00:01:00 -Mode Methods -Build
pwsh -NoProfile -File scripts/New-StartupMibc.ps1 -Platform Android
```

`-Build` publishes with `IsTracingEnabled=true` and installs it.

**Record at 60 s, not the script's 10 s default.** A 10 s window stops before the
post-startup paths settle — live session governors, fold/mute enforcement, chat item
loading — and yields roughly half the methods: 23,201 against 31,225 for the same app,
measured 2026-09-03. One 60 s cold start is enough; if you record more than one, keep the
largest and discard the rest. Expect ~700 MB per capture.

**This writes the new capture over `android.mibc`.** The old file is in `tmp/mibc-old/` (see
*Before starting*) and is merged back in afterwards unless `replace` was given — see *Merge,
or replace*. It has to be merged by default: `android.mibc` carries hand-driven session
coverage a cold start never reaches, and replacing it blindly once cost 26k methods.

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

This writes the new capture over `windows.mibc`, like the Android step; a plain run drops
whatever the previous recording had and this one did not (~23k methods once), so the old file
is merged back in afterwards unless `replace` was given — see *Merge, or replace*.

`New-StartupMibc.ps1` rebuilds `merged.mibc` at the end of **every** run, from the files on
disk at that moment — so it has to be rebuilt again after the step below.

### iOS — skip unless `$ARGUMENTS` is `ios` or `all`

Recorded on the Mac mini (`/macmini`), on the tethered iPhone, with
`scripts/record-ios-startup-profile.sh`. Run it **last**: the iOS build compiles against
`merged.mibc` and `aothelper.mibc`, so it has to see the results of every step above.

**The Mac builds what is pushed, not your working tree.** Before the build, commit everything
the steps above changed — the `*AotSource.g.cs` files and every `.mibc` — as
`chore(profiling): …`, and push it. `build` resets the Mac's checkout to `origin/dev`, so a
`.g.cs` or profile that was not pushed is silently absent from the build, and the recording
then profiles a build that does not match. `EXPECT_SHA` makes it fail instead:

```bash
git push origin HEAD:dev
SHA=$(git rev-parse HEAD)
```

Tell the user what was committed and pushed. If the current branch is not `dev`, say so and
use that branch as `BRANCH` instead.

The trace lists the methods that **ran interpreted** on the build it was recorded on, and that
build already compiled everything the old `ios.mibc` rooted — so a fresh trace is expected to
be much *smaller* than the old file (312 methods against 4,351 last time). Merging keeps the
old roots; `replace` drops them (4,195 once), which is why the stats come first.

```bash
# 1. build (~5 min) and install on the phone
ssh macmini "BRANCH=dev EXPECT_SHA=$SHA caffeinate -is ~/Projects/ActualChat/scripts/record-ios-startup-profile.sh build"
ssh macmini '~/Projects/ActualChat/scripts/record-ios-startup-profile.sh install'
```

The script itself comes from the same push; if the Mac has no
`scripts/record-ios-startup-profile.sh` yet, run `git -C ~/Projects/ActualChat pull` there once.

**Then stop and tell the user to unlock the phone and be ready**: the capture is only worth
anything if someone drives the app for the whole window — open chats, scroll, record voice,
join a call. A launch-only capture leaves the audio and call paths interpreted. The phone
must be tethered, unlocked, and the app signed in (a reinstall keeps the sign-in).

```bash
# 2. record - launches the app suspended, then traces for the window
ssh macmini '~/Projects/ActualChat/scripts/record-ios-startup-profile.sh record 00:02:00'
# 3. convert on the Mac, bring the new profile back
ssh macmini '~/Projects/ActualChat/scripts/record-ios-startup-profile.sh convert'
scp macmini:Projects/ActualChat/tmp/ios-profile/ios.mibc src/dotnet/App.Maui/_Profiling/ios.mibc
```

The `create-mibc` "MVID mismatch" warnings are expected here too.

A dsrouter failure (`Failed USBMuxConnectByPort … result = 61`) means the tracer started
before the app was up: rerun `record`. A trace file of 0 bytes, or no `DOTNET_DiagnosticPorts`
line in the build output, means the build lacks the diagnostic port: rerun `build`. After a
runtime/SDK bump pass `CLEAN=1` to `build` — an incremental build keeps the old native runtime.

### Merge, or replace

By now every recorded file holds only its new capture; the old ones are in `tmp/mibc-old/`.
Print the stats for each re-recorded file **before** doing anything to it:

```bash
P=src/dotnet/App.Maui/_Profiling
pwsh -NoProfile -File scripts/Compare-Mibc.ps1 -Old tmp/mibc-old/android.mibc -New $P/android.mibc
```

`old only` is what replacing discards; `new only` is what the new capture adds either way.
Show these numbers to the user — in `replace` mode they are the whole point of this step.

- **Default — merge the old file back in** (always safe: it can only add), per file:

  ```bash
  ./dotnet-pgo.cmd merge -i tmp/mibc-old/android.mibc -i $P/android.mibc -o tmp/android-merged.mibc --compressed
  cp tmp/android-merged.mibc $P/android.mibc
  ```

- **`replace` — keep the new file as it is.** This is the one that needs the stats above. Nothing to do beyond the stats; the old file
  stays in `tmp/mibc-old/` until the temp folder is cleaned, so it can still be restored.

Then rebuild `merged.mibc` from the files as they now stand. It is the same step in both
modes — the union of the final per-platform files:

```bash
pwsh -NoProfile -File scripts/New-StartupMibc.ps1 -MergedOnly
```

### Verify before reporting

```bash
./dotnet-pgo.cmd dump -i src/dotnet/App.Maui/_Profiling/merged.mibc -o tmp/merged-dump.txt | grep "# Methods:"
sed -n 's/.*"Method": "\[\([^]]*\)\].*/\1/p' tmp/merged-dump.txt | sort | uniq -c | sort -rn | head
git diff --stat src/dotnet/App.Maui/_Profiling/
```

Sanity-check the output rather than trusting exit codes:

- A method count that went **down** is only valid under `replace`; otherwise the merge
  failed. Compare against `tmp/mibc-old/`, not against numbers written here — these grow
  every round.
- Overlap with the previous profile is the real check that the trace mapped correctly.
  `common` in `Compare-Mibc.ps1` is how much of the old file the new capture reproduced;
  far less than half on a re-record of the same app means the references were wrong. (Not
  for `ios.mibc`, which is a residue by construction.)
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

**"Unable to validate match between assembly ..."** is expected and is **not** a reason to
re-record. dotnet-pgo prints it for every module even when the `R2R/` references come from
the very publish that was installed — the validation check changed, not the build (see
commit `e0e5783825`). Judge the run by overlap against the previous profile instead; at
90-95% the mapping is correct. Acting on this warning alone burns a full re-record.

**A second build running at the same time.** Android `dotnet publish` shares
`artifacts/obj/App.Maui/`, so anything else building — a `b android`, another
`/mibc-update` — overwrites assemblies mid-capture and can leave the R2R step skipped
entirely. Before starting, check that no `dotnet`/`MSBuild` process is burning CPU. The
signature afterwards: no `crossgen`/`ReadyToRun` lines in the publish log, and no
`artifacts/obj/App.Maui/release_net11.0-android_android-arm64/R2R` directory. Wait for the
other build to finish, then re-record.

## Reporting

Give the user method counts for every recorded file and `merged.mibc` with before/after
deltas, the `Compare-Mibc.ps1` numbers for each and whether it was merged or replaced,
which platform halves were re-recorded, whether `aothelper.mibc` and the `.g.cs` files
changed, what was committed and pushed for the iOS build, and any traces that were dropped.
If runs were lost, say how many rather than quietly merging fewer.
