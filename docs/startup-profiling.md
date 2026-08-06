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
native code existed"* from one that means *"tier-1 promotion"*.

To measure what a build still jits, record with `-Mode Jit` and run it through
`New-StartupMibc.ps1`: the resulting method list **is** the set of methods startup had to
jit.

Two traps when cross-referencing the map against a `.mibc`:

- The map embeds assembly names **inside** generic type arguments
  (`RadixHeapSet_1<ActualLab_Core_ActualLab_Time_GenericTimeoutSlot>`) and the mibc does not
  (`RadixHeapSet\`1<ActualLab.Time.GenericTimeoutSlot>`). Naive name matching therefore
  misfiles generic instantiations as "not in image". This produced a 6× wrong answer once.
- A truncated `.nettrace` fails conversion with *"Read past end of stream"*. On Android this
  is almost always the device dropping off ADB mid-capture — `adb kill-server && adb
  start-server` and re-record. Watch for suspiciously small captures (13 MB where the others
  are 420 MB).

## What the experiments established

### The profile does nothing in a full build

`dotnet-pgo dump` reports **0 methods with block counts, edge counts or class histograms** —
a trace-derived `.mibc` is a method *list*, nothing more. In non-partial mode crossgen2 does
not root from it, and hot-first ordering would need `--method-layout`, which defaults to
`DefaultSort` (layout disabled) and which neither we nor the SDK ever pass.

Verified on device rather than inferred: `RadixHeapSet\`1<GenericTimeoutSlot>` appears 8
times in the profile that built the shipping full image, and 5 of those methods were still
being jitted at runtime.

**So partial mode is not merely "full minus some code".** crossgen2 roots from the profile
only in partial mode, and the profile names exact generic instantiations — including
value-type ones, which have no shared `__Canon` representation and which a full build
silently skips if it cannot enumerate them statically. The partial image contains
`ArrayPoolBuffer_1<UInt8>__GetMemory` and `RadixHeapSet_1<GenericTimeoutSlot>__ExtractMinSet`;
the full image did not.

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

## Still open

- **Notification launch is a different startup path** and no profile covers it. The app can
  be started by FCM into a different execution path than a launcher tap, so those methods are
  absent from every recording here.
- **No Windows startup-time numbers.** Android has `ChatListLoaded` / `MarkRendered` figures
  from `scripts/Measure-AndroidStartup.ps1`; there is no equivalent script for Windows yet.
- **Full-vs-partial startup time was never compared end to end.** The full Android build was
  measured statically and never installed, so there is no `ChatListLoaded` for it.
