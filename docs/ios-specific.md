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

**ReadyToRun is always full, never partial.** MAUI's `_MauiPublishReadyToRunPartial`
(which appends `--partial` to crossgen2) is gated on
`TargetPlatformIdentifier == 'android'`, so it can't affect Apple targets.

**The launch surface has to be painted explicitly.** The `UIWindow` and the root view
controller's view are white by default and show for a frame between the launch
storyboard and WebKit's first paint — see [Splash screens](./splash-screen.md).

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
