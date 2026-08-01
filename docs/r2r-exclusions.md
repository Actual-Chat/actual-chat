# ReadyToRun exclusions

Which assemblies are worth keeping out of the composite R2R image on Android, why, and
how the size numbers were derived. Recorded from the `2.14.273` Android Release build
(`net11.0-android`, `android-arm64`) and a 22.3 s cold-start trace taken on device.

The exclusion list itself lives in `src/dotnet/App.Maui/App.Maui.csproj`, in the
`PublishReadyToRunExclude` item groups. See [Android-specific behavior](./android-specific.md)
for the surrounding build setup and [Native AOT](./native-aot.md) for the other end of
the codegen spectrum.

## Why exclude anything

Release is CoreCLR + **composite, full** R2R on *every* platform — Android, iOS, Mac
Catalyst and Windows: crossgen2 compiles every method of every input assembly into one
composite image. Full rather than partial is a deliberate trade — see the comment above
`PublishReadyToRun` in `App.Maui.csproj` — but it means we pay for native code we never
execute.

Excluding an assembly does **not** remove it from the app. Its IL still ships; only the
precompiled native code goes away. Whatever runs anyway gets JIT'd on Android and Windows,
or interpreted on iOS and Mac Catalyst — see [iOS-specific behavior](./ios-specific.md) —
so the cost of being wrong is a slower first call, not a failure. That makes "large, and
cold in practice" the right selection criterion.

Because the exclusions apply on all four platforms while the evidence comes from an
Android trace, everything on the list has to be cold for a *platform-independent* reason.
That's why the applied set is Tier A minus its platform-specific entries, plus one
Android-only exclusion of its own.

The mechanism works under composite mode: excluded assemblies are absent from the
`R2R/` output directory and get packaged straight from `linked/`.

## Size model

Three measured ratios turn "IL bytes excluded" into "MB off the APK".

| Quantity | Value |
|---|---|
| Composite image `ActualChat.r2r.dll` | 107.4 MiB |
| IL method bodies fed to crossgen2 (228 assemblies, minus `PhoneNumbers`) | 11.5 MiB |
| **Average native/IL expansion** | **9.3×** |
| Measured native/IL for non-generic library and binding assemblies | 3.9–6.2× |
| Composite → APK, from the partial-vs-full R2R experiment (Δ100.6 MB composite → Δ27.1 MB APK) | **3.7 : 1** |

The APK ratio is well below 1:1 because Android stores assemblies LZ4-compressed inside
`lib/arm64-v8a/libassembly-store.so` (57 MB raw, 46.7 MB after the APK's own deflate),
and the store is by far the largest thing in the 103.9 MB APK.

The per-assembly expansion varies enormously with generics — `MemoryPack.Core` measures
38× and `ActualLab.Interception` 22×, because generic instantiations multiply, while flat
JNI bindings like `Xamarin.AndroidX.Core` measure 4.9×. The estimates below use **5.5×**,
which is conservative for the mostly non-generic assemblies on the list.

Rule of thumb: **1 MB of excluded IL ≈ 5.5 MB off the composite ≈ 1.5 MB off the APK.**

## How the data was gathered

`src/dotnet/App.Maui/Trace-A.cmd` records a cold start with
`Microsoft-Windows-DotNETRuntime:0x1F000080018:5` — Loader + Jit keywords, plus the
end-of-session rundown. That yields `ModuleLoad` for every assembly and
`MethodLoadVerbose` / `MethodDCStopVerbose` for every method that actually acquired
native code, which is to say every method that ran.

Joining those events against the IL metadata in
`artifacts/obj/App.Maui/release_net11.0-android_android-arm64/linked` gives, per
assembly: total methods, methods executed, IL body bytes, native bytes, and the measured
expansion ratio.

The result for the traced startup: **22,608 distinct methods executed, and 148 of the 228
shipped assemblies executed no managed code at all.**

## Applied

These are in `App.Maui.csproj` today. All platforms, Release only:

| Assembly | IL code | What it is |
|---|---|---|
| `PhoneNumbers` | 813 KB | phone number parsing / formatting (predates this analysis) |
| `System.Private.Xml` | 627 KB | `XmlReader` / `XmlWriter` / XML serialization |
| `System.Data.Common` | 265 KB | ADO.NET base types |
| `Newtonsoft.Json` | 258 KB | JSON.NET, transitive via `ActualLab.Core` and `RestEase` |
| `RestEase` | 35 KB | REST client generator runtime |
| `Mjml.Net` + `HtmlPerformanceKit` + `ActualChat.Mjml.Blazor` + `ActualChat.Users.Templates` | 222 KB | email templating |

Android only:

| Assembly | IL code | What it is |
|---|---|---|
| `Sentry.Bindings.Android` | 410 KB | JNI bindings for the Sentry Android SDK |

Total excluding `PhoneNumbers`, which was already excluded and contributes nothing new:
**1.77 MB of IL → ~9.8 MB off the composite → ~2.6 MB off the Android APK.** The
all-platform subset of that is 1.37 MB IL; iOS, Mac Catalyst and Windows should see a
proportional cut, though the ratios above were measured on Android only.

The tiers below are the full candidate set the trace produced. Everything not in the table
above is still compiled.

## Tier A — zero use, no plausible hot path

22 assemblies, 1.87 MB IL → ~10.3 MB composite → **~2.8 MB APK**.

| Assembly | IL code | Why it's cold |
|---|---|---|
| `System.Private.Xml` | 627 KB | nothing on the client touches XML |
| `System.Data.Common` | 265 KB | ADO.NET base types, server-only |
| `Newtonsoft.Json` | 258 KB | transitive via `ActualLab.Core` and `RestEase` |
| `Microsoft.CSharp` | 107 KB | the `dynamic` binder |
| `Mjml.Net`, `HtmlPerformanceKit`, `ActualChat.Mjml.Blazor`, `ActualChat.Users.Templates` | 222 KB | email templating — a server concern |
| `Microsoft.ML.OnnxRuntime` | 58 KB | Android uses LiteRt (`libLiteRt.so`); this path is dead |
| `System.ComponentModel.TypeConverter` | 60 KB | |
| `System.Runtime.Numerics` | 60 KB | |
| `Microsoft.Maui.Controls.Xaml` | 39 KB | BlazorWebView app, no XAML at runtime |
| `RestEase` | 35 KB | |
| `K4os.Compression.LZ4` | 30 KB | via `Sentry.Android.AssemblyReader` |
| `System.Reflection.Metadata`, `Sentry.Android.AssemblyReader` | 49 KB | crash-time only |
| `Microsoft.AspNetCore.Components.WebAssembly` | 19 KB | dead in MAUI |
| `System.Diagnostics.Process`, `System.Numerics.Tensors`, `System.Private.Xml.Linq`, `Xamarin.Kotlin.StdLib`, `Xamarin.KotlinX.Serialization.Core.Jvm` | 87 KB | |

## Tier B — zero use, exercised later by native UI

17 assemblies, 1.20 MB IL → ~6.6 MB composite → **~1.8 MB APK**.

`Sentry.Bindings.Android` (410 KB — its module loads at 11.3 s but never executes a
managed method), `Xamarin.Google.Android.Material` (242 KB),
`Xamarin.AndroidX.AppCompat` (131 KB), `Xamarin.AndroidX.RecyclerView` (90 KB),
`Xamarin.Google.Crypto.Tink.Android` (84 KB), `Xamarin.GooglePlayServices.Base` (49 KB),
`Xamarin.AndroidX.Fragment` (48 KB), `Xamarin.AndroidX.Media` (43 KB), plus nine smaller
AndroidX / Google Play Services bindings.

These are thin JNI wrappers reached when native UI appears — permission dialogs, the file
picker, the share sheet, notifications. Cold at startup by construction, and cheap to JIT
when they do run. Lowest risk per byte on the whole list.

## Tier C — used, but at ~0%

4 assemblies, 0.62 MB IL → ~3.4 MB composite → **~0.9 MB APK**.

| Assembly | Methods used | IL code |
|---|---|---|
| `ActualChat.Api.Contracts` | 3 of 3,860 (0.08%) | 281 KB |
| `Xamarin.AndroidX.Core` | 51 of 4,016 (1.27%) | 198 KB |
| `ActualLab.Kvasar` | 1 of 847 (0.12%) | 88 KB |
| `OpenTelemetry` | 7 of 1,514 (0.46%) | 68 KB |

`ActualChat.Api.Contracts` is the largest single opportunity and also the least certain:
MemoryPack and MessagePack instantiate formatters over its types, and excluding it drops
it from the version bubble, so those cross-assembly instantiations may go with it. Measure
this one rather than assuming it.

## Expected total

| Scope | Composite | APK (at 5.5×) | APK (at the 9.3× average) |
|---|---|---|---|
| Tier A | ~10.3 MB | ~2.8 MB | ~4.7 MB |
| Tier A + B | ~16.9 MB | ~4.6 MB | ~7.8 MB |
| Tier A + B + C | ~20.3 MB | ~5.5 MB | ~9.3 MB |

## Deliberately not on the list

`OpusSharp.Core`, `Plugin.Maui.Audio` and `ActualChat.Core.Audio` all show zero use in the
trace, because the traced session never recorded audio. They are hot in real use and must
stay in the image.

More generally: the trace is one 22-second cold start. Tier A is safe because those
assemblies have no plausible runtime path at all. Tiers B and C rest on *rare*, not
*never*.

## Unrelated, but bigger

`PhoneNumbers.dll` is **11.0 MB of the 62 MB `linked/` set — 18%** — and only 813 KB of it
is IL code. The rest is embedded metadata. It is already R2R-excluded, so nothing here
applies to it, but it is the single largest assembly we ship by a wide margin. Trimming
its metadata or loading it lazily is a larger lever than this entire exercise.

## Re-running the analysis

1. `src/dotnet/App.Maui/Build-Tracing-AR.cmd` — publish and install a Release build with
   `IsTracingEnabled=true`.
2. `src/dotnet/App.Maui/Trace-A.cmd` — record the cold start over `dotnet-dsrouter`.
3. Parse the `.nettrace` with a `Microsoft.Diagnostics.Tracing.TraceEvent` reader:
   subscribe to `ClrTraceEventParser` `LoaderModuleLoad` and the `MethodLoadVerbose` /
   `MethodDCStopVerbose` families, key methods by `ModuleID`, and join against the
   `MethodDefinition` table of each assembly in `linked/` via `System.Reflection.Metadata`
   to get IL body sizes and total method counts.

Assemblies with zero reported methods are the candidate set; rank them by IL body bytes.
