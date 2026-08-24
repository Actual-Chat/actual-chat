# Startup profiling and ReadyToRun settings

How we record what the app runs at startup, turn it into a `.mibc` profile, and use that
profile to compile a much smaller ReadyToRun image. Covers Android and Windows; iOS is
deliberately excluded and [says why](#ios-stays-full).

Everything here is measured on a Samsung `SM-S948U1` (8 cores) and a Windows x64 desktop,
2026-08-06, `net11.0` preview 6.

## Current settings

All in `src/dotnet/App.Maui/App.Maui.csproj`.

| Setting | Value | Effect |
|---|---|---|
| `UsePartialR2R` | `true`, forced `false` on `-ios` / `-maccatalyst` | crossgen2 compiles only what the profile lists |
| `PublishReadyToRunPgoFiles` | `_Profiling/merged.mibc` | the profile, on both Android and Windows |
| `System.Runtime.TieredCompilation.CallCountingDelayMs` | `15000` | no tier-1 promotion during startup |
| `--mapcsv` | always, Release | `ActualChat.r2r.map.csv` next to the composite |

Results against the previous full-R2R configuration:

| | Android | Windows |
|---|---|---|
| Composite image | 111.4 MB → **18.6 MB** | 234 MB → **15.5 MB** |
| APK / publish | 82.9 MB → **56.0 MB** | — |
| Methods compiled | 362,596 → 35,821 | → 34,020 |
| Methods jitted at startup | 1,869 → **123** | 2,553 → **446** |

**Startup time did not change.** On Android, `ChatListLoaded` was 445 ms (min of 5) before
and 458 ms after — inside run-to-run noise, as were `MarkRendered` and `am start` TotalTime.
The wins here are image size and CPU/battery, not launch latency. Say so when reporting it.

## Recording a profile

Three scripts, all in `scripts/`:

| Script | Purpose |
|---|---|
| `Record-AndroidStartupProfiles.ps1` | capture `.nettrace` on device |
| `Record-WindowsStartupProfiles.ps1` | capture `.nettrace` on this machine |
| `New-StartupMibc.ps1` | `.nettrace` → `.mibc`, and rebuild `merged.mibc` |

Or run **`/mibc-update [android|windows|all]`** (default `all`), which does all of it.
`merged.mibc` is rebuilt either way, since that's the file the builds consume.

### Android

```bash
# builds + installs the tracing APK, then records 3 cold starts
pwsh scripts/Record-AndroidStartupProfiles.ps1 -Runs 3 -Mode Methods -Build
pwsh scripts/New-StartupMibc.ps1 -Platform Android
```

`-Build` matters: without `IsTracingEnabled=true` the app has no diagnostic port and
`dotnet-trace` never finds it. A tracing build also **suspends at launch until a tracer
attaches** — if it looks hung, that is the reason, not a bug. Use `-Mode Attach` to drive
such a build by hand (signing in, reaching a screen the cold starts never touch); that
session is recorded with the same providers, so it is usable profile data rather than
something to throw away.

### Windows

```bash
dotnet publish src\dotnet\App.Maui\App.Maui.csproj -f:net11.0-windows10.0.22621.0 -c:Release -p:WindowsPackageType=None
pwsh scripts/Record-WindowsStartupProfiles.ps1 -Runs 2 -Mode Methods
pwsh scripts/New-StartupMibc.ps1 -Platform Windows
```

No dsrouter and no special build: `dotnet-trace collect -- <exe>` launches the app itself
and suspends it until the session is live, so the trace covers process startup against the
build that is already published.

### iOS — the inverse recording

iOS records the **opposite** list, and for the opposite reason. On Android and Windows a
profile exists to *shrink* the image: a miss costs one JIT compile, so partial mode trades
size for a little cold JIT. On iOS there is no JIT and nothing to shrink toward — a miss is
interpreted for the life of the process — so what is worth recording here is not "what to
compile" but "what we failed to compile": the interpreted set.

That set is then fed back in. iOS Release supplies **three** `.mibc` files to crossgen2 —
`ios-interactive.mibc`, `merged.mibc` and `aothelper.mibc` — to compile away what would
otherwise stay interpreted, not to shrink anything: it costs ~1MB of bundle and removes 95%
of the interpreted methods (2835 → 140, measured on device 2026-08-09). See
[ios-specific.md → The three profiles iOS Release feeds crossgen2](./ios-specific.md#the-three-profiles-ios-release-feeds-crossgen2).

The mechanism is the same `-Mode Jit` mask (`0x1C000080018`). There is no JIT on iOS, so
every method it reports had to be built by the interpreter at runtime.

Full recipe, traps and first results:
[ios-specific.md → Measuring what runs interpreted](./ios-specific.md#measuring-what-runs-interpreted).
Short version — build with `-p:EnableDiagnostics=true -p:DiagnosticSuspend=true` (the macios
SDK bakes `DOTNET_DiagnosticPorts` into the bundle), **launch the app first**, then
`dotnet-dsrouter ios`, then `dotnet-trace`, then `dotnet-pgo create-mibc` as usual.

First run (2026-08-09, launch only) found **2,835 interpreted methods**, 94% of them generic
instantiations and 882 instantiated purely over value types — the shapes a full-mode image
cannot enumerate statically.

### How many runs

**Two or three.** Cold starts barely vary: going from 3 merged traces to 7 added 210 methods
out of 26,971. Extra runs are wasted wall-clock, and each 10 s capture is ~420 MB.

Ten seconds is enough — the app is up in ~1 s and the `*ServiceStarter` work is done by ~6 s.

### Merging

`New-StartupMibc.ps1` converts each trace separately and merges them, because a merge is a
union: sessions that exercised different parts of the app only ever add coverage. It then
rebuilds `merged.mibc` from `android.mibc` + `windows.mibc`. That last step runs
unconditionally, so re-recording one platform can never leave the union stale.

Both platforms compile against `merged.mibc`, not their own profile. It costs little and
buys real coverage — Windows on the Android profile alone resolved ~47% of it and compiled
23,930 methods; on the union it compiles 34,020 for 2.7 MB more image.

## Reading the results

`ActualChat.r2r.map.csv` (next to the composite) lists every method crossgen2 compiled,
generic instantiations included. It is the only way to tell a JIT event that means *"no
native code existed"* from one that means *"tier-1 promotion"*. As of
2026-08-09 it is emitted for iOS Release builds too, where it serves the opposite
purpose: separating "never compiled, interpreted forever" from noise.

To measure what a build still jits, record with `-Mode Jit` and run it through
`New-StartupMibc.ps1`: the resulting method list **is** the set of methods startup had to
jit.

Two traps when cross-referencing the map against a `.mibc`:

- The map embeds assembly names **inside** generic type arguments
  (`RadixHeapSet_1<ActualLab_Core_ActualLab_Time_GenericTimeoutSlot>`) and the mibc does not
  (``RadixHeapSet`1<ActualLab.Time.GenericTimeoutSlot>``). Naive name matching therefore
  misfiles generic instantiations as "not in image". This produced a 6× wrong answer once.
- A truncated `.nettrace` fails conversion with *"Read past end of stream"*. On Android this
  is almost always the device dropping off ADB mid-capture — `adb kill-server && adb
  start-server` and re-record. Watch for suspiciously small captures (13 MB where the others
  are 420 MB).

## What the experiments established

### The profile roots in a full build too — it just doesn't order it

**Corrected 2026-08-09.** This section previously said crossgen2 ignores the profile outside
partial mode. That is wrong, and it mattered: it is the reason iOS shipped without one.

`ReadyToRunProfilingRootProvider` is added **unconditionally** — `crossgen2/Program.cs:576`,
outside the `if (!partial)` that guards the visibility/XML/library root providers. Partial
mode does not *enable* profile rooting; it removes everything else, leaving only the profile.

Measured on `net11.0-ios` / `ios-arm64` / Release, feeding a device-recorded `.mibc` to an
otherwise unchanged full build:

| | baseline | with profile |
|---|---|---|
| compiled methods | 324,076 | **327,767** (+3,691) |
| `AsyncStateMachineBox` instantiations | 19,299 | **20,823 (+1,524)** |
| `MessagePackByteSerializer_1<ParticipationKind>` | 0 | 4 |

That `+1,524` is exactly the number of interpreted `AsyncTaskMethodBuilder` methods the
device trace recorded — not approximately, the same number. Every instantiation named in the
profile landed in the image.

What *is* still true, and is probably what the original claim conflated:

- A trace-derived `.mibc` carries **no block counts, edge counts or class histograms**
  (`dotnet-pgo dump` reports 0 of each). It is a method list. It roots; it cannot inform
  block layout or devirtualization.
- Hot-first **ordering** would need `--method-layout`, which defaults to `DefaultSort`
  (layout disabled) and which neither we nor the SDK ever pass. So the profile changes
  *which* methods are compiled, never their order.

The original evidence — ``RadixHeapSet`1<GenericTimeoutSlot>`` named in the profile yet
"still being jitted at runtime" on Android — does not show what it appeared to. Android has
a JIT and tiering, and this same document measures that ~1,200 of 1,675 methods jitted
during a cold start are **tier-1 promotions, not first compiles**. A method present in the
R2R image is jitted again on promotion. The observation is consistent with rooting having
worked all along.

**Partial mode is still not merely "full minus some code".** In partial the profile is the
*only* root source, so it decides the whole image rather than adding to it. Its real value in
both modes is that it names exact generic instantiations — including value-type ones, which
have no shared `__Canon` representation and which a full build silently skips when it cannot
enumerate them statically. That is why the partial image contained
`ArrayPoolBuffer_1<UInt8>__GetMemory` and `RadixHeapSet_1<GenericTimeoutSlot>__ExtractMinSet`
and the full image did not — but as the table above shows, a full build fed the same names
gets them too.

### Most startup "JIT" is tier-1 rejit

Tiering counts calls into R2R code too, so a composite-R2R startup spends real time
recompiling methods that already had native code. Of 1,675 methods jitted during an Android
cold start, ~1,200 were promotions rather than first compiles.

Suppressing promotion for the startup window (`CallCountingDelayMs=15000`) cut the count
~15×. Measured on Android, partial image, 3-run unions:

| Config | Methods jitted |
|---|---|
| default tiering | 1,869 |
| `TieredCompilation_CallCounting=0` | 2,021 |
| `TieredCompilation=0` | 170 |
| `TC_CallCountingDelayMs=3600000` | 246 |
| **`TC_CallCountingDelayMs=15000`** | **119** |

**`TieredCompilation_CallCounting=0` is the trap.** It does not mean "never promote" — it
disables *counting*, so everything is promoted as soon as the delay expires. Measured
promotions went 1,200 → 1,416, the wrong direction.

15 s is chosen so tier 1 still happens normally afterwards; a long session keeps its
optimized code.

### The setting travels with the app

`System.Runtime.TieredCompilation.CallCountingDelayMs` works as a **runtimeconfig** property,
not just as `DOTNET_TC_CallCountingDelayMs`. Confirmed with the env var cleared: Windows
2,553 → 446 jitted, Android 123 (against 119 for the env var). It is set via
`RuntimeHostConfigurationOption`, so it ships in `runtimeconfig.json` and applies however the
app is launched — which on Windows is the difference between working and not, since there is
no `android-env.txt` equivalent there.

An env var still overrides it, which is how to A/B without rebuilding.

## Traps

**Toggling `UsePartialR2R` does not trigger a rebuild of the image.** `_CreateR2RImages`
takes its `Inputs` from the project files, the compile list and the pgo files — *not* from
the crossgen2 argument properties. Flipping the switch alone leaves the previous composite in
place and the build quietly ships the image you were trying to replace. Delete
`artifacts/obj/App.Maui/<config>/R2R/` when toggling. This produced two bogus "full" builds
before it was noticed.

**`_MauiPublishReadyToRunPartial` is real and load-bearing on Android.**
`Microsoft.Maui.Controls.targets` appends `--partial` unless it is `false`, so MAUI 11
already defaults Android CoreCLR Release to partial. It lives in the
`microsoft.maui.controls.build.tasks` **NuGet package**, not under `dotnet/packs` — grep the
package cache before concluding a MAUI property is unused. The same `PropertyGroup` is gated
on `TargetPlatformIdentifier == 'android'`, so Windows has to pass `--partial` itself.

**A profile-poor build hides tiering effects.** The first Windows tiering comparison showed
knob and env var as identical (8,611 vs 8,784) — because that build was compiled with
`android.mibc` at ~47% coverage, so its startup JIT was nearly all *cold* JIT with no rejit
to suppress. Fix coverage first, then measure tiering.

**Direction matters between platforms, but less than it used to.** A Windows-recorded profile
fed to `android-arm64` crossgen2 used to crash on `MemoryPack` generic instantiations it
could not re-resolve. That no longer reproduces — Android builds cleanly on `merged.mibc`,
which contains the Windows recording. The note about it in `App.Maui.csproj` is kept as
history; if it returns, split the profiles again.

## iOS stays full

Android and Windows can afford partial because a miss costs one JIT compile. **iOS has no
JIT** — anything missing from the image is interpreted for the life of the process, so a miss
is permanent and the size win is not worth it. `UsePartialR2R` is forced `false` there.

Partial mode was measured on device anyway (2026-08-01: 339 MB → 141 MB bundle, startup
unchanged) and then **opening a chat hung**. See
[ios-specific.md → Shrinking the R2R image, and why we don't](./ios-specific.md#shrinking-the-r2r-image-and-why-we-dont).

What iOS records instead is the interpreted set — see *Recording a profile → iOS* above
and [ios-specific.md → Measuring what runs interpreted](./ios-specific.md#measuring-what-runs-interpreted).

## Still open

- **Notification launch is a different startup path** and no profile covers it. The app can
  be started by FCM into a different execution path than a launcher tap, so those methods are
  absent from every recording here.
- **No Windows startup-time numbers.** Android has `ChatListLoaded` / `MarkRendered` figures
  from `scripts/Measure-AndroidStartup.ps1`; there is no equivalent script for Windows yet.
- **Full-vs-partial startup time was never compared end to end.** The full Android build was
  measured statically and never installed, so there is no `ChatListLoaded` for it.
