# Android ANRs — FCM cold-start stalls

Status: **investigation complete, nothing implemented yet.** Data pulled from
Play Console on 2026-08-25.

## Summary

Two thirds of Voxt's Android ANRs are one problem: an FCM push wakes a dead
process, and `Application.onCreate` doesn't return before Android's broadcast
timeout. The main thread isn't burning CPU during that window — it's *blocked
on a runtime mutex* while ~57 other threads (Firebase, GMS, WorkManager,
Conscrypt, OkHttp, two JIT engines) fight over a 2019 mid-range device.

That framing matters: regenerating `.mibc` startup profiles shortens JIT work,
but the main thread here is queued behind someone else doing that work. The
plan below reduces what runs at all on a push-started process, which helps
regardless of how fast the JIT gets. The `.mibc` refresh is still worth doing —
it's just not the primary fix.

## Evidence

### Play Console vitals

Play Console → Android vitals → Crashes and ANRs, `chat.actual.app`, all app
versions, type = **All ANRs**, **Last 28 days (Jul 28 – Aug 25, 2026)**. Play
rejects `days=30` and snaps to 28.

**79 ANR events across 45 distinct clusters, ~35 affected users.**

Top clusters by event count:

| Events | Crashing frame | Trigger | Insights | Versions | Last |
|---:|---|---|---|---|---|
| **23** (29.1%) | `MauiApplication.n_onCreate` | Broadcast `c2dm.intent.RECEIVE` → `FirebaseInstanceIdReceiver` | Native lock contention + Slow app startup | 2.15.147 +2 | 4d |
| **8** (10.1%) | `MauiApplication.n_onCreate` | same FCM broadcast | Slow app startup | 2.15.147 +1 | 3d |
| **3** (3.8%) | `mono.java.lang.RunnableImplementor.n_run` | Input dispatching timed out | Native lock contention | 2.15.147 +2 | 3d |
| **2** | `mono.java.lang.RunnableImplementor.n_run` | Input dispatching timed out | — | 2.15.147 | 8d |
| **2** | `pal_misc.c – CryptoNative_GetRandomBytes` | main thread waiting too long | Slow app startup | 2.15.147 +1 | 4d |
| **2** | `MainApplication.n_onCreate` | same FCM broadcast | Slow app startup | **2.16.608 (current prod)** | 31h |

The remaining 39 clusters have 1 event each.

Grouped by root cause rather than by top frame:

- **FCM push waking a cold process — 52 of 79 events (66%).** Every one carries
  `Broadcast of Intent { act=com.google.android.c2dm.intent.RECEIVE
  cmp=chat.actual.app/com.google.firebase.iid.FirebaseInstanceIdReceiver }`.
  The top frames are scattered across RyuJIT internals (`lowerarmarch.cpp`,
  `lsrabuild.cpp`, `lsra.h`, `block.cpp`, `compiler.cpp`, `lclvars.cpp`,
  `emit.h`, `regMaskTPOps.cpp`, `codegenarmarch.cpp`, `ee_il_dll.cpp`) plus
  `LZ4_decompress_safe`, `mono.android.Runtime.initInternal` and
  `mono.android.Runtime.register` — the .NET runtime JIT-compiling and
  unpacking assemblies while the broadcast timer runs.
- **Input dispatching timed out — 20 events (25%).** UI-thread stalls with a
  user present: `RunnableImplementor.n_run`, `MessageQueue.nativePollOnce`,
  WebView frames (`AppWebMessagePort.onMessage`, `AwBrowserProcess.e`,
  `WV.o1.h`, `J.N.OOZ`, `J.N.IZ`),
  `AndroidWebKitWebViewManager_BlazorWebMessageCallback.n_onMessage`,
  `MauiWindowInsetListener.n_onApplyWindowInsets`, `Fragment.onStop`.
- **Other — 7 events (9%).** Slow IO during startup, a binder-transaction wait,
  a `JobInfoSchedulerService` execution, and one on a `SCREEN_OFF` broadcast
  landing in Sentry's `SystemEventsBreadcrumbsIntegration`.

The bulk-report CSVs (`gs://pubsite_prod_6291022562349091998/stats/crashes/`,
Jul 26 – Aug 17, the last day with data) agree and add the device breakdown:
**67 ANRs vs 9 crashes** — ANRs are ~7× crash volume — with **38 of 67 on
`a51`** (Samsung Galaxy A51) and **40 of 67 on Android 12**.

### The top cluster's thread dump

Sample: Samsung Galaxy A51, Android 12 (SDK 31), 2.15.147, visibility
Background. 23 events / 3 users, **+187% events vs. the previous 28 days**.

Main thread:

```
#00 syscall+28                          libc.so
#01 __futex_wait_ex+144                 libc.so
#02 NonPI::MutexLockWithTimeout+748     libc.so
#03 <split_config.arm64_v8a.apk>
at crc6488302ad6e9e4df1a.MauiApplication.n_onCreate (Native method)
at crc6488302ad6e9e4df1a.MauiApplication.onCreate (MauiApplication.java:27)
at android.app.Instrumentation.callApplicationOnCreate (Instrumentation.java:1211)
at android.app.ActivityThread.handleBindApplication (ActivityThread.java:7506)
```

At that instant the process had **57 threads**:

- **Firebase / GMS, fully spun up**: `Firebase Background Thread #0–#3`,
  `Firebase Blocking Thread #0–#1`, `Firebase-Messaging-Init`,
  `Firebase-Messaging-Topics-Io`, `MessengerIpcClient`, `GmsDynamite`,
  `GoogleApiHandler`, `ScionFrontendApi` + `Measurement Worker` (Analytics),
  `ProcessStablePhenotypeFlag`, `OkHttp ConnectionPool`, `Okio Watchdog`,
  4× `ConscryptStatsLogWriter`, 3× `DefaultDispatcher-worker-*` (Kotlin
  coroutines), **4× `WM.task-*` (WorkManager)**.
- **Two JIT engines at once**: ART's `Jit thread pool worker thread 0` and
  Mono's `.NET Tiered JIT`.
- **Disk I/O**: `queued-work-looper`, `queued-work-looper-data`,
  `queued-work-looper-timer` (SharedPreferences async writes), `Profile Saver`,
  `FileObserver`.
- **`.NET TP Gate` at tid 526** — a high tid, i.e. created late. The gate
  thread only appears once the ThreadPool first dispatches work.

Two conclusions:

1. **Most of that ran before our code did.** `ActivityThread.handleBindApplication`
   installs ContentProviders *before* `callApplicationOnCreate`, so
   `FirebaseInitProvider`, `androidx.startup.InitializationProvider` and friends
   execute — and leave their thread pools churning — before
   `MainApplication.OnCreate` gets its first instruction. None of it is visible
   in our breadcrumbs.
2. **The main thread is blocked, not busy.** Play's "Native lock contention"
   insight is correct. Reduce the contention and we win regardless of JIT speed.

Caveat: the dump names no lock holder, and the `.NET *` / `Thread-9..13` threads
report as `Unknown` / `Native`, so *who holds the mutex* is inference, not
evidence. The plan is ordered so the cheap, safe steps land before the ones that
depend on that inference.

### Already fixed: the main-thread monitor

`AndroidMainThreadMonitor.n_println` is the top frame in 2 of the 45 clusters —
both on 2.15.147 only. `Looper.SetMessageLogging` makes ART's `Looper.loopOnce`
do, for **every** main-looper message, two Java string concatenations
(`Handler.toString()` + `Runnable.toString()`) plus two JNI upcalls into managed
`Println` — feeding exactly the GC pressure and native transitions it was meant
to observe.

`MauiSettings.Diagnostics.EnableMainThreadMonitor` went `false` in `4dbc19f541`
*perf(android): stop instrumenting every main-looper message* (2026-08-18),
which is on `origin/release/v2.16` → shipped in prod 2.16.608. No monitor frames
appear on 34603616. **Closed.**

One residual if it's ever re-enabled for a debugging session: the slow path
calls `Log.LogWarning` synchronously on the main thread *after* it has already
been blocked >1s, piling on at the worst moment.

## The constraint that shapes the design

`Application.onCreate` runs on the main thread inside `handleBindApplication`.
Until it returns, the main looper dispatches nothing — the
`c2dm.intent.RECEIVE` broadcast is a *later* main-thread message. **There is no
way to yield to the looper from inside `onCreate`; there is nothing to yield
to.**

So "pacing the FCM path" has to mean: `Application.onCreate` returns fast, and
everything else happens after it, in bounded slices. Genuine pacing (idle
handlers, chunked warm-up) is available for the post-`onCreate` phase and is
where most of this plan lives — it just can't rescue `onCreate` itself.

## The budget is 10 seconds, not 60

All three Android send paths in `src/dotnet/Notifications.Service/FirebaseMessagingClient.cs`
(lines 121, 187, 234) use `Priority = Priority.High`.

High-priority FCM messages are delivered as **foreground broadcasts**, which get
a **10s** ANR budget instead of the background 60s. On an A51 cold-starting
Firebase + GMS + WorkManager + Mono + the full DI graph, 10s is tight.

There is a second cost. FCM watches whether a high-priority message actually
produces a user-visible notification; when it repeatedly doesn't, it downgrades
the app's future messages. `FirebaseMessagingService.OnMessageReceivedImpl`
already logs this — `IsDeprioritized` is `message.Priority != message.OriginalPriority`.
So sending non-notifying pushes at High isn't merely wasteful, it spends the
allowance that real notifications need.

## Plan

Ordered cheapest-and-safest first. Steps A1–A5 are independent and individually
shippable; A1 needs no app release at all.

### A1 — Priority triage: High only where Doze must break

The decision rule is **"is this worth waking the device out of Doze?"**, not
"is this a notification?".

- **High — keep.** Anything the user should learn about now: chat messages,
  mentions, incoming calls, the PTT / speech-started wake (line 234). These are
  the product; a message that arrives at the next maintenance window is a worse
  product. They also produce a visible notification, so they don't burn the
  high-priority allowance.
- **Normal — downgrade.** The dismissal / badge-update push (line 187). Its own
  comment reads *"a silent push: it only updates the badge and lets the app drop
  dismissed notifications."* Zero urgency, no visible notification, and today it
  cold-starts the process on a 10s clock **and** spends the allowance that the
  messages in the bullet above depend on.

Expected effect: removes the dismissal push from the cold-start ANR population
entirely and stops it deprioritizing the pushes that matter. It does **not**
fix the message-notification path — that one stays High by design, which is why
A2–B1 exist.

Worth measuring afterwards: dismissal-push delivery latency, and whether
`IsDeprioritized` shows up less often in logs.

### A2 — Move `WarmUpWebView` off the push path

`src/dotnet/App.Maui/Platforms/Android/MainApplication.cs`

```csharp
public override void OnCreate()
{
    WarmUpWebView();   // Task.Run, BEFORE base.OnCreate()
    base.OnCreate();   // → CreateMauiApp() on the main thread
```

On a push-started process there is no `MainActivity` and no WebView coming, so
this is pure contention for zero benefit: it forces ThreadPool spin-up, JIT of
the lambda, and JNI type-load for `Android.Webkit.WebSettings`, all taking
runtime locks the main thread then needs inside `CreateMauiApp()`. The
`.NET TP Gate` thread at tid 526 in the dump is very likely it —
`WarmUpWebView` is the process's first ThreadPool work item.

No heuristic needed: move it into `MainActivity.OnCreate`, where its own comment
says it belongs ("right before `BlazorAndroidWebView` is constructed"). Then it
runs only when an activity really is starting.

### A3 — Gate `AndroidActivitiesForegroundService.TryStartArmed`

Android 12+ bans background foreground-service starts, so on a push-started
process this always throws `ForegroundServiceStartNotAllowedException` — a
binder round-trip plus exception construction on the critical path for a
guaranteed failure. **Android 12 is 40 of 67 ANR events in the CSVs.**

Also note `MauiPreferences.IsPttArmed` forces the first synchronous
SharedPreferences load; there is an ANR cluster labelled "ANR triggered by slow
IO operations" on `MauiApplication.n_onCreate`.

Gate on API level and on whether a foreground start is actually permitted.

### A4 — ContentProviders: what they are before we touch them

`handleBindApplication` order is:

1. `makeApplication()` — `Application` constructed, `attachBaseContext()`
2. `installContentProviders()` — **every `<provider>` in the merged manifest is
   instantiated and its `onCreate()` runs, on the main thread**
3. `callApplicationOnCreate(app)` — our `Application.OnCreate()`

So all of the below runs before we get control, on every process start,
including push starts.

**`FirebaseInitProvider`** (firebase-common) — calls
`FirebaseApp.initializeApp(context)` from the `google-services.json` values
baked into resources, then runs Firebase component discovery. Required for FCM
to work at all. **Keep.** Side note: our `InitFirebaseApp` in
`MauiProgram.Android.cs` calls `FirebaseApp.InitializeApp(context)` again — a
no-op, since the provider already did it.

**`androidx.startup.InitializationProvider`** (androidx.startup) — one provider
that runs every registered `Initializer<T>`. The ones that matter here:

- **`WorkManagerInitializer`** (androidx.work) — calls `WorkManager.initialize()`,
  which opens/creates the `androidx.work.workdb` Room/SQLite database, starts
  its executor (the four `WM.task-*` threads in the dump), and runs a
  force-stop / reschedule pass over persisted work. Disk I/O plus four threads
  at **every** process start.

  `Xamarin.AndroidX.Work.Runtime` is an *explicit* `PackageReference` in the
  Android `ItemGroup` (`App.Maui.csproj:179`) and there are **zero** uses of
  `AndroidX.Work` / `WorkManager` anywhere in `src/dotnet/`.

  **Decision: try removing it.** WorkManager is pure AndroidX — it has nothing
  to do with the .NET runtime, and Mono vs CoreCLR makes no difference to it.
  It is needed only when the app or a transitive library schedules persistent
  background jobs, and Android's own docs cover removing the initializer when
  it is unused or initialized manually. Nothing in our code schedules any work,
  so the default assumption is that it goes.

  Preferred form: drop the `PackageReference` outright — that removes the
  initializer, the `workdb` database and the four threads in one move. If
  something does pull it transitively (firebase-messaging is the obvious
  candidate), fall back to disabling just the initializer:

  ```xml
  <provider android:name="androidx.startup.InitializationProvider"
            android:authorities="${applicationId}.androidx-startup"
            tools:node="merge">
    <meta-data android:name="androidx.work.WorkManagerInitializer"
               tools:node="remove" />
  </provider>
  ```

  **Caveat:** removing the initializer without providing on-demand init makes
  the first `WorkManager.getInstance()` throw. The `Application` class must then
  implement `androidx.work.Configuration.Provider`.

- **`ProfileInstallerInitializer`** (androidx.profileinstaller) — installs the
  APK's baseline profile into ART's profile directory on the first run after
  install/update, and no-ops afterwards. The real work is deferred to a
  background thread, so this one is cheap; listed for completeness. It interacts
  with the `.mibc` work — do not remove without checking that.

- Depending on what's pulled in, `EmojiCompatInitializer` and
  `ProcessLifecycleInitializer` may also be registered. Enumerate the merged
  manifest before deciding.

**Firebase `ComponentDiscoveryService`** — not a provider but a `<service>`
whose `meta-data` entries list which Firebase `ComponentRegistrar`s to
construct at init. Each Firebase dependency adds a line. Trimming means
removing the meta-data entries for products we don't use.

- **Analytics** (`ScionFrontendApi`, `Measurement Worker`) — genuinely used:
  `ActivateDataCollectionIfEnabled` is called from `CreateMauiApp` and gated on
  `MauiPreferences.IsDataCollectionEnabled`. Keep, but note the registrar is
  constructed at process start regardless of whether consent is on — worth
  checking whether it can be deferred to the consent path.
- **Crashlytics** — `Xamarin.Firebase.Crashlytics` is referenced in the Android
  `ItemGroup` (`App.Maui.csproj:182`), but there is **no Crashlytics call
  anywhere in Android code** — only iOS uses it, via
  `Plugin.Firebase.Crashlytics` in `MauiProgram.iOS.cs`. Android's
  `strings.xml` has `com.google.firebase.crashlytics.mapping_file_id = none`,
  suggesting mapping upload was never wired up. Crashlytics installs
  `CrashlyticsInitProvider` and replaces the default uncaught-exception handler
  at startup. **Verify it isn't there deliberately for native crash capture,
  then drop it.**

**Sentry** — `Sentry.Bindings.Android` 5.14.1 arrives transitively (along with
`Sentry.Serilog`, `Sentry.Extensions.Logging`, `Sentry.OpenTelemetry`); no
project references it directly. The native Android SDK auto-installs
integrations at init, including `SystemEventsBreadcrumbsIntegration`, which
registers a `BroadcastReceiver` for SCREEN_ON/OFF, battery and similar — and one
of our ANR clusters is exactly a SCREEN_OFF broadcast into that receiver. Sentry
is actively used, so it stays; the integration list is configurable and worth
trimming.

### A5 — Notification channels don't depend on an activity

`NotificationHelper.EnsureDefaultNotificationChannelExist` and
`EnsureActivityChannelsExist` run in the **activity** `OnPostCreate`
(`MauiProgram.Android.cs`), as does `ChatAttentionService.Instance.Init()`. A
push-started process never creates an activity. Channels persist once created,
so in practice this only bites before the first launch — but it becomes load-
bearing the moment B1 lands and push-started processes stop building the MAUI
app at all. Move channel creation into the cheap `Application.OnCreate` path and
check whether `ChatAttentionService.Ask` is safe without `Init()`.

### B1 — Lazy, idle-primed `MauiApp`

The structural fix, and the one that needs care.

`Application.OnCreate` should do only the cheap things — `MauiDiagnostics.Initialize`,
breadcrumbs, notification channels (A5) — and return. The DI graph gets built
by a holder with two entry points:

```
MauiAppHolder.Prime()   // schedule the build for when the main looper goes idle
MauiAppHolder.Get()     // return the MauiApp, building it inline if not ready
```

- `Application.OnCreate` calls `Prime()` and returns immediately.
- `MainActivity.OnCreate` calls `Get()` → inline build. Identical to today's
  behaviour for user launches; the splash covers it.
- The FCM push path never calls `Get()`. It doesn't need to: `NotificationHelper`,
  `IncomingCallNotifications`, `ChatAttentionService` and `PttWakeHandler` use
  only `StaticLog` plus Android APIs, and every scoped-service use is already
  best-effort behind `AppServicesAccessor.TryGetScopedServices`, which is
  null-safe and returns `false`. Verified against the current code.

**Scheduling: use `Looper.MainLooper.Queue.AddIdleHandler`, not `Handler.post`.**
An idle handler runs only when the queue has nothing left to dispatch, which
*guarantees* the broadcast has been delivered and the notification posted before
the build starts. A posted `Runnable` only wins the race if AMS hasn't enqueued
the broadcast yet, which is timing-dependent and not something to build on.

**On the "run it on a different thread" idea.** Attractive, and the DI container
construction itself is probably fine off-thread — but `ConfigureMauiApp` and
`MauiApp.Build()` touch platform APIs, and `IPlatformApplication.Current` /
`Services` must be set before the first activity is created. The idle-handler
variant keeps everything on the main thread, which sidesteps that entire class
of question while getting nearly all of the benefit: the point is that the build
happens *after* the broadcast, not that it happens *elsewhere*. Recommendation:
ship the idle-handler version, measure, and only then consider moving parts of
the build to a background thread with a real trace to justify it.

**Framework friction to expect.** MAUI's `MauiApplication.OnCreate` both
registers activity lifecycle callbacks and calls `CreateMauiApp()`. Splitting
"cheap eager" from "expensive lazy" means not calling `base.OnCreate()` and
replicating its cheap half — the lifecycle-callback registration in particular
must stay eager. This is the part that needs a real test pass across cold start,
warm start, push-only start, and push-then-launch.

### Rejected — a separate `:push` process

Putting `FirebaseMessagingService` and `FirebaseInstanceIdReceiver` in
`android:process=":push"` would structurally eliminate the top cluster, but
pushes would land in `:push` even when the app is foregrounded, so
`TryGetScopedServices` would always return `false`. That loses "suppress if the
user is viewing this chat", "already read on this device", and the in-app
ringer, and recovering them needs cross-process forwarding. Recorded for
completeness — **we are not planning to do this.**

## Instrumentation first

`MauiStartupBreadcrumbs` (`src/dotnet/Maui/MauiStartupBreadcrumbs.cs`) and
`AndroidProcessExitReporter` already persist startup-phase marks and report the
previous process's `ApplicationExitInfo` ANR on the next launch. One dev/beta
cycle can tell us where the 10s actually goes on an A51-class device instead of
inferring it. Add:

- a mark for the start reason — `ActivityManager.GetMyMemoryState` gives
  `RECEIVER` for a broadcast-started process vs `FOREGROUND` for a user launch;
- sub-marks around `WarmUpWebView` and the `CreateMauiApp` phases.

**Caveat:** each `MauiStartupBreadcrumbs.Add` is a synchronous
`File.AppendAllText` (open / append / close) on the main thread. If we add
marks, buffer them in memory and flush once — otherwise the instrument feeds the
problem it measures.

## Reuse

**Existing abstractions to reuse:**

- `MauiStartupBreadcrumbs` — startup phase marks, already persisted across the
  ANR kill.
- `AndroidProcessExitReporter` — `ApplicationExitInfo` ANR reporting on next
  launch.
- `AppServicesAccessor.TryGetScopedServices` / `DispatchToBlazor` — already the
  correct best-effort pattern for the push path; B1 depends on it staying that
  way.
- `StaticLog` — works without the MAUI container, which is what makes the push
  path DI-free today.
- `MauiSettings.Diagnostics` — the right home for any new toggle.
- `AndroidUtils.IsAppForeground` — existing process-state helper.

**New components and where they belong:**

- A *process start reason* helper (`RECEIVER` vs `FOREGROUND`) — Android-only,
  so it belongs next to `IsAppForeground` in
  `App.Maui/Platforms/Android/AndroidUtils.cs`, not in a shared project.
  `ActualChat.Core` has no Android dependency and shouldn't gain one.
- `MauiAppHolder` (B1) — MAUI-Android-specific lifecycle glue; belongs beside
  `MainApplication`. Not shared: it encodes assumptions about
  `IPlatformApplication.Current` that only this host has.

Neither is a reuse candidate elsewhere, so both stay feature-local.

## Open questions

1. Does anything pull `Xamarin.AndroidX.Work.Runtime` transitively, or is the
   explicit `PackageReference` simply dead? We intend to remove it either way;
   this only decides whether A4 is a clean package removal or an initializer
   removal plus `Configuration.Provider`.
2. Is `Xamarin.Firebase.Crashlytics` on Android deliberate (native crash
   capture) or vestigial?
3. Can the Firebase Analytics `ComponentRegistrar` be deferred to the consent
   path rather than constructed at process start?
4. Who actually holds the mutex the main thread waits on? A2 is the leading
   hypothesis; a systrace or a Perfetto capture on an A51-class device would
   settle it, and would also tell us whether B1 is worth its complexity.
5. Does `ChatAttentionService.Ask` work without `Init()` having run? Relevant
   once push-started processes stop creating the MAUI app.

## Related

- `.mibc` startup profiles / partial ReadyToRun — see the `mibc-update` skill.
  Complementary to this plan, not a substitute: it shortens JIT work, this plan
  reduces contention for it.
