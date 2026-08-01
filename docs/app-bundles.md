# App bundles

What ships inside the mobile and desktop app packages, how each part is kept out when it
isn't needed, and how to re-measure. Baselined on the `2.14.273` Android Release build
(`net11.0-android`, `android-arm64`) plus a 22.3 s cold-start trace taken on device;
Android is the largest package and the only one with a recorded trace, so it's the worked
example throughout. iOS, Mac Catalyst and Windows share the same managed-code and
web-asset pipelines.

See [Android-specific behavior](./android-specific.md) for how the traces are recorded,
[iOS-specific behavior](./ios-specific.md) for why exclusions behave differently on Apple
targets, and [Native AOT](./native-aot.md) for the other end of the codegen spectrum.

## The baseline

The APK measured 98.7 MB compressed (103.9 MB on disk; the AAB was 102.9 MB, and Play's
split delivery trims `res/` and languages further for real downloads).

| Category | Compressed | Share |
|---|---|---|
| Managed assemblies (`lib/arm64-v8a/libassembly-store.so`) | 46.7 MB | 47.3% |
| wwwroot images + video | 19.5 MB | 19.8% |
| Java/Kotlin (4× dex) | 9.7 MB | 9.8% |
| Other native libs (`.so`) | 8.7 MB | 8.8% |
| JS/CSS sourcemaps | 4.8 MB | 4.9% |
| Android `res/` + `resources.arsc` | 3.0 MB | 3.0% |
| wasm + ONNX models | 2.3 MB | 2.4% |
| Fonts | 2.0 MB | 2.0% |
| JS | 1.3 MB | 1.3% |
| CSS, misc | 0.7 MB | 0.7% |

Two things follow from that table and drive everything below. Managed code is half the
package, so the composite ReadyToRun image is the first place to look. But **web assets
plus sourcemaps were 24.7 MB — over a fifth** — and cutting those costs nothing at
runtime, which made them the better trade.

Landed so far: **~19.5 MB**, roughly 20% of the package.

| Change | Saving | Status |
|---|---|---|
| Landing images split three ways | 14.7 MB | landed |
| No production sourcemaps (net of `keepNames`) | 4.8 MB | landed |
| R2R exclusions | 2.6 MB | **blocked** — crashes crossgen2, see below |

## Managed code: composite ReadyToRun

Release is CoreCLR + **composite, full** R2R on *every* platform: crossgen2 compiles every
method of every input assembly into one composite image. Full rather than partial is a
deliberate trade — see the comment above `PublishReadyToRun` in `App.Maui.csproj` — but it
means we pay for native code we never execute.

### Selection criterion

Excluding an assembly does **not** remove it from the app. Its IL still ships; only the
precompiled native code goes away. Whatever runs anyway gets JIT'd on Android and Windows,
or interpreted on iOS and Mac Catalyst, so the cost of being wrong is a slower first call,
not a failure.

That makes the criterion **large, and cold in practice**. Concretely, an assembly earns a
`PublishReadyToRunExclude` when it executed no managed methods across a full cold start
*and* there's a structural reason it stays cold — a server concern, a dead platform path,
or crash-only. "Didn't happen to run in this trace" is not enough on its own.

One further constraint: the main exclusion item group is not TFM-conditioned, so entries
there apply on all four platforms while the evidence comes from an Android trace. Anything
platform-specific goes in the Android-only group instead.

### Blocked: crossgen2 crashes on a larger exclusion set

**Only `PhoneNumbers.dll` is active today.** Adding the nine assemblies below at once made
crossgen2 fail the Android Release publish:

```
error NETSDK1096: Optimizing assemblies for performance failed.
  at ILCompiler.ReadyToRunVisibilityRootProvider.AddCompilationRoots(IRootingServiceProvider)
     ReadyToRunVisibilityRootProvider.cs:line 39
  at ILCompiler.Compilation..ctor(...)  ReadyToRunCodegenCompilation.cs:line 68
```

The crash is in root enumeration, before any code generation: `AddCompilationRoots` walks
`_module.GetAllTypes()` for each input module and roots their methods. So it's a
*version-bubble* problem, not a codegen one — something still in the bubble refers to
something that left it, and the rooting path doesn't tolerate that.

Which of the nine is at fault is not yet known. The two framework assemblies are the prime
suspects: composite builds pass `--inputbubble`, which tells crossgen2 it can see every
input, and `System.Private.Xml` / `System.Data.Common` sit behind type-forwarding facades
(`System.Xml.ReaderWriter.dll`, `System.Data.dll`) that stay in the bubble. That would also
explain why `PhoneNumbers` — a leaf third-party assembly nothing forwards to — has always
been fine. **Untested.**

| Assembly | IL code | What it is | Scope |
|---|---|---|---|
| `System.Private.Xml` | 627 KB | `XmlReader` / `XmlWriter` / XML serialization | all platforms |
| `System.Data.Common` | 265 KB | ADO.NET base types | all platforms |
| `Newtonsoft.Json` | 258 KB | JSON.NET, transitive via `ActualLab.Core` and `RestEase` | all platforms |
| `RestEase` | 35 KB | REST client generator runtime | all platforms |
| `Mjml.Net` + `HtmlPerformanceKit` + `ActualChat.Mjml.Blazor` + `ActualChat.Users.Templates` | 222 KB | email templating | all platforms |
| `Sentry.Bindings.Android` | 410 KB | JNI bindings for the Sentry Android SDK | Android |

Together: 1.77 MB of IL → ~9.8 MB off the composite → ~2.6 MB off the APK.

### Adding an exclusion

Add one group at a time and publish between groups — a bad entry fails the build with the
NETSDK1096 above, and a batch tells you nothing about which entry caused it. Suggested
order, cheapest suspicion first: leaf third-party assemblies (`Newtonsoft.Json`, `RestEase`,
`Mjml.Net`, `HtmlPerformanceKit`), then our own (`ActualChat.Mjml.Blazor`,
`ActualChat.Users.Templates`), then `Sentry.Bindings.Android`, then the two framework
assemblies last.

When it works, the tell is that the excluded assembly is absent from
`artifacts/obj/App.Maui/release_net11.0-android_android-arm64/R2R/` and gets packaged
straight from `linked/` instead.

### Size model

Three measured ratios turn "IL bytes excluded" into "MB off the APK".

| Quantity | Value |
|---|---|
| Composite image `ActualChat.r2r.dll` | 107.4 MiB |
| IL method bodies fed to crossgen2 (228 assemblies, minus `PhoneNumbers`) | 11.5 MiB |
| **Average native/IL expansion** | **9.3×** |
| Measured native/IL for non-generic library and binding assemblies | 3.9–6.2× |
| Composite → APK, from the partial-vs-full R2R experiment (Δ100.6 MB composite → Δ27.1 MB APK) | **3.7 : 1** |

The APK ratio is well below 1:1 because Android stores assemblies LZ4-compressed inside
`libassembly-store.so` (57 MB raw → 46.7 MB after the APK's own deflate).

Per-assembly expansion varies enormously with generics — `MemoryPack.Core` measures 38× and
`ActualLab.Interception` 22×, because instantiations multiply, while flat JNI bindings like
`Xamarin.AndroidX.Core` measure 4.9×. Estimates below use **5.5×**, conservative for the
mostly non-generic assemblies involved.

Rule of thumb: **1 MB of excluded IL ≈ 5.5 MB off the composite ≈ 1.5 MB off the APK.**

### Candidates considered and not taken

The trace found **148 of 228 shipped assemblies executed no managed code at all.** Beyond
what's applied above, three groups remain on the table.

**Cold, no hot path** (~0.6 MB IL): `Microsoft.CSharp` (the `dynamic` binder),
`Microsoft.ML.OnnxRuntime` (Android uses LiteRt, so this path is dead),
`System.ComponentModel.TypeConverter`, `System.Runtime.Numerics`,
`Microsoft.Maui.Controls.Xaml` (BlazorWebView app, no XAML at runtime),
`K4os.Compression.LZ4`, `System.Reflection.Metadata` + `Sentry.Android.AssemblyReader`
(crash-time only), `Microsoft.AspNetCore.Components.WebAssembly` (dead in MAUI),
`System.Diagnostics.Process`, `System.Numerics.Tensors`, `System.Private.Xml.Linq`,
`Xamarin.Kotlin.StdLib`, `Xamarin.KotlinX.Serialization.Core.Jvm`.

**Cold at startup, exercised later by native UI** (1.20 MB IL → ~1.8 MB APK):
`Xamarin.Google.Android.Material` (242 KB), `Xamarin.AndroidX.AppCompat` (131 KB),
`Xamarin.AndroidX.RecyclerView` (90 KB), `Xamarin.Google.Crypto.Tink.Android` (84 KB),
`Xamarin.GooglePlayServices.Base` (49 KB), `Xamarin.AndroidX.Fragment` (48 KB),
`Xamarin.AndroidX.Media` (43 KB), plus nine smaller AndroidX / Play Services bindings.
Thin JNI wrappers reached by permission dialogs, the file picker, the share sheet,
notifications — cheap to JIT when they run.

**Used, but at ~0%** (0.62 MB IL → ~0.9 MB APK):

| Assembly | Methods used | IL code |
|---|---|---|
| `ActualChat.Api.Contracts` | 3 of 3,860 (0.08%) | 281 KB |
| `Xamarin.AndroidX.Core` | 51 of 4,016 (1.27%) | 198 KB |
| `ActualLab.Kvasar` | 1 of 847 (0.12%) | 88 KB |
| `OpenTelemetry` | 7 of 1,514 (0.46%) | 68 KB |

`ActualChat.Api.Contracts` is the largest single opportunity and the least certain:
MemoryPack and MessagePack instantiate formatters over its types, and excluding it drops it
from the version bubble, so those cross-assembly instantiations may go with it. Measure
rather than assume.

`OpusSharp.Core`, `Plugin.Maui.Audio` and `ActualChat.Core.Audio` also show zero use — only
because the traced session never recorded audio. They are hot in real use and must stay.

### Not an R2R problem, but the largest single assembly

`PhoneNumbers.dll` is **11.0 MB of the 62 MB `linked/` set — 18%** — and only 813 KB of
that is IL code. The rest is embedded metadata. It's already R2R-excluded, so nothing above
applies to it, but trimming its metadata or loading it lazily is a bigger lever than the
whole exclusion list.

## Web assets: three folders under `src/nodejs/images`

`src/nodejs/images` is the source of truth for every image in the web bundle and the app
packages — the per-app `wwwroot/dist/images` trees are build output and gitignored.
`build.mjs` copies the tree wholesale, so a file's **folder decides how far it travels**:

| Folder | Web bundle | App packages | Meaning |
|---|---|---|---|
| `images/**` (default) | yes | yes | used by the app UI |
| `images/webonly/` | yes | **no** | only reachable on the web |
| `images/unused/` | **no** | **no** | nothing references it; archived, not deleted |

Two independent mechanisms enforce that, and it's worth knowing which is which:

- **`unused/` never leaves the repo.** `copyAssets()` in `build.mjs` filters it out of the
  `fs.cp` into `dist`, so it reaches neither the web bundle nor any app package. Nothing
  else has to know about it.
- **`webonly/` is published but unpacked.** It lands in `dist` normally and is served on
  the web; `App.Maui.csproj` drops `wwwroot\dist\images\webonly\**` from `Content` and
  `None`, which is what keeps it out of every app package — Android, iOS, Mac Catalyst and
  Windows alike.

**Adding an asset:** put it in `images/` and reference it as
`/dist/images/<folder>/<name>`. If it's only ever rendered by a page the apps don't show,
put it in `webonly/` instead. Paths are literal strings in `.razor` / `.css`, so the folder
is part of the URL — moving a file between folders means updating its references.

### Why this mattered

`images/landing` alone was 16.7 MB compressed — 17% of the whole APK — while the MAUI
landing (`LandingForApp`) renders only `LandingPage1`, the header, the left menu and the
video modal. `LandingForWeb` renders pages 2 and 4–7, the last page, `PremiumFeaturesModal`
and the download links on top of that.

Mapping every file to its referencing component split it cleanly:

| Bucket | Files | APK |
|---|---|---|
| `unused/` — referenced nowhere, plus the `LandingPage3` set (commented out in `LandingForWeb` too) | 71 | 6.92 MB |
| `webonly/` — pages 2 and 4–7, last, premium | 42 | 4.67 MB |
| `landing-tutorial.webm` → `webonly/` | 1 | 3.09 MB |
| **Total** | **114** | **14.67 MB** |

`images/landing` went from ~24 MB to 2.2 MB on disk; the 16 files left are what the app
actually renders.

The tutorial video is worth calling out as the failure mode this structure exists to
prevent: `landing-tutorial.webm` and `.mp4` existed **byte-identical in both `landing/` and
`webonly/`**, and the markup pointed at the `landing/` copies — so the `webonly` exclusion
was silently doing nothing for 4.9 MB. The webm now lives only in `webonly/` (every WebView
we ship plays the H.264 mp4, and `landing.ts` appends both `<source>` elements so the app
falls back automatically), the mp4 only in `landing/`.

A caveat on the classification: it was name-based across ~4,000 source files, matching full
filenames plus the one dynamic pattern in use (`page-4-image-@(item.N).svg`). An asset
referenced only through a fully constructed string could have been misfiled. That is
exactly why they moved to `unused/` rather than being deleted — restoring one is a
`git mv`.

## Sourcemaps and `keepNames`

Production ships **no `.map` files**. They were 20.5 MB raw / 4.8 MB compressed in the APK
(`bundle.js.map` alone was 10.7 MB) and nothing reads them at runtime.

Dropping them alone would have cost readable stack traces, because esbuild's `minify`
renames identifiers: before this change `bundle.js` contained `class kt extends`, and a
frame would have read `kd.processFrame`. So `keepNames: true` goes with it — esbuild emits
a `__name()` tag per function and class that sets `Function.name` at runtime, which is what
V8 reads when building a stack trace. The production bundle carries 3,689 such tags,
covering app classes (`VirtualList`, `AudioRecorder`, `RpcPeer`, `AudioPlayer`) as well as
vendor code.

The trade: **+65 KB on `bundle.js` (+2.7%) to remove 4.8 MB.** What's lost is line and
column numbers — frames name their function but point into minified output.

If line numbers are ever needed, the path back is `sourcemap: 'external'` in production
(emits the `.map` without a `//# sourceMappingURL=` comment, so no 404 when devtools are
open) plus a `sentry-cli sourcemaps upload` step. Sentry then symbolicates server-side and
the maps still never enter the package. There is no such upload step today.

`App.Maui.csproj` also removes `wwwroot\dist\**\*.map` from Release content. That's belt
and braces: `npm run build:Release` emits none, but the usual local flow builds a Release
app package on top of a debug frontend build.

## Still on the table

| Candidate | Saving | Note |
|---|---|---|
| Ship only `woff2` fonts | 1.8 MB | `ttf` 624 KB + `otf` 578 KB + `svg` 329 KB + `eot` 165 KB + `woff` 165 KB, against 476 KB of woff2. woff2 works in WKWebView (iOS 10+), Chromium (Android WebView, WebView2) and every browser since 2016 — well below our floors of iOS 16.4 / API 28 / Windows 10 17763. Needs the `@font-face` chains narrowed in the TT Commons Pro CSS and in the svgtofont config behind `npm run font`. |
| Drop the ONNX-wasm VAD on Android | 1.8 MB | `vad_batched.ort` (1.06 MB) + `ort-wasm-simd.wasm` (750 KB). Android resolves `VoiceActivityDetector` to `TfLiteVoiceActivityDetector`, which uses `res/raw/vad_batched_fp16.tflite` and `libLiteRt.so`; the `.ort` is loaded only under `#if WINDOWS` in `MauiAppModule`. Confirm the JS recorder never runs on Android before cutting. |
| Trim unused AndroidX / Play Services packages | up to 9.7 MB | The dex payload. Real risk — this removes Java, not just precompiled code. |

## Re-measuring

**Package breakdown.** Open the APK as a zip and group entries by `CompressedLength` —
that's what the table at the top is. Don't use uncompressed sizes: the assembly store is
LZ4-compressed before the APK's own deflate, so raw numbers overstate its share.

**Which assemblies are cold.**

1. `src/dotnet/App.Maui/Build-Tracing-AR.cmd` — publish and install a Release build with
   `IsTracingEnabled=true`.
2. `src/dotnet/App.Maui/Trace-A.cmd` — record the cold start over `dotnet-dsrouter` with
   `Microsoft-Windows-DotNETRuntime:0x1F000080018:5` (Loader + JIT keywords, plus the
   end-of-session rundown).
3. Parse the `.nettrace` with a `Microsoft.Diagnostics.Tracing.TraceEvent` reader:
   subscribe to `ClrTraceEventParser`'s `LoaderModuleLoad` and the `MethodLoadVerbose` /
   `MethodDCStopVerbose` families, key methods by `ModuleID`, and join against the
   `MethodDefinition` table of each assembly in
   `artifacts/obj/App.Maui/release_net11.0-android_android-arm64/linked` via
   `System.Reflection.Metadata` for IL body sizes and total method counts.

Only methods that acquired native code are reported, which is to say only methods that ran.
Assemblies with zero reported methods are the candidate set; rank them by IL body bytes.

**Which assets are unreferenced.** Walk `src/nodejs/images`, and for each file search the
`.razor` / `.cs` / `.ts` / `.css` sources for its full filename. Match on the name *with*
extension — a stem-only match produces false negatives that are easy to miss
(`synchronous.svg` matches every `asynchronous`, `android.svg` matches every `Android`
identifier). Handle dynamic `src` patterns explicitly.
