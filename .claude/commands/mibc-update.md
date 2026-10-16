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

**`replace`** (also written `-replace`), alongside any of those, means: ditch the existing
recorded profile of every platform being re-recorded and keep only the new capture. Without
it, each re-recorded profile is the union of the new capture and the old file, which can only
add methods and is always safe.

- It applies to the recorded files only (`android`, `windows`, `ios`). `aothelper.mibc` is
  regenerated from the tree either way, and `android-notif.mibc` is never touched.
- `merged.mibc` is the same step in both modes: the union of whatever per-platform files are
  on disk at the end. `replace` changes what goes *into* those files, not how `merged.mibc` is
  built.
- Replacing discards coverage, so the numbers come first: print `Compare-Mibc.ps1` for each
  file, show them to the user, and only then keep the new file — see *Merge, or replace*.
- It is only meaningful when the capture is a **full recording**, not a delta. That depends on
  what the capture build took as profile input — see the next paragraph.

**Capture builds take no recorded profile — only `aothelper.mibc`.** A build with tracing
enabled must not feed crossgen2 `merged.mibc` or any per-platform `.mibc`; where the platform
uses a static profile at all, that is `aothelper.mibc` and nothing else. A capture build that
compiles against the recorded profiles precompiles exactly what they list, so its trace holds
only what they did *not* cover: a delta over the old files. Merging a delta into the old file
is fine; replacing the old file with it throws the old coverage away — this is how a 4,507
method `ios.mibc` was once replaced by a 280 method one. `aothelper.mibc` is the exception
because it is static, regenerated from the tree every time, and on iOS the app does not connect
at all without it.

- **iOS: enforced.** `record-ios-startup-profile.sh build` is run with `BASELINE=aothelper`,
  which removes `merged.mibc` and `ios.mibc` from the crossgen2 inputs.
- **Android and Windows: not enforced yet.** Their capture builds still compile against
  `merged.mibc`. Their traces carry R2R events as well as JIT events, so they list everything
  that ran, precompiled or not, which is why `replace` is still sound there — but that is an
  argument, not a guarantee, and it is why the stats come first.

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
`scripts/record-ios-startup-profile.sh`. Run it **last**, so it sees the `.g.cs` files and
`aothelper.mibc` produced above.

**The Mac builds what is pushed, not your working tree.** Before the build, commit what the
steps above changed — the `*AotSource.g.cs` files and `aothelper.mibc`; the recorded `.mibc`
files are not inputs of the capture build — as `chore(profiling): …`, and push it. **Stage
files by name; never `git commit -a`**, which sweeps unreviewed profile files into the commit.
`build` resets the Mac's checkout to `origin/dev`, so a `.g.cs` that was not pushed is silently
absent from the build and the recording then profiles a build that does not match.
`EXPECT_SHA` makes it fail instead:

```bash
git push origin HEAD:dev
SHA=$(git rev-parse HEAD)
```

Tell the user what was committed and pushed. If the current branch is not `dev`, say so and
use that branch as `BRANCH` instead.

The trace lists the methods that **ran interpreted** on the build it was recorded on. With
`BASELINE=aothelper` that build precompiles only what `aothelper.mibc` covers, so the trace is
the full profile — what a real session needs beyond the static set (1,504 methods in the last
round, against 4,507 in the file it replaced, 2,610 of which were in neither the new trace nor
`aothelper.mibc`). The stats in *Merge, or replace* decide whether to merge the old file in.

```bash
# 1. build (~4 min) and install on the phone
ssh macmini "BRANCH=dev BASELINE=aothelper EXPECT_SHA=$SHA caffeinate -is ~/Projects/ActualChat/scripts/record-ios-startup-profile.sh build"
ssh macmini '~/Projects/ActualChat/scripts/record-ios-startup-profile.sh install'
```

`build` prints the compiled-method count; the aothelper-only build compiled 436,211, against
438,652 with every profile and 421,318 with none. **Do not use `BASELINE=1`** (no profile at
all): that build starts, paints cached content, and never connects — every RPC call stays
`delayed … completed stage: None` with nothing logged.

**This is a tracing build and cannot be opened by hand.** It suspends at launch until a
recorder attaches, sits on the splash screen, and iOS's launch watchdog kills it after ~20 s
(`0x8BADF00D … process-launch watchdog`). Tell the user not to tap the icon, and not to
close the app during the first ~40 s of a run while it waits for the recorder.

The script itself comes from the same push; if the Mac has no
`scripts/record-ios-startup-profile.sh` yet, run `git -C ~/Projects/ActualChat pull` there once.

**Then stop and tell the user to unlock the phone and be ready**: the capture is only worth
anything if someone drives the app for the whole window — open chats, scroll, record voice,
join a call. A launch-only capture leaves the audio and call paths interpreted. The phone
must be tethered and unlocked. A reinstall keeps the sign-in; a deleted app does not — then do
a short sign-in run first (below).

```bash
# 2. move earlier traces aside - convert merges every *.nettrace in the folder
ssh macmini 'cd ~/Projects/ActualChat/tmp/ios-profile && mkdir -p aside && find . -maxdepth 1 -type f \( -name "*.nettrace" -o -name "*.mibc" -o -name "*.etlx" \) -exec mv {} aside/ \;'
# 3. record - launches the app suspended, then traces for the window (a sign-in run first
#    if the app is signed out: 00:01:00, then the main run)
ssh macmini '~/Projects/ActualChat/scripts/record-ios-startup-profile.sh record 00:03:00'
# 4. convert every trace on the Mac into one ios.mibc, bring it back
ssh macmini '~/Projects/ActualChat/scripts/record-ios-startup-profile.sh convert'
scp macmini:Projects/ActualChat/tmp/ios-profile/ios.mibc tmp/ios-new.mibc
```

When the user wants an open-ended run, start `record` with a long window (`00:15:00` as a
ceiling) and stop it on request with `ssh macmini 'pkill -INT -f "dotnet-trace collect"'`
— the file is written on SIGINT. Keep runs to a few minutes: the recorder has a 1 GB buffer
(`BUFFER_MB`), and a run that overflows it silently loses events.

**Check for lost events before trusting a trace.** `convert` prints any
`Warning: N Lost events`; the first two iOS traces lost ~2 million each (512 MB buffer, 3 min)
and produced a 280-method profile that looked plausible. Zero is the only acceptable value.

The `create-mibc` "MVID mismatch" warnings are expected here too.

A dsrouter failure (`Failed USBMuxConnectByPort … result = 61`) means the tracer started
before the app was up: rerun `record`. A trace file of 0 bytes, or no `DOTNET_DiagnosticPorts`
line in the build output, means the build lacks the diagnostic port: rerun `build`. After a
runtime/SDK bump pass `CLEAN=1` to `build` — an incremental build keeps the old native runtime.

### Merge, or replace

By now every re-recorded file holds only its new capture (for iOS it is in `tmp/ios-new.mibc`;
copy it to `_Profiling/ios.mibc` once the stats are in); the old ones are in `tmp/mibc-old/`.
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

**iOS: the app freezes after painting** (`0x8BADF00D`, `CID_VirtualOpenDelegateDispatchWorker`
on the main thread) — `DynamicCodeSupport=false` was removed. `dotnet/runtime#130840` still
reproduces on .NET 11 RC2; the property must stay.

**iOS: `convert` picks up an old trace** — `convert` merges every `*.nettrace` in
`tmp/ios-profile`. Move earlier ones aside first (step 2 above).

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
