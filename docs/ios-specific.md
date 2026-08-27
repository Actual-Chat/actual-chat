# iOS-specific behavior

Things that behave differently on iOS than on every other platform we ship, and the
reasons why. Mac Catalyst shares most of them — both are Apple targets with the same
runtime constraints.

## Policy: every platform keeps a JIT or an interpreter

**We are not yet ready to ship NativeAOT-style production builds** — meaning no JIT
*and* no interpreter. We are close, and it's where we want to end up, but until every
reflection and serialization path is covered statically, each platform must keep one of
the two available. Turning the safety net off early doesn't make us AOT-ready; it just
moves the failures in front of users.

In practice: Android and Windows have a JIT. **iOS has the CoreCLR interpreter** — always,
whatever `UseInterpreter` says; see *The interpreter is always on* below. So the policy
holds on every shipping platform, and it always did: the safety net for code R2R didn't
precompile has never actually been absent on iOS. Native AOT builds have neither by
definition; that's why they're not production yet, and why
[CodeKeeper](./native-aot.md) coverage is the work that gets us there.

## The interpreter is always on — `UseInterpreter` only gates dynamic code

One setting in `App.Maui.csproj`, and no R2R exclusion:

```xml
<UseInterpreter>false</UseInterpreter>
```

**`UseInterpreter` does not turn the CoreCLR interpreter on or off on Apple targets.** The
interpreter is always there, and it is the *only* code generator on iOS. All the property
does is gate `DynamicCodeSupport`, i.e. `RuntimeFeature.IsDynamicCodeSupported`. The name
misleads, which is why this cost us a night.

Evidence, all from the shipped `ios-arm64` build:

- `libcoreclr` contains **no JIT** — zero `CILJit` / `Compiler::compCompile` symbols — but
  does contain `InterpExecMethod`, `ExecuteInterpretedMethod`, `AllocateInterpreterPrecode`.
  The same binary ships either way: it comes from the runtime pack, not from our build.
- `codeman.cpp` hard-wires it whenever no JIT is compiled in, which is exactly iOS:
  ```cpp
  #if defined(FEATURE_DYNAMIC_CODE_COMPILED)
      bool interpreterOnly = CLRConfig::GetConfigValue(CLRConfig::EXTERNAL_InterpMode) == 3;
  #else
      bool interpreterOnly = true;      // <- iOS: no JIT linked in
  #endif
  ```
- The macios SDK has no CoreCLR-interpreter knob at all. `UseInterpreter` is an alias for
  `MtouchInterpreter`, which the linker discards outright when the runtime isn't MonoVM —
  see [Which `Mtouch*` properties still do something](#which-mtouch-properties-still-do-something-under-coreclr).

**The decisive test**, run on device 2026-07-31: build with `UseInterpreter=false` *and*
`Microsoft.AspNetCore.Components.dll` excluded from R2R. Blazor then has zero precompiled
bodies and ships as plain IL, and there is no JIT — so if Blazor renders at all, only the
interpreter can be running it. It rendered, and did not crash. Rebuild that combination if
anyone doubts this; it is a one-line change to the exclusion plus `-p:UseInterpreter=false`.

### What the interpreter is actually for

R2R is not Native AOT — there is no whole-program instantiation closure — so generic
coverage splits:

- **Reference-type `T`: always covered, no codegen needed.** crossgen2 emits one canonical
  body per generic definition and every reference-type instantiation shares it through the
  generic dictionary. `MarkupViewBase_1<System___Canon>__set_Markup` is in the image; so is
  every other `<__Canon>` form. Instantiations built at runtime over reference types reuse
  these, so `MakeGenericType` over a class is safe.
- **Value-type `T`: one body per instantiation, only where crossgen2 could see it.** Structs
  have no shared representation. The image contains e.g.
  `ReflectionEmitCachingMemberAccessor__CreatePropertySetter<Int32>`, `<Double>`, `<Size2D>`.
  Anything it couldn't enumerate statically has **no body**.

That second bullet is the gap the interpreter fills, and why `IsDynamicCodeSupported=false`
is not the same as "nothing can generate code". Libraries stop *choosing* dynamic paths; the
runtime keeps its ability to execute IL that R2R didn't precompile.

### What dotnet/runtime#130840 actually is

The fault needs **both** halves, which is why it read as "the interpreter crashes":

1. a library builds an open-instance delegate over a property setter declared on a
   shared-generic type (`Foo<__Canon>`), **and**
2. the caller invoking it is R2R-compiled.

`IsDynamicCodeSupported=false` removes the first half — libraries stay on their static paths
and never build the delegate. That is why `UseInterpreter=false` fixes it while leaving the
interpreter running, and why full R2R is fine again. The old workaround attacked the second
half instead, by keeping `Microsoft.AspNetCore.Components.dll` out of the R2R image so the
caller was interpreted too; that cost ~17MB and an interpreted parameter path on every
render, and is no longer needed.

Diagnosis signature, should it ever return — consecutive `0x8BADF00D` watchdog kills,
main thread:

```
InterpExecMethod / ExecuteInterpretedMethod
  CID_VirtualOpenDelegateDispatchWorker      <- native fault (PAC failure)
    EEPolicy::LogManagedCallstackForSignal
      CallStackLogger::PrintStackTrace
        TypeString::AppendMethodImpl         <- >5s symbolicating -> process killed
```

[r]: https://github.com/dotnet/runtime/issues/130840

The symptom is deceptive: the app paints its UI and then ignores touches. That's not our
code hanging — the main thread is stuck in CoreCLR's *own* fatal-error handler,
symbolicating a managed stack on device, until the watchdog fires. Pull the reports with
`idevicecrashreport -u <udid> -k <dir>`; `faultingThread` points at thread 0.

Two callers were seen reaching it while dynamic code was enabled:

- **Ours**: `MetadataExt` builds `Action<IHasMetadata, MetadataBag>` from an interface
  property setter via Fusion's `GetSetter`. Fixed upstream in **ActualLab.Fusion 14.2.50**.
- **Blazor's**: `ComponentProperties.SetProperties` builds an open-instance
  `Action<TTarget,TValue>` for every component parameter setter, so it sits on the path of
  every render. Seen first for `VirtualList<TItem>`, then — after that one component was
  worked around — for `MarkupViewBase<TMarkup>.set_Markup`, reached from
  `MarkupView.BuildRenderTree`.

Six generic types here declare parameters, and all of them would be exposed if dynamic code
were re-enabled — not the three an earlier pass counted: `VirtualList<TItem>`,
`MarkupViewBase<TMarkup>`, `ComputedMarkupViewBase<TMarkup, TState>`, `Menu<THub>`,
`ComputedMenuBase<THub, TState>`, `Step<THub, TModel>`.

### Runtime knobs that look like a way out, and aren't

- **`DOTNET_Interpreter=<method filter>`** cannot force an R2R-compiled method to be
  interpreted. `MethodDesc::GetPrecompiledCode` returns R2R code and never reaches the
  interpreter's `compileMethod`, where the filter is consulted. Corroborated by the mode
  semantics below: the modes that interpret everything have to switch R2R off wholesale.
- **`DOTNET_InterpMode`** (`0`–`3`; anything else is `NO_WAY("Unsupported value for
  DOTNET_InterpMode")`): `0` default, interpreter only for methods named by
  `DOTNET_Interpreter`; `1` everything except R2R code and CoreLib; `2` all but intrinsics,
  implies `ReadyToRun=0`; `3` interpreter-only, implies `ReadyToRun=0`. Every mode makes
  *more* code interpreted, never less — none of them removes an R2R→interpreted boundary
  while keeping R2R.

Both are reachable in a shipped build if ever needed: see
[Setting process environment variables in the app bundle](#setting-process-environment-variables-in-the-app-bundle).

## Shrinking the R2R image, and why we don't

**Android and Windows now ship partial R2R; iOS deliberately does not.** There a miss costs
one JIT compile, here it is interpreted for the life of the process — so the same size win
buys a permanent slowdown. `UsePartialR2R` is forced `false` for `-ios` / `-maccatalyst` in
`App.Maui.csproj`. The profile pipeline and the settings both platforms use are described in
[startup-profiling.md](./startup-profiling.md); this section is why none of it applies here.

Two ways to precompile less and interpret more were built and measured on device
(2026-08-01, `net11.0-ios` / `ios-arm64` / Release, dev-signed, clean builds). **Neither
shipped**, but the mechanics are worth recording because the surrounding docs got them
wrong, and because the failure mode is not the one you'd expect.

| | baseline | crossgen2 partial | only `ActualChat*`/`ActualLab*` |
|---|---|---|---|
| App bundle | 339 MB | **141 MB** | 207 MB |
| Build | 115 s | 63 s | 71 s |
| `__managedcode` in the main composite | 59.7 MB | 7.2 MB (**12%**) | 29.0 MB |
| Assemblies compiled | 183 / 184 | 183 / 184 | **23 / 184** |
| Launch → WebView | 2.1 s | 1.3 s | not run |
| Launch → chat list rendered | ~2.3 s | **2.5 s** | not run |

**Partial mode is reachable on Apple targets.** MAUI's `_MauiPublishReadyToRunPartial`
is real — `Microsoft.Maui.Controls.targets` in the `microsoft.maui.controls.build.tasks`
package (not under `dotnet/packs`, which is why grepping there finds nothing) appends
`--partial` unless it is `false`. But that `PropertyGroup` is gated on
`'$(TargetPlatformIdentifier)' == 'android'`, so on Apple targets the property is inert
and nothing turns partial on for you. crossgen2's own `--partial` works here,
appended to `PublishReadyToRunCrossgen2CompositeExtraArgs`, with profiles supplied as
`PublishReadyToRunPgoFiles` items (they reach crossgen2 as `-m:`).

**`--strip-il-bodies` must be turned off for partial.** The SDK adds it to every composite
build (`PublishReadyToRunStripILBodies`), replacing the IL of compiled methods with
throwing stubs. That is harmless in full mode and fatal in partial mode, where the whole
point is that uncompiled methods still have IL for the interpreter to run.

**No PGO profile reaches crossgen2 on iOS today.** The runtime pack ships
`tools/StandardOptimizationData.mibc`, but it isn't tagged as a `pgodata` asset, so
`PublishReadyToRunUseRuntimePackOptimizationData` (default `true`) picks up nothing. The
composite image is laid out without any profile at all.

That is about the *runtime pack's* profile. A profile we supply ourselves via
`PublishReadyToRunPgoFiles` does reach crossgen2 and does root in full mode - measured
2026-08-09, +3,691 methods including the 1,524 async state-machine boxes. See
[startup-profiling.md](./startup-profiling.md#the-profile-roots-in-a-full-build-too--it-just-doesnt-order-it).

**`_Profiling/merged.mibc` does transfer to `ios-arm64`.** It drives partial mode without a
generic-instantiation failure. (The Windows→Android direction used to crash crossgen2 on
`MemoryPack` instantiations; as of 2026-08-06 that no longer reproduces — Android builds
cleanly on the merged profile.) It is still an Android + Windows recording, so it covers the
shared startup path and knows nothing about `Microsoft.iOS`, the ObjC registrar, or the Apple
audio paths — those would run interpreted, which is precisely why partial is wrong here.

**Excluding most assemblies needs explicit `-r:` references.** For a normal composite build
the SDK passes crossgen2 *zero* references, because every assembly is an input. Exclude
enough of them and crossgen2 can no longer resolve even `System.Runtime`:

```
Failed to load assembly 'System.Runtime'
  at Internal.TypeSystem.Ecma.EcmaType.InitializeBaseType()
  at ILCompiler.DependencyAnalysis.ReadyToRun.CopiedFieldRvaNode.GetRvaData(Int32, Int32&)
  at ILCompiler.ReadyToRunCodegenCompilation.RewriteComponentFile(...)
```

Feeding the excluded set back as `-r:<path>` through the composite extra args fixes it.
(The SDK's own `FilterReadyToRunAssemblies=true` — Debug's "user assemblies interpreted"
mode — never hits this, because it excludes only a couple of dozen assemblies and leaves
the framework in the image.)

### What actually broke

The partial build **starts fine**: cold start to a rendered chat list is unchanged, with
88% of the managed code interpreted. Then **opening a chat hangs** — no crash, no error
barrier, no exception in the WebView console, and the app keeps running. The baseline
built from the same commit opens chats normally, so this is attributable to partial mode.

Root cause was never established, and the "only ours" variant was never run on device at
all. If anyone picks this up: opening a chat is the first substantial block of code the
Android profile never covered, so the natural next step is to force the chat-view
assemblies back into the image and see whether the hang follows the profile coverage.
Reach for the syslog channel first — see [Debugging on a device](#debugging-on-a-device).

### The exclusion set fails differently here than on Android

The `PublishReadyToRunExclude` list that crashes crossgen2 on Android with NETSDK1096
(see [App bundles → Blocked](./app-bundles.md#blocked-crossgen2-crashes-on-a-larger-exclusion-set))
**does not crash it on iOS.** The build succeeds, the bundle is 8 MB smaller, the app
installs and launches and renders the chat list — and it is broken. Same root cause,
silent failure.

The practical consequence: on Apple targets a green build proves nothing about an
exclusion. Anything re-added to that list has to be exercised on device, not just compiled.

### `FilterReadyToRunAssemblies` is a Debug-only knob

`Microsoft.Sdk.R2R.targets` — the Apple-only R2R target, present in the iOS and MacCatalyst
SDK packs and nowhere else in the SDK — defaults it on outside Release:

```xml
<FilterReadyToRunAssemblies Condition="'$(FilterReadyToRunAssemblies)' == '' And '$(Configuration)' != 'Release'">true</FilterReadyToRunAssemblies>
```

`_SelectR2RAssemblies` then adds every **non-NuGet** assembly to `PublishReadyToRunExclude`
("user assembly" = anything without `NuGetPackageId` metadata), so in Debug our own code is
interpreted and only the framework is in the image. It hashes just the *non-user* set, and
`_TouchR2ROutputs` touches the R2R outputs when that hash is unchanged — which is how an
incremental Debug build skips crossgen2 entirely.

**In Release the property is empty and the target never runs**, so setting it to `false`
there does nothing; Release compiles 183/184 assemblies either way. It is recommended as a
workaround on dotnet/macios#26269, but that reporter was running a filtered build; ours
never is.

Both this target and `_ComputeInstructionSetForCrossgen2` are gated on
`'$(RuntimeIdentifiers)' == ''` — *plural*. iOS sets the singular `RuntimeIdentifier` so
both run; Android sets the plural one and silently skips both.

## The composite is compiled for `armv8-a`

Measured 2026-08-09 on `dev`, Release / `net11.0-ios` / `ios-arm64`:

```
_ComputedInstructionSet     = armv8-a
Crossgen2ExtraArgs          = ;--strip-inlining-info;--strip-debug-info;--instruction-set:armv8-a
SupportedOSPlatformVersion  = 16.4
```

`armv8-a` is the ARMv8.0 **baseline**: no LSE atomics (v8.1), no FP16 or dot product (v8.2),
no `LDAPR` (v8.3). Every method in the composite is compiled to it, and there is no tier-1
rejit here to make up the difference later.

`_ComputeInstructionSetForCrossgen2` (in `Microsoft.Sdk.R2R.targets`, on by default, opt out
with `ComputeInstructionSetForReadyToRun=false`) derives it from `SupportedOSPlatformVersion`.
It is a step function, not a slider:

| `SupportedOSPlatformVersion` | `_ComputedInstructionSet` |
|---|---|
| 16.4 (ours) | `armv8-a` |
| 17.0 | `armv8-a` |
| **18.0** | **`armv8.3-a`** |
| 26.0 | `armv8.3-a` |

The cliff is where iOS 18 drops the A11 devices (iPhone 8 / X), leaving A12 Bionic —
ARMv8.3-A — as the floor. Nothing between 16.4 and 18.0 moves it, nothing above 18.0 moves
it further. The flag does reach the composite: `Microsoft.NET.CrossGen.targets:506-507`
hands `Crossgen2ExtraCommandLineArgs` and `Crossgen2CompositeExtraCommandLineArgs` to the
same task.

**Raising the floor to 18.0 is the entire lever, and it costs iOS 16.4/17 support.** What it
buys has *not* been measured — only the flag has. Build both and compare on device before
trading those users away.

## Measuring what runs interpreted

**Every method in a JIT trace is an interpreted method.** There is no JIT on iOS, so a
runtime method-compile event can only mean the interpreter had to build byte code. The
Android `-Mode Jit` recording means "cold JIT" there and means "interpreted" here.

It works because the interpreter shares the JIT's prestub path: `MethodDesc::JitCompileCode`
emits `ETW::MethodLog::MethodJitting` / `MethodJitted` (`prestub.cpp:834,860`) whether
`JitCompileCodeLocked` returned native code or interpreter byte code.

**Not via the perf map.** `PerfMap::LogInterpreterMethod` writes a literal `[Interpreter]`
tag per method, which would drop dsrouter from the loop entirely — but it landed in
dotnet/runtime#129989 on 2026-07-01 and is **not** in preview 6. Verified: `strings` on
`microsoft.netcore.app.runtime.ios-arm64/11.0.0-preview.6.26359.118/.../libcoreclr.dylib`
finds `%s/perf-%d.map` and `PerfMapJitDumpPath`, but no `[Interpreter]`. Re-check after a
runtime bump.

### The recipe

The diagnostic port is baked into the bundle by the SDK — no csproj change needed.
`Xamarin.Shared.props:201-222` turns any of `DiagnosticAddress` / `DiagnosticPort` /
`DiagnosticSuspend` / `DiagnosticListenMode` into `EnableDiagnostics=true` and emits a
`_BundlerEnvironmentVariables Include="DOTNET_DiagnosticPorts"` item. `EnableDiagnostics`
also keeps `EventSourceSupport` and `MetricsSupport` from being trimmed out of an optimized
build (`Xamarin.Shared.Sdk.targets:137,139`).

Both helper scripts live in `tmp/` on the Mac and are **gitignored**, like every other iOS
build script there (`build-ios-dev.sh`, `build-ios-signed.sh`, ...) — they hard-code the
device UDID, keychain path and `DEVELOPER_DIR`. The commands below are therefore given in
full, and are the actual contents.

`tmp/build-ios-trace.sh` adds:

```
-p:EnableDiagnostics=true -p:DiagnosticAddress=127.0.0.1 -p:DiagnosticPort=9000 \
-p:DiagnosticListenMode=listen -p:DiagnosticSuspend=true
```

Confirm it landed — `artifacts/out/linker-cache/main.arm64.mm` should contain
`setenv ("DOTNET_DiagnosticPorts", "127.0.0.1:9000,listen,suspend", 0);`.

Then `tmp/trace-ios.sh`, whose order is the load-bearing part:

```bash
xcrun devicectl device process launch --device $UDID chat.actual.dev.app   # suspends, holds :9000
dotnet-dsrouter ios -v debug &                                             # connects over usbmux
dotnet-trace collect -p <dsrouter-pid> \
  --providers "Microsoft-Windows-DotNETRuntime:0x1C000080018:5" \
  --buffersize 512 --duration 00:00:40 -o ios-interp.nettrace
```

`0x1C000080018` is the Android script's `-Mode Jit` mask. Convert with the repo's own tool
(`dotnet-pgo.cmd` is not executable on the Mac — call the dll):

```bash
dotnet tools/dotnet-pgo/dotnet-pgo.dll create-mibc \
  --trace ios-interp.nettrace --output ios-interp.mibc --compressed \
  --reference "artifacts/out/R2R/*.dll"
dotnet tools/dotnet-pgo/dotnet-pgo.dll dump -i ios-interp.mibc -o ios-interp-dump.txt
```

**Start the tracer before the app and it looks like a runtime bug.** dsrouter dials the
device the moment a diagnostic tool attaches to its IPC socket; if the app is not up yet you
get `Failed USBMuxConnectByPort: device = 11, port = 9000, result = 61` (ECONNREFUSED) and
then an `EndOfStreamException` out of dotnet-trace, and dsrouter will not retry against a
tool that already gave up. `DiagnosticSuspend=true` exists so the app can sit waiting.

The `create-mibc` MVID mismatch warnings are expected, for the same reason as on Android:
the on-device assemblies are IL-stripped and the `-r` set is not.

A build-time note: `--mapcsv` is now on for iOS Release as well as Android, because on
iOS the map is the only way to separate "never compiled, interpreted forever" from noise.

### The three profiles iOS Release feeds crossgen2

`ios.mibc` and `merged.mibc` are recordings — they know which instantiations a
real session actually ran, which nothing static can. `aothelper.mibc` is emitted from the
current tree by `update-aot-helpers.cmd`, from the CodeKeeper type set plus what ActualLab's
proxy keepers and the async machinery construct reflectively.

Regenerate `aothelper.mibc` whenever the keeper set moves; it costs one command and no
device. It is not a replacement for the recordings — on its own it reaches 99.3% of the
methods they cover (2,314 short of 328,443, measured 2026-08-10) — but it does not go stale,
so it backstops them as the code drifts away from the last recording.

Two things it deliberately does not do. It leaves a generic method's own type arguments at
`object`: varying them over every value type in the app tripled the profile to close ~14
methods. And it names no framework generics beyond what our types reach — `JsonTypeInfo<T>`,
comparers, LINQ iterators and friends were measured at +5 MB of bundle to close 187 methods,
because we can identify the definition but not which arguments are live.

### First results (2026-08-09, launch only)

One cold launch to a rendered chat list, 40 s capture, no user interaction:
**2,835 methods ran interpreted.**

| assembly | methods | | assembly | methods |
|---|---:|---|---|---:|
| S.P.CoreLib | 1278 | | ActualChat.Core | 56 |
| System.Text.Json | 459 | | MessagePack | 39 |
| ActualLab.Fusion | 255 | | System.Collections.Immutable | 39 |
| ActualLab.Interception | 212 | | ActualLab.Rpc | 35 |
| System.Linq | 137 | | ActualLab.Fusion.Blazor | 31 |
| ActualLab.Core | 98 | | MemoryPack.Core | 9 |
| Pidgin | 82 | | ActualChat.UI.Blazor | 4 |
| Microsoft.iOS | 64 | | | |

**94% are generic instantiations** (2,663 of 2,835); 882 are instantiated purely over value
types. That is exactly the shape a full-mode image cannot enumerate statically, and the map
confirms it directly:

```
$ grep MethodWithGCInfo ActualChat.r2r.map.csv | grep -o 'RangeMessagePackFormatter[^,]*'
RangeMessagePackFormatter_1<System___Canon>__Deserialize
RangeMessagePackFormatter_1<System___Canon>__Serialize
RangeMessagePackFormatter_1<System___Canon>___ctor
```

Only the shared `__Canon` form is in the image. The `<int64>` instantiation appears in none
of the 324,076 compiled methods, and the trace shows it being built at runtime. Same story
for `StringLikeMessagePackFormatter<HashString>`, `MessagePackByteSerializer<bool>` and
`FeatureDef<bool>`.

This is the one category where rooting from reachable code should help — a fake call *is* a
static enumeration point, which is what full mode lacks. Untested. Verify any such change
against `map.csv` rather than assuming.

### Traps

**The dev provisioning profile has no `com.apple.developer.push-to-talk`.** A dev-signed
device build of current `dev` fails with `error MT7140` because `Entitlements.dev.plist`
requests it. Stripping the key (`plutil -remove "com\.apple\.developer\.push-to-talk"`)
unblocks the build at the cost of PTT in that build — fine for profiling, useless for
profiling the PTT path itself. Fixing it properly means adding the entitlement to the
`chat.actual.dev.app` profile in the portal.

**The share extension gets the diagnostic port too.** `DOTNET_DiagnosticPorts` is written
into both `main.arm64.mm` files, so `App.Maui.IosShareExt` would also try to listen on 9000.
It only runs when sharing, so it has not collided yet.

## How this went wrong once

`RuntimeFeature.IsDynamicCodeSupported` was **`false`** on iOS between the .NET 11 sweep
and its fix. You can see the flip in the two runtimeconfigs:

```
net10 iOS:  "System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported": true
net11 iOS:  "System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported": false
```

**It was our setting, not a platform limit.** Apple forbids JIT, but CoreCLR on iOS
ships an interpreter, and the macios SDK decides dynamic-code support purely from two
MSBuild properties (`Xamarin.Shared.Sdk.targets`):

```xml
<DynamicCodeSupport Condition="... '$(MtouchInterpreter)' == '' And '$(UseInterpreter)' != 'true'
                               And ('$(_PlatformName)' == 'iOS' Or 'tvOS' Or 'MacCatalyst')">false</DynamicCodeSupport>
```

We used to set both — `UseInterpreter=true` with `MtouchInterpreter=-ActualChat`, i.e.
ActualChat assemblies AOT'd and the rest interpreted. The .NET 11 sweep removed them as
Mono-era cleanup. They are **not** Mono-era: the net11 SDK still reads them, and dropping
them flipped the flag. We set `UseInterpreter=true` again, hit #130840, and have now
settled on `false` deliberately rather than by accident — see above for why that is the
right setting and not a regression to the state described here.

The nuance is that `MtouchInterpreter` survives only as a build-time input on CoreCLR —
its actual method filter is discarded. See
[Which `Mtouch*` properties still do something](#which-mtouch-properties-still-do-something-under-coreclr)
below.

**Android was unaffected.** It permits JIT and was already on CoreCLR before the sweep
(`UseCoreClr` + `UseMonoRuntime=false`), so `IsDynamicCodeSupported` stayed `true` — which
is why the failure showed up on iOS alone.

**Mac Catalyst is an open question.** macOS allows JIT, so it probably doesn't need the
interpreter — but the SDK condition above lists `MacCatalyst`, so it likely gets
`DynamicCodeSupport=false` too and would fail the same way. Unverified. If it does, prefer
setting `DynamicCodeSupport=true` there over paying for an interpreter it doesn't need.

### What breaks: MessagePack formatters

`AppMessagePackResolverSettings` registers the Reflection.Emit-based fallbacks
conditionally:

```csharp
if (RuntimeFeature.IsDynamicCodeSupported) {
    standardResolvers.Add(DynamicUnionResolver.Instance);
    standardResolvers.Add(DynamicObjectResolver.Instance);
}
```

`DynamicObjectResolver` manufactures a formatter at runtime for any type that lacks
one. With dynamic code off it isn't registered, so a type without a source-generated or
attribute formatter fails — and the failure is loud but indirect:

```
INF [FCE] FormatterNotRegisteredException, ActualChat.Media.Size2D is not registered
          in resolver: ActualChat.Serialization.Internal.AppMessagePackResolver
INF [FCE] MessagePackSerializationException, Failed to deserialize
          ApiArray<ActualChat.Video.VideoStreamInfo>
INF [FCE] SerializationException, Cannot deserialize inbound call arguments.
ERR [ActualLab.Rpc.RpcClientPeer] Failed to process inbound message: …
ERR [ErrorBoundary] SerializationException: Cannot deserialize inbound call arguments.
ERR [ErrorBarrier] ErrorBarrier VideoPanel activated, error count = 1
```

One missing formatter takes down every RPC call whose argument graph reaches that
type, and surfaces to the user as an error barrier — not as anything mentioning
serialization. Read the chain bottom-up: the barrier names the *component*, the first
`[FCE]` names the *type*.

Two consequences worth internalising:

- **The bug is never iOS-specific, only iOS-visible.** The same gap exists on Android;
  it's silently paid for with runtime codegen per type. Every one of these is also a
  Native AOT blocker on every platform. `Size2D` had carried `[MessagePackObject]` and
  `[Key(...)]` all along — `DynamicObjectResolver` was simply generating it at runtime,
  on every platform, unnoticed.
- **It scales badly.** A single missing formatter produced ~23k first-chance
  exceptions in one short session, because every affected RPC message throws.

Still fix the type properly — give it a real formatter (source-generated,
`[MessagePackFormatter]`, or an explicit registration). The interpreter keeps a missing
formatter from reaching users; it doesn't make the gap go away, and every such gap is
still a Native AOT blocker. Getting them all covered is what makes NativeAOT builds
shippable.

## Which `Mtouch*` properties still do something under CoreCLR

The `Mtouch*` prefix is a Xamarin-era name, which makes the whole family look like
dead weight now that Apple targets run CoreCLR. Most of it isn't. The net11 iOS SDK
still imports the legacy Xamarin targets — `Xamarin.Shared.Sdk.targets:2913-2916`
sets `_TargetsDirectory` to `tools/msbuild/` and imports `Xamarin.iOS.CSharp.targets`
for every `iOSExecutableProject`, which chains to `tools/msbuild/Xamarin.Shared.props`
and `Xamarin.Shared.targets`. So a property that appears nowhere under `targets/` can
still be live.

Audited against `packs/Microsoft.iOS.Sdk.net11.0_26.5/26.5.11720-net11-p6/`.
`UseMonoRuntime` defaults to `false` there (`Xamarin.Shared.Sdk.props:43`), so CoreCLR
is the path that matters.

| Property | Under CoreCLR |
|---|---|
| `MtouchLink` | **Live.** The trimming knob — see *Other deltas*. `Xamarin.Shared.Sdk.Trimming.props:79` |
| `MtouchExtraArgs` | **Live.** Feeds `AppBundleExtraOptions` → `ParseBundlerArguments`. `tools/msbuild/Xamarin.Shared.props:117` |
| `MtouchDebug` | **Live.** Sets `_BundlerDebug`, which drives `DebuggerSupport`, `UseSystemResourceKeys`, `-DDEBUG`. `Xamarin.Shared.Sdk.targets:120` |
| `MtouchNoSymbolStrip` | **Live.** Native symbol strip, incl. the CoreCLR/R2R frameworks. `tools/msbuild/Xamarin.Shared.props:122`, `Xamarin.Shared.targets:3132` |
| `MtouchNoDSymUtil` | **Live.** Same shape, for `dsymutil`. `tools/msbuild/Xamarin.Shared.props:130` |
| `MtouchHttpClientHandler` | **Live.** Picks `NSUrlSessionHandler` / `CFNetworkHandler`; an invalid value is error E7152. `Xamarin.Shared.Sdk.targets:147,659,661,797` |
| `MtouchSdkVersion` | **Live**, obscure — pins the Xcode SDK version. Never reassigned at target time. `tools/msbuild/Xamarin.Shared.props:42` |
| `MtouchArch` | **Live as an error only** — build fails telling you to use `RuntimeIdentifier`. `Xamarin.Shared.Sdk.targets:1283-1287` |
| `MtouchInterpreter` | **Discarded** by the linker; build-time side effects remain. See below |
| `MtouchUseLlvm` | **Mono-only.** Only reaches `mono_use_llvm`, written `if (app.XamarinRuntime == XamarinRuntime.MonoVM)` |
| `MtouchFloat32` | **Mono-only.** An AOT-compiler argument (`Application.AotFloat32`) |
| `MtouchEnableSGenConc` | **Mono-only.** An SGen GC setting |

### `MtouchInterpreter` is accepted, then thrown away

The linker discards it outright — `tools/dotnet-linker/LinkerConfiguration.cs` in
`dotnet/macios`:

```csharp
if (Application.XamarinRuntime != XamarinRuntime.MonoVM && Application.UseInterpreter) {
    Application.Log (4, "The interpreter is enabled, but the current runtime isn't MonoVM. The interpreter settings will be ignored.");
    Application.UnsetInterpreter ();
}
```

Verbosity 4 — you will never see that message in a normal build. So the *method
filter* half of `MtouchInterpreter=-ActualChat` has been inert since we moved to
CoreCLR; there is no CoreCLR equivalent of Mono's per-assembly interpreter selection
anywhere in the SDK.

What survives is the MSBuild side, and only one of those matters. Every other use is
gated on Mono: `_RunAotCompiler` sits inside `<PropertyGroup Condition="'$(UseMonoRuntime)' == 'true'">`
(`Xamarin.Shared.Sdk.targets:1316-1320`), which in turn gates the `sealer` trimmer
optimization (`:770`) and `_IsDedupEnabled` (`:1359`); `_MonoComponent hot_reload`
(`:488`) is only read by `_ComputeMonoComponents`, itself Mono-gated (`:495`); the
hot-reload error (`:2861`) and both `Trimming.props` defaults (`:70,72`) carry
`'$(UseMonoRuntime)' == 'true'` explicitly.

The exception is the one that bit us — `Xamarin.Shared.Sdk.targets:158` has **no**
runtime guard, so setting `MtouchInterpreter` still suppresses `DynamicCodeSupport=false`
on CoreCLR. That is the entire remaining effect of the property on our builds, and
`UseInterpreter=true` reaches the same line (and aliases to `MtouchInterpreter=all` at
`tools/msbuild/Xamarin.Shared.props:190`).

### Setting process environment variables in the app bundle

There *is* a supported mechanism, and the SDK uses it on the CoreCLR path itself —
`Xamarin.Shared.Sdk.props:260-262`:

```xml
<ItemGroup Condition="'$(UseMonoRuntime)' == 'false' And '$(_PlatformName)' != 'macOS' And '$(PublishReadyToRun)' != 'true'">
    <_BundlerEnvironmentVariables Include="DOTNET_ReadyToRun" Value="0" />
</ItemGroup>
```

Adding to that item group is the whole recipe:

```xml
<ItemGroup>
    <_BundlerEnvironmentVariables Include="DOTNET_SomeKnob" Value="1" />
</ItemGroup>
```

The equivalent through `MtouchExtraArgs` is `--setenv:KEY=VALUE`, parsed by
`ParseBundlerArguments` and appended to the same item group
(`tools/msbuild/Xamarin.Shared.targets:2294-2309`). Prefer the item — it skips
shell-style argument splitting and a first-separator (`:` or `=`) split of the value.

The chain from there, none of it runtime-gated:

1. `Xamarin.Shared.Sdk.targets:683` emits
   `@(_BundlerEnvironmentVariables -> 'EnvironmentVariable=Overwrite=%(Overwrite)|%(Identity)=%(Value)')`
   into `_CustomLinkerOptions`.
2. `dotnet-linker`'s `LinkerConfiguration` parses it into `Application.EnvironmentVariables`.
3. `GenerateMainStep` (unconditional) has `tools/common/Target.cs` write a literal
   `setenv("KEY", "VALUE", 1)` per entry into the generated `main.<arch>.mm`, inside
   `xamarin_setup_impl()` — last, *"so that the app developer can override any other
   environment variable we set"*.
4. `runtime/monotouch-main.m` calls `xamarin_setup()` early in `xamarin_main`, **before**
   `xamarin_bridge_setup()` and `xamarin_vm_initialize()`.

So the variables are in the process environment before CoreCLR initialises, which is
exactly why the SDK's own `DOTNET_ReadyToRun=0` works. Whether any particular
`DOTNET_*` knob is honoured is then a dotnet/runtime question — macios only guarantees
it is set in time.

Two lookalikes that are **not** this:

- `RuntimeEnvironmentVariable` → `MlaunchEnvironmentVariables`
  (`Microsoft.Sdk.Mobile.targets:99,115`) only applies to `dotnet run` / mlaunch
  launches. Nothing is written into the bundle.
- The `-setenv=` launch argument parsed in `runtime/monotouch-main.m` is a
  debugger/mlaunch channel, not a shipping one.

Nothing writes environment variables into `Info.plist`; the generated `main.mm` is the
only bundle-baked path.

## Other deltas

**`_ExportSymbolsExplicitly=true` is required.** CoreCLR won't resolve
`DllImport("__Internal")` unless the main executable exports symbols; Mono resolved it
regardless. Without it the app dies on its first P/Invoke with
`DllNotFoundException '__Internal'` out of `Foundation.NSObject`'s cctor. Set in
`App.Maui.csproj`; **not** set on `App.Maui.IosShareExt`, so if the share extension
ever fails to launch on a P/Invoke, start there.

**Trimming is spelled `MtouchLink`.** The macios SDK always runs the trimmer and
rejects an explicit `PublishTrimmed` outright (*"iOS projects do not support setting
'PublishTrimmed'"*), so the knob is `MtouchLink` — `None` / `SdkOnly` / `Full`.

**ReadyToRun is full here, but not because partial is unavailable.** Partial mode works on
Apple targets and was measured on device; we don't ship it, even though Android and Windows
do. See [Shrinking the R2R image, and why we don't](#shrinking-the-r2r-image-and-why-we-dont)
and [startup-profiling.md](./startup-profiling.md).

**The launch surface has to be painted explicitly.** The `UIWindow` and the root view
controller's view are white by default and show for a frame between the launch
storyboard and WebKit's first paint — see [Splash screens](./ui/splash-screen.md).

**The app bundle is two apps.** `ActualChat.app` plus
`PlugIns/ActualChat.App.Maui.IosShareExt.appex` (`chat.actual.dev.app.share`), signed
with its own provisioning profile. A global `-p:CodesignProvision=…` therefore breaks
the build — it applies to every project in the graph and the extension's bundle id
won't match.

## Debugging on a device

Nothing in the app writes a log file on iOS, and `devicectl … --console` streams
nothing for it. Two channels together give full coverage:

**.NET side** — the device syslog carries our `ILogger` output, including every
`[FCE]`:

```bash
idevicesyslog -u <udid> | grep -aE 'ActualChat\[[0-9]+\] <'
```

Note the filter: lines with a parenthesised subsystem (`ActualChat(WebKit)`,
`ActualChat(CoreHaptics)`) are system noise; ours have none.

**JS side** — the WebView console via `ios_webkit_debug_proxy`.
`MauiWebView.MaciOS.cs` sets `webView.Inspectable = true` on iOS 16.4+ with no
`#if DEBUG` guard, so Release builds are inspectable. The app must be **foregrounded**
(backgrounded, the page list is empty — indistinguishable from Web Inspector being
off), and iOS 26's WebKit needs commands wrapped in `Target.sendMessageToTarget`
rather than flat CDP.

Reach for the syslog first: a failure that renders as an error barrier is almost
always .NET-side, and the JS console will show nothing useful.

## Profiling CPU on a device

`xctrace` with the Time Profiler template, system-wide. Three details are load-bearing
and every one of them has cost a wasted capture:

```bash
# 1. Warm the tunnel FIRST, or the recording silently produces a 0-byte bundle
xcrun devicectl device info details --device $UDID >/dev/null
# 2. Address the device by NAME (note the curly apostrophe), not by UDID
xcrun xctrace record --device "Alexander’s iPhone" --template "Time Profiler" \
  --all-processes --time-limit 30s --no-prompt --output /tmp/cap.trace
# 3. Export needs the xpath quoted - zsh globs the brackets otherwise
XP="/trace-toc/run[@number=\"1\"]/data/table[@schema=\"time-profile\"]"
xcrun xctrace export --input /tmp/cap.trace --xpath "$XP" > /tmp/cap.tp.xml
```

**Always assert the export size.** Without the tunnel warm-up `xctrace record` fails, still
exits 0, and leaves an empty `.trace`; a wrapper that swallows stderr will report six
successful captures that contain nothing. Fail the run if the XML is under ~100 KB.

Symbolication is `atos` against `~/Library/Developer/Xcode/iOS DeviceSupport/<ver>/Symbols`.
Our own frames and WebKit's resolve; **`audiomxd`'s system dylibs do not**, so stack-level
analysis inside the audio daemon is not available without more setup. Thread names and
per-binary attribution still are, and were enough.

`sample-time` is the schema *tag*; the column mnemonic is `time`, in nanoseconds. Bucketing
by the wrong one yields all-zero bins.

### `xctrace` symbolication fails silently — symbolicate the export yourself

`xctrace` sometimes records a trace whose frames are bare addresses (`0x12021f695`) with the
binary resolving to `?`. It exits 0, the export is full size, and every symbol-matching
analysis then reports **0 ms for everything** — a broken capture that reads exactly like a
free lunch. On one afternoon this hit two of four arms in a run, then every capture after
it; it is not tied to the workload, the app, the screen state, or the arm. `xctrace
symbolicate` cannot repair it after the fact ("No dSYMs were found or relevant to this
trace"), and neither can re-exporting.

It does not matter, because **the export still carries everything needed to resolve the
addresses**: each `<binary>` element keeps its `name`, `UUID`, `path` and — crucially —
`load-addr`, and each `<frame>` keeps `addr`. So batch the addresses per binary through
`atos` against the DeviceSupport symbol cache:

```bash
atos -o "$SYMROOT/System/Library/PrivateFrameworks/WebCore.framework/WebCore" \
     -arch arm64e -l 0x1b6b24000   # load-addr from the <binary> element
```

`tmp/ios-profiling/symtime2.py` on the Mac does this: it resolves only the binaries you ask
about (WebCore/WebKit/JavaScriptCore by default), so one `atos` call per binary covers a
whole trace. It took a capture from 96% unresolved to 5%, and on a trace that `xctrace` had
symbolicated correctly it reproduced that tool's numbers **to the digit** (801 ms / 8.39%),
which is what makes it trustworthy.

Two guards worth keeping in any analysis script, because both failure modes are silent:

- **Report the unresolved fraction and refuse to print numbers above ~20%.** A capture that
  cannot be symbolicated must fail loudly, not score 0.00% on every pattern.
- **Pin the analysis to one pid.** Matching the process by substring silently sums two
  `WebContent` instances when a stale container is alive - see hygiene below.

### Measurement hygiene

- **Assert exactly one `ActualChat.app/ActualChat` process before trusting any number.** iOS
  keeps older bundle containers around and relaunches them; a stale one silently doubled
  WebContent several times in one day. Check `devicectl device info processes` and kill
  anything whose container UUID isn't the one you just installed.
- **A live call is not a repeatable workload.** Two captures of an *identical* build differed
  by 10% (1.235 vs 1.365 cores) - larger than most effects worth chasing.
- **Most rendering work needs DOM churn, not a call.** Style-invalidation costs are paid per
  mutation, so a call is only a convenient mutation firehose. A driver injected over the
  WebView debugger - append/remove one off-screen node at 20 Hz via `setInterval`, so it keeps
  running after the debugger detaches - reproduces the same code path, holds the rate identical
  across arms, and needs nobody holding a phone. `tmp/ios-profiling/arm.sh` does this.
- **Prefer a same-call A/B** over comparing builds. For anything CSS- or JS-level, inject the
  variant over the WebView debugger instead of rebuilding: `wvexec` connects, evaluates and
  exits, and an injected `<style>` persists, so nothing is attached while the profiler runs.
  Do **not** leave Web Inspector attached during a capture.
- **Interleave arms and sample the same config more than once.** In one run the same
  configuration produced 591 ms and 1422 ms on the metric under test - a 2.4x spread that
  dwarfed the effect. Three same-config samples measure the noise floor directly.
- `callers.py`-style weight sums and sample-count sums disagree (weight vs 1 ms per sample).
  Use one tool consistently and compare **percentages of the same denominator**, not absolute
  ms across tools.

## Reading `audiomxd`

On iOS 18+ `mediaserverd` was split up and **`audiomxd` is the audio capture and DSP server**.
Hot `libvDSP` / `libAudioDSP` / `AudioToolboxCore` inside it means a live audio graph, not
something exotic.

Its threads are named `audio IO: VAD [xxxx] AggDev N` - VAD is **Virtual Audio Device**, not
voice-activity detection. The four-CCs are undocumented by Apple but decode from CoreAudio's
own `VirtualAudio_PlugIn` device dumps:

| code | direction | physical device |
|---|---|---|
| `vdef` | output | **Speaker** |
| `vspd` | input | **built-in microphone** |
| `vzzz` | output | **Actuator** (Taptic Engine) |
| `vcal` | duplex | Baseband Voice |
| `vhaw` | input | "Hawking" |
| `vlqd` | duplex | unknown |

**`AggDev N` is a creation counter, not an identity.** Low, stable numbers (2, 4, 7) are
boot-time devices - `vzzz` is AggDev 2 on unrelated hardware too. `vdef` is rebuilt on every
route-configuration change, so its number climbs every call. **A climbing AggDev number is
not evidence of a leak**; several hours were spent believing it was. A real leak would show
several `vdef` aggregates alive at once.

Two log lines tell you whether audio actually stopped, and both come through
`idevicesyslog` at Notice level:

```
HALB_PowerAssertion::Release ... 'com.apple.audio.VAD [vspd] AggDev 7.context...'
VirtualAudio_IONotificationManager: new I/O running state = 0, previous = 1
```

If you never see `IORunning` go to 0, something in the process is still holding the graph.
Deeper detail (`StartIOProcID` / `StopIOProcID`, which would name the exact leaking IO proc)
is debug-level and needs Apple's Audio debug profile installed on the device.

### What holds an audio device open

- **`AVAudioEngine` holds a virtual audio device with a live IO thread until it is
  deallocated.** `Pause()`, `Stop()` and deactivating the session do not give it back. Build
  on first use, tear down when the last consumer goes.
- **The input node is not released with the engine.** Unlike mixer and player nodes, nothing
  disposes it, so the managed peer outlives the engine as the native node's last owner.
  Voice processing must be switched off *before* release - it can only be toggled while the
  node still belongs to a live engine.
- **`CHHapticEngine` keeps the entire audio graph running.** Created without an audio session
  it makes its own, and while it runs it holds CoreAudio's global I/O running state - the
  speaker and microphone devices stay live too, not just the actuator. `autoShutdownEnabled`
  is off by default and `Dispose()` on your wrapper won't stop it. This cost 0.33 cores of
  `audiomxd` whenever the app sat idle after a call, and was the single largest CPU consumer
  on the device. Stop the engine when nothing is vibrating.
- **The orange microphone dot only indicates privacy-visible capture.** A prepared-but-muted
  graph runs without lighting it, so "no dot" does not mean "no audio IO".

To prove where a cost lives: kill the app (does it vanish?), fresh-launch without exercising
the feature (is it absent?), then exercise it. That three-point chain localises an owner far
faster than reasoning about the code.

### A configuration change stops the engine, and `start()` does not repair it

Anything that changes the hardware format or the route under a running `AVAudioEngine` - a
session category switch, disabling voice processing, AirPods flipping between HFP and A2DP -
makes AVFoundation log `iounit configuration changed > stopping the engine` and post
`AVAudioEngineConfigurationChange`. Note **`stop`**, not `pause`: the graph comes down with it.

Restarting the engine is not enough. Measured on device, stopping a recording while a message
was playing:

| attempted repair | result |
|---|---|
| `engine.Start()` alone | engine runs, renders silence |
| reading `MainMixerNode` | no effect - it does **not** rebuild the connection |
| `player.Play()` again | no effect; `Playing` still reports `true`, so it isn't even a signal |
| **`engine.Connect(player, MainMixerNode, format)`** | **sound returns** |

So the repair is the one a freshly built player node performs anyway - which is why the symptom
was "silence until the next message", the next message being the thing that ran `ConnectToMainMixer`.
`AudioEngine.Reconnect()` does this for every live player node, then restates `Play()` on each.

### An interruption that never ends wedges all audio

`AVAudioSessionInterruptionType.Began` is not guaranteed a matching `Ended` - a Bluetooth device
connecting produced a `Began` with nothing after it, for the life of the process. Treating the
flag as a latch meant every mode change was deferred forever: no playback, and a microphone whose
`InstallTapOnBus` failed with `IsFormatSampleRateAndChannelCountValid(format)` on every retry,
because the session had never been activated and the input node reported 0 Hz. Only killing the
app cleared it. The flag now expires (see `InterruptionEndTimeout`) rather than waiting for a
notification that may not come.

## WebKit rendering cost during a call

WebContent is **~94% main thread** - there is no large hidden worker bucket. Inclusive
breakdown from a symbolicated 30s device profile, buckets nest so they overlap:

| bucket | % of main thread |
|---|---|
| `Document::updateLayout` | 61.5 |
| `RenderLayerCompositor` | 36.0 |
| `Style::TreeResolver` | 17.1 |
| `collectTouchEventRects` | 8.8 |
| **`performLayout` (real layout)** | **7.2** |

**Real layout is 7.2%; the compositing update after it is 36% - 5x the layout itself**,
because it walks ~800 RenderLayers (53% of elements, ~6 per chat message). Layer *count* is
the lever, not layout.

Things that did **not** work, so they aren't retried:

- **Deferring Blazor render batches onto a fixed step.** Measured three ways; none beat not
  deferring. Coalescing does cut layout+style work, but holding writes back puts them nearer
  the reads that follow and converts scheduled layout into *forced* layout by at least as
  much. See `render-sync.ts`, which keeps the mechanism disabled with the numbers.
- **`steps()` does not reduce style invalidation.** Proven in isolation: stepped and
  continuous animations both invalidate style at vsync (119.8/s). It does reduce layout and
  paint to the value-change rate.
- **Every SVG transform variant invalidates layout in Blink** - CSS `transform` with
  `transform-box: fill-box` or `view-box`, the presentation attribute, and
  `will-change: transform` all measured the same. Only an HTML element avoids it.
- **Removing the two `body.device-ios :has(...)` rules** changed nothing on device. They were
  obsolete and went anyway, but they were not the `:has()` cost.

### `:has()` — the cost is proving absence, and a presence class removes all of it

`matchHasPseudoClass` was **8.4% of WebContent's main thread** in a live call (801 ms of
9549 ms). It is carried by rules whose subject is `body` and whose argument is a *descendant*
(`body:has(.video-panel:not(.panel-hidden))`). WebKit's `Element` has only sibling-direction
`:has()` bits, no descendant equivalent, so `StyleInvalidator` walks the ancestor chain and
runs a real match on **every** mutation. Blink has breadcrumb bits, which is why Chrome shows
nothing here and why this had to be measured on device.

The expensive case is the rule that *fails*: `.video-panel` is absent in an audio-only
session, so each invalidation walks the whole subtree to prove a negative. WebKit evaluates a
compound left-to-right, so **one class on `body` short-circuits the match before `:has()`
runs** — `video-panel.ts` adds `has-video-panel` while a panel exists, and the three rule
sites are now `body.has-video-panel:has(...)`.

ABAB in a live call, one build, one page, arms toggled by adding/removing the class over the
WebView debugger (30 s arms, main-thread totals within 9.1-10.3 s of each other):

| arm | `matchHasPseudoClass` | `HasSelectorFilter` |
|---|---|---|
| guard bypassed | 801 ms / 8.39% | 241 ms |
| **guard active** | **273 ms / 3.17%** | 144 ms |
| guard bypassed | 662 ms / 6.40% | 152 ms |
| **guard active** | **279 ms / 3.06%** | 153 ms |

So the guard **halves** it - ~730 ms → ~276 ms per 30 s - it does not remove it. The residual
~3.1% is the other ~91 container-subject descendant rules (`.list-view-layout`,
`.layout-header`, …), which still walk on mutations elsewhere in the tree.

**Beware the synthetic driver here.** Under a 20 Hz driver appending one node inside
`.chat-view`, the guarded arms measured *exactly* 0 ms, twice. That is real but misleading:
invalidation only walks the mutated node's **ancestors**, so a single mutation site exercises
only the subjects above it - `body`, i.e. exactly the rules being guarded. It proves the
guard works and is perfectly repeatable, but it cannot see the rules a real call hits, and
reading it as "eliminated" overstates the fix by 2x. Use the driver for a clean yes/no on one
rule set; quote the in-call numbers for what is actually saved.

A related build-level trap: `:not(.hidden)` inside a Tailwind-built selector made the build
expand `.hidden` into the full selector text of all 453 rules applying it - 223 copies of one
rule, several dragging an unrelated `:has()` along. `.hidden` is `display:none` and already
wins, so the `:not()` was pure cost.

The one ordering rule worth internalising: **a write scheduled among other code's reads makes
each of those reads force a synchronous layout.** One such inversion measured 1461 ms of
forced layout per 30 s call. `fast-raf.ts` exists to make that impossible - every read
registered for a frame runs before every write, across all frequencies due together.
