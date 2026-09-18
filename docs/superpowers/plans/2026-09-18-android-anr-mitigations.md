# Android ANR mitigations — #4622

Play's 28-day user-perceived ANR rate is 0.67–0.99% (threshold 0.47%). Every fixable
foreground ANR is an ART stop-the-world landing on the main thread at a JNI transition; the
background ones are the FCM cold start blowing the 10 s broadcast deadline on low-tier phones.

## Scope (agreed 2026-09-18)

| # | Change | Kind |
|---|---|---|
| A | `DOTNET_GCgen0size` via `AndroidEnvironment` (Release) — fewer managed gen0 GCs → fewer bridge-forced `Runtime.gc()` STWs | measurable A/B |
| B | `DOTNET_EnableDiagnostics=0` (Release) — drops `.NET Debugger`/`DebugPipe` threads | hygiene |
| C | `OnTrimMemory` logs `GC.CollectionCount(0/1/2)` + Java heap, so the next ANR's Crashlytics log shows GC pressure | observability |
| D | `MainApplication` ctor breadcrumb: runtime-init time since process start (`Process.StartElapsedRealtime`) — measures the `Runtime.initInternal` ANR class | observability |
| E | Firebase/Analytics enabling in the activity `OnCreate` handler moves off main, gated on memory pressure (skipped/deferred while `MemoryInfo.LowMemory`) | ANR path |
| F | Investigate audio-path allocation churn (`gc 0/1/2=1/0/0` per second while recording) — findings, not code | investigation |
| G | Investigate what could be shed on `TRIM_MEMORY_*` — findings, not code | investigation |
| — | `MauiSession.Read` is already off main (`Task.Run`, MauiProgram.cs:195) | no change |
| — | `TrimMode=full`, assembly-compression flag, headless slim start, R2R profile refresh, Crashlytics symbol upload | out of scope / follow-ups |

Crashlytics' own init on the FCM path (`CrashlyticsRegistrar → AnalyticsDeferredProxy → zzc.<clinit>`)
runs from `FirebaseInitProvider` before `Application.onCreate`; skipping it means either removing the
provider (breaks FCM message delivery ordering) or `firebase_analytics_collection_deactivated`
(permanent). Not doing either — E covers the part we own.

## Reuse

Existing abstractions:
- `AndroidEnvironment` MSBuild item + `android-tracing-env.txt` pattern (App.Maui.csproj:428) — same mechanism for a Release env file.
- `MauiStartupBreadcrumbs.Add` (Maui/MauiStartupBreadcrumbs.cs) + `MauiDiagnostics.LogTag` `Android.Util.Log` — for D.
- `MainActivity.DumpMemoryInfo` (already off main via `Task.Run`) — C extends it; `ActivityManager.MemoryInfo` for the pressure check in E.
- `BackgroundTask.Run(…, Log, …)` (used by `AndroidProcessExitReporter.Start`) — E's worker.
- `LoadingUI.WhenAppRendered` — the "app is up" signal E and Sentry/exit reporter already key off.
- `MauiProgram.Android.InitFirebaseApp` / `ActivateDataCollectionIfEnabled` — E reuses, only changes *where* they run.

New components: none that warrant a shared type. The memory-pressure check is two lines inside
`MainActivity`/`MauiProgram.Android`; a second caller would justify `AndroidUtils.IsUnderMemoryPressure()`
in `Platforms/Android/AndroidUtils.cs` (already the home for `GetProcessInfo`).

## Verification

- Build `App.Maui` for android (Release) — the env file must land in the APK's environment.
- Device A/B for A: `adb logcat -v threadtime "*:I" | grep -c 'Explicit'` per minute, before/after, same phone + same 60 s of use.
- Crashlytics log of any later ANR must show `OnTrimMemory … gc=` and `Runtime started +X.XXXs`.

## F — audio-path allocation churn (findings, 2026-09-18)

The managed side of the recording path is already pooled end to end: `AndroidAudioCapture` reads
into one `floatReadBuffer`, hands frames out as `ArrayPools.SharedFloatPool` owners through a ring
buffer; `OpusAudioCodec` leases from `SharedBytePool` and returns `PooledSliceOwner` structs (one
boxing per 20 ms frame when written to the channel — ~50 small objects/s, negligible); the TfLite
VAD reuses direct `ByteBuffer`s for every tensor. The `gc 0/1/2=1/0/0` in Sentry's cadence lines is
a biased sample (logged only in seconds that had a gap) and doesn't locate the allocator.

What did allocate per frame was on the **Java** side: `recorder.Read(float[] , …)` in
`AndroidAudioCapture`. The binding marshals a managed `float[]` as
`JNIEnv.NewArray` + `CopyArray` + `DeleteLocalRef` per call — a fresh Java `float[]` of
`frameSamples * 2` floats on every read, plus two copies. That's ART garbage at the read rate,
in the process whose ART heap the bridge's `Runtime.gc()` compacts.

**Fixed (this branch):** the producer now reads into one direct `ByteBuffer` via
`AudioRecord.read(ByteBuffer, sizeInBytes, READ_BLOCKING)` and scans/copies the samples through a
`ReadOnlySpan<float>` over `JNIEnv.GetDirectBufferAddress` — zero Java allocations per read
(AOSP `readInDirectBuffer` writes from the buffer's base address and caps at capacity; position
is ignored).

Follow-up, same shape: `TfLiteVoiceActivityDetector` fills/reads its direct buffers through
`FloatBuffer.Put(float[])` / `Get(float[])`, which marshal a Java array per call as well. The
direct-address span replaces those too.

The rest of a call's allocation rate is not in this path: RPC serialization of outbound audio
chunks, transcript/UI updates and JS interop for the level meter are the usual suspects, and the
way to see them is a `dotnet-gcdump`/EventPipe allocation sample on a device, not code reading.
Recipe: Build-Tracing-AR build → `dotnet-trace collect -p <pid> --providers Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x1:5`
(GC + AllocationTick keywords) over 60 s of a call.

## G — what could be shed on TRIM_MEMORY (findings, 2026-09-18)

- Managed caches that exist: Fusion's computed cache (weak, GC-driven — nothing to shed by hand),
  `KvasarRemoteComputedCache` (persistent, disk-backed), `SendingMessages` (pruned on its own
  schedule), `History` items, `TranslationUI` and `ChatMessageKey` caches (small). None of them is
  a memory-pressure lever, and freeing managed memory needs a managed GC, which is exactly the
  event the bridge turns into an ART stop-the-world — a `GC.Collect()` on trim would be the ANR.
- Java-side: `NotificationHelper.ImagesCache` — 5 `Bitmap`s, the only sizeable ART-heap cache we
  own. Clearing it on `RunningLow`/`RunningCritical` is cheap and honest, but 5 avatar bitmaps
  (~100–400 KB) won't change an ART compaction's cost.
- The big consumer under trim is the WebView renderer, a separate process that gets its own trim
  callbacks; nothing on our side changes what it does.

Conclusion: there is no worthwhile shed. The trim handler should stay what it is now — a
measurement point (GC counters + Java heap logged, `AndroidUtils.NoteTrimMemory` for the pressure
gate) — and the fix for trim-triggered ANRs is fewer bridge GCs (A) and a cheaper cold start.
